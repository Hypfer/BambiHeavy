using System.Diagnostics;
using BambiHeavy.Capture;
using BambiHeavy.Edge;
using BambiHeavy.Models;
using BambiHeavy.Pipeline;
using BambiHeavy.Types;
using MQTTnet;

namespace BambiHeavy.Services;

public class BambiService
{
    private const int CaptureTimeoutMs = 2;
    private const int StatusInterval = 120;
    private const int PreviewInterval = 60;
    private const int KeepAliveMs = 4000;
    private const int PipelineFps = 60;
    private readonly List<LightState> _currentLightStates = new();

    private readonly Dictionary<ScreenZone, AdaptiveIntegrator> _integrators = new();
    private readonly Stopwatch _keepAliveSw = new();
    private readonly Stopwatch _networkSw = new();
    private readonly int _pipelineFps;

    private readonly Stopwatch _pipelineSw = new();

    private readonly List<ScreenZone> _screenZones;
    private readonly object _settingsLock = new();
    private readonly object _shutdownLock = new();
    private PipelineStage _activeStages;
    private IScreenCapture? _capture;

    private CancellationTokenSource? _cts;
    private BambiStyle _currentStyle;
    private bool _dirtySettings;
    private uint _frameCounter;

    private double _globalBrightnessLimit;
    private Rectangle? _lastScreenBounds;
    private List<LightState> _lastSentStates = new();

    private bool _mqttCleanupDone;
    private IMqttClient? _mqttClient;
    private MqttClientFactory? _mqttFactory;
    private bool _needResetIntegrators;
    private int _networkFps;

    // Pipeline settings
    private PipelineSettings _pipelineSettings;
    private Task? _pipelineTask;
    private Light? _proxyLight;
    private bool _started;
    private int _unchangedCount;

    public BambiService(List<ScreenZone> screenZones, AppSettings settings)
    {
        _screenZones = screenZones;
        _proxyLight = screenZones.FirstOrDefault()?.Lights.FirstOrDefault();
        _pipelineFps = PipelineFps;
        _networkFps = settings.NetworkFps;
        _globalBrightnessLimit = settings.GlobalBrightnessLimit;
        _currentStyle = settings.ActiveStyle;
        _pipelineSettings = settings.Pipeline;
        _activeStages = ComputeActiveStages(settings.Pipeline);
        _pipelineSw.Start();
        _networkSw.Start();
        _keepAliveSw.Start();
    }

    public bool IsMqttConnected => _mqttClient?.IsConnected ?? false;

    public event Action<bool, double, double>? StatusChanged;
    public event Action<Rgb, Rgb, Rgb, Rgb, Rectangle, (int, int, int, int)>? ColorPreviewUpdated;
    public event Action<Exception>? Error;
    public event Action<double, double, double, double, double, double>? StatsUpdated;
    public event Action<bool>? MqttConnectionChanged;

    public void Start()
    {
        if (_started)
            return;

        var brokerUrl = Config.Settings.MqttBrokerUrl;
        var brokerPort = Config.Settings.MqttBrokerPort;

        if (string.IsNullOrWhiteSpace(brokerUrl))
        {
            Error?.Invoke(new InvalidOperationException(
                "MQTT broker URL is not configured. Please configure it in Settings before starting."));
            return;
        }

        _started = true;
        _mqttCleanupDone = false;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        StatusChanged?.Invoke(true, 0, 0);

        _mqttFactory = new MqttClientFactory();
        _mqttClient = _mqttFactory.CreateMqttClient();
        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithCleanSession()
            .WithTcpServer(brokerUrl, brokerPort);

        if (Config.Settings.MqttUseTls)
            optionsBuilder.WithTlsOptions(o => o.UseTls());

        if (!string.IsNullOrEmpty(Config.Settings.MqttUsername))
            optionsBuilder.WithCredentials(Config.Settings.MqttUsername, Config.Settings.MqttPassword);

        if (!string.IsNullOrEmpty(Config.Settings.MqttClientId))
            optionsBuilder.WithClientId(Config.Settings.MqttClientId);

        var options = optionsBuilder.Build();
        _mqttClient.ConnectAsync(options, token).Wait(token);
        MqttConnectionChanged?.Invoke(true);

        _capture = CreateScreenCapture();
        var screenBounds = _capture.GetPrimaryScreenBounds();
        _lastScreenBounds = screenBounds;

        _integrators.Clear();
        foreach (var zone in _screenZones)
        {
            var integrator = new AdaptiveIntegrator();
            integrator.UpdateSettings(_pipelineSettings);
            _integrators[zone] = integrator;
        }

        _lastSentStates = new List<LightState>();
        _unchangedCount = 0;
        _frameCounter = 0;
        _keepAliveSw.Restart();

        _pipelineTask = Task.Run(() => PipelineLoop(token), token);
        _ = _pipelineTask.ContinueWith(t =>
        {
            if (t is { Exception: not null })
                Error?.Invoke(t.Exception!.InnerException ?? t.Exception!);
        }, TaskContinuationOptions.NotOnCanceled);
    }

    public void Stop()
    {
        if (!_started)
            return;

        _started = false;
        _cts?.Cancel();
        _cts = null;

        // Wait for pipeline task to complete (including finally-block MQTT cleanup)
        _pipelineTask?.Wait(5000);
        _pipelineTask = null;

        _capture?.Dispose();
        _capture = null;

        StatusChanged?.Invoke(false, 0, 0);
        MqttConnectionChanged?.Invoke(false);
    }

    public void ApplySettings(AppSettings settings)
    {
        lock (_settingsLock)
        {
            var styleChanged = settings.ActiveStyle != _currentStyle;
            _globalBrightnessLimit = settings.GlobalBrightnessLimit;
            _networkFps = settings.NetworkFps;
            _currentStyle = settings.ActiveStyle;
            Config.ActiveStyle = settings.ActiveStyle;
            _needResetIntegrators = styleChanged;
            _dirtySettings = true;

            // Update pipeline settings
            _pipelineSettings = settings.Pipeline;
            _activeStages = ComputeActiveStages(settings.Pipeline);
            foreach (var integrator in _integrators.Values)
                integrator.UpdateSettings(settings.Pipeline);

            // Rebuild zones so per-light brightness & assignments are current
            var newZones = ScreenZoneBuilder.BuildFromSettings();
            _proxyLight = newZones.FirstOrDefault()?.Lights.FirstOrDefault();
            // Preserve integrator state for zones that still exist; drop removed ones
            var existingZoneNames = newZones.Select(z => z.Name).ToHashSet();
            foreach (var zone in _screenZones.Where(z => !existingZoneNames.Contains(z.Name)))
                _integrators.Remove(zone);
            // Add integrators for new zones
            foreach (var zone in newZones)
                if (!_integrators.ContainsKey(zone))
                {
                    var integrator = new AdaptiveIntegrator();
                    integrator.UpdateSettings(settings.Pipeline);
                    _integrators[zone] = integrator;
                }

            _screenZones.Clear();
            _screenZones.AddRange(newZones);
        }
    }

    private void PipelineLoop(CancellationToken token)
    {
        var targetTicks = Stopwatch.Frequency / _pipelineFps;
        var networkInterval = 1000.0 / _networkFps;
        var lastFrameTimeMs = Stopwatch.GetTimestamp();

        var frameCounter = 0;
        var previewCounter = 0;
        var statsCounter = 0;

        var leftPreview = Rgb.Black;
        var rightPreview = Rgb.Black;
        var topPreview = Rgb.Black;
        var bottomPreview = Rgb.Black;

        double captureMs = 0, edgeMs = 0, pipeMs = 0, sendMs = 0;
        var fpsTimerSw = new Stopwatch();
        fpsTimerSw.Start();
        var networkFramesSent = 0;

        var partSw = new Stopwatch();
        var totalSw = new Stopwatch();
        totalSw.Start();

        try
        {
            var allLights = _screenZones.SelectMany(z => z.Lights).ToList();
            var totalLightCount = allLights.Count;

            if (allLights.Count > 0)
            {
                EntertainmentProtocol.StopAllLights(_mqttClient!, allLights, _mqttFactory!).Wait(token);
                Thread.Sleep(300);
                EntertainmentProtocol.StartAllLights(_mqttClient!, allLights, _mqttFactory!).Wait(token);
            }

            while (!token.IsCancellationRequested)
            {
                totalSw.Restart();
                try
                {
                    TuningProfile profile;
                    PipelineStage activeStages;
                    PipelineSettings pipelineSettings;
                    lock (_settingsLock)
                    {
                        if (_dirtySettings)
                        {
                            _dirtySettings = false;
                            allLights = _screenZones.SelectMany(z => z.Lights).ToList();
                            totalLightCount = allLights.Count;
                        }

                        if (_needResetIntegrators)
                        {
                            _needResetIntegrators = false;
                            foreach (var integrator in _integrators.Values)
                                integrator.Reset();
                            ContentBoundsDetector.Reset();
                        }

                        profile = GetProfile();
                        activeStages = _activeStages;
                        pipelineSettings = _pipelineSettings;
                    }

                    if (_capture == null)
                        break;

                    var screenBounds = _capture.GetPrimaryScreenBounds();

                    if (_lastScreenBounds.HasValue && !_lastScreenBounds.Value.Equals(screenBounds))
                    {
                        foreach (var integrator in _integrators.Values)
                            integrator.Reset();
                        ContentBoundsDetector.Reset();
                        _lastScreenBounds = screenBounds;
                    }

                    partSw.Restart();
                    using var frame = _capture.Capture(screenBounds, CaptureTimeoutMs);
                    captureMs = partSw.Elapsed.TotalMilliseconds;

                    partSw.Restart();
                    var contentBounds = ContentBoundsDetector.Detect(frame, screenBounds);
                    var grid = TileColorExtractor.Extract(frame, screenBounds, contentBounds);
                    edgeMs = partSw.Elapsed.TotalMilliseconds;

                    partSw.Restart();
                    _currentLightStates.Clear();

                    // Aggregate bypass stats across zones
                    var totalBypassFrames = 0;
                    var totalFrames = 0;

                    foreach (var zone in _screenZones)
                    {
                        var strip = zone.Name switch
                        {
                            "left" => grid.Left,
                            "right" => grid.Right,
                            "top" => grid.Top,
                            "bottom" => grid.Bottom,
                            _ => grid.Left
                        };

                        var rawColor = strip.ContentAverage();
                        var (r, g, b) = ColorPipeline.ProcessPreIntegration(rawColor, profile, activeStages);

                        // Zero black input before integration
                        if (ColorPipeline.IsBlack(r, g, b))
                            r = g = b = 0;

                        _integrators[zone].Apply(ref r, ref g, ref b, profile, pipelineSettings);

                        var adjusted = AmbientPerception.Apply(r, g, b);
                        var zoneOutput = RgbToXy.Apply(adjusted.r, adjusted.g, adjusted.b);

                        foreach (var light in zone.Lights)
                        {
                            var finalBri = (ushort)Math.Round(
                                zoneOutput.Brightness * _globalBrightnessLimit * light.Brightness);
                            finalBri = (ushort)Math.Clamp((double)finalBri, 0, 2047);
                            _currentLightStates.Add(new LightState(light.ShortAddress, finalBri, zoneOutput.X,
                                zoneOutput.Y));
                        }

                        if (zone.Name == "left")
                            leftPreview = new Rgb((byte)Math.Round(r * 255), (byte)Math.Round(g * 255),
                                (byte)Math.Round(b * 255));
                        else if (zone.Name == "right")
                            rightPreview = new Rgb((byte)Math.Round(r * 255), (byte)Math.Round(g * 255),
                                (byte)Math.Round(b * 255));
                        else if (zone.Name == "top")
                            topPreview = new Rgb((byte)Math.Round(r * 255), (byte)Math.Round(g * 255),
                                (byte)Math.Round(b * 255));
                        else if (zone.Name == "bottom")
                            bottomPreview = new Rgb((byte)r, (byte)g, (byte)b);

                        // Collect bypass stats
                        var (bypassFrames, frames) = _integrators[zone].GetBypassStats();
                        totalBypassFrames += bypassFrames;
                        totalFrames += frames;
                    }

                    pipeMs = partSw.Elapsed.TotalMilliseconds;

                    partSw.Restart();
                    sendMs = 0;

                    if (_currentLightStates.Count > 0 && _networkSw.Elapsed.TotalMilliseconds >= networkInterval)
                    {
                        _networkSw.Restart();

                        var changed = !LightStatesEqual(_currentLightStates, _lastSentStates);
                        if (changed)
                        {
                            _unchangedCount = 0;
                            _lastSentStates = CloneList(_currentLightStates);
                        }
                        else
                        {
                            _unchangedCount++;
                        }

                        var shouldSend = changed || _unchangedCount <= 3 ||
                                         _keepAliveSw.ElapsedMilliseconds >= KeepAliveMs;

                        if (shouldSend && _mqttClient != null && _proxyLight != null)
                        {
                            _keepAliveSw.Restart();
                            networkFramesSent++;

                            var smoothing = EntertainmentProtocol.CalculateSmoothing(_networkFps);
                            _ = EntertainmentProtocol.SendEntertainmentChunk(
                                _mqttClient, _proxyLight, _currentLightStates, smoothing, _frameCounter);
                            _frameCounter++;
                        }

                        sendMs = partSw.Elapsed.TotalMilliseconds;
                    }

                    var nowMs = Stopwatch.GetTimestamp();
                    lastFrameTimeMs = nowMs;

                    frameCounter++;
                    if (frameCounter >= StatusInterval)
                    {
                        frameCounter = 0;
                        var secondsPassed = fpsTimerSw.Elapsed.TotalSeconds;
                        var netFps = secondsPassed > 0
                            ? networkFramesSent / secondsPassed
                            : 0;
                        var effectivePipeFps = secondsPassed > 0
                            ? StatusInterval / secondsPassed
                            : 0;

                        StatusChanged?.Invoke(true, effectivePipeFps, netFps);
                    }

                    previewCounter++;
                    if (previewCounter >= PreviewInterval)
                    {
                        previewCounter = 0;
                        ColorPreviewUpdated?.Invoke(leftPreview, rightPreview, topPreview, bottomPreview, screenBounds,
                            contentBounds);
                    }

                    statsCounter++;
                    if (statsCounter >= 120)
                    {
                        statsCounter = 0;
                        var effectiveNetFps = fpsTimerSw.Elapsed.TotalSeconds > 0
                            ? networkFramesSent / fpsTimerSw.Elapsed.TotalSeconds
                            : 0;
                        var bypassPct = totalFrames > 0
                            ? totalBypassFrames * 100.0 / totalFrames
                            : 0;
                        StatsUpdated?.Invoke(captureMs, edgeMs, pipeMs, sendMs, effectiveNetFps, bypassPct);
                        networkFramesSent = 0;
                        fpsTimerSw.Restart();
                    }

                    while (totalSw.ElapsedTicks < targetTicks)
                    {
                        if (token.IsCancellationRequested) break;

                        var remainingTicks = targetTicks - totalSw.ElapsedTicks;
                        var remainingMs = remainingTicks * 1000 / Stopwatch.Frequency;

                        if (remainingMs > 1)
                            Thread.Sleep(1);
                        else
                            break;
                    }
                }
                catch (OutOfMemoryException)
                {
                    foreach (var integrator in _integrators.Values)
                        integrator.Reset();
                    ContentBoundsDetector.Reset();
                    Thread.Sleep(2000);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Error?.Invoke(ex);
                    Thread.Sleep(100);
                }
            }
        }
        finally
        {
            CleanupMqtt();
        }
    }

    private void CleanupMqtt()
    {
        lock (_shutdownLock)
        {
            if (_mqttCleanupDone)
                return;
            _mqttCleanupDone = true;
        }

        var client = _mqttClient;
        var factory = _mqttFactory;
        if (client == null || factory == null || !client.IsConnected)
        {
            _mqttClient = null;
            _mqttFactory = null;
            return;
        }

        try
        {
            var allLights = _screenZones.SelectMany(z => z.Lights).ToList();
            var offState = allLights.Select(l => new LightState(l.ShortAddress, 0, 0, 0)).ToList();
            if (offState.Count > 0)
            {
                try
                {
                    EntertainmentProtocol
                        .SendEntertainmentChunk(client, _proxyLight!, offState, 0x0100, _frameCounter)
                        .Wait(2000);
                    Thread.Sleep(150);
                }
                catch
                {
                }

                try
                {
                    EntertainmentProtocol.StopAllLights(client, allLights, factory, _frameCounter)
                        .Wait(2000);
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        try
        {
            client.DisconnectAsync().Wait(2000);
        }
        catch
        {
        }

        _mqttClient = null;
        _mqttFactory = null;
    }

    private static bool LightStatesEqual(List<LightState> a, List<LightState> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
            if (!a[i].Equals(b[i]))
                return false;
        return true;
    }

    private static List<LightState> CloneList(List<LightState> source)
    {
        var result = new List<LightState>(source.Count);
        foreach (var item in source)
            result.Add(item);
        return result;
    }

    private static IScreenCapture CreateScreenCapture()
    {
#if WINDOWS
        return new WindowsScreenCapture();
#elif LINUX
        return new LinuxScreenCapture();
#else
        throw new PlatformNotSupportedException($"Unsupported platform: {Environment.OSVersion}");
#endif
    }

    private TuningProfile GetProfile()
    {
        return Config.GetProfile();
    }

    private static PipelineStage ComputeActiveStages(PipelineSettings ps)
    {
        var stages = PipelineStage.None;
        if (ps.GainEnabled) stages |= PipelineStage.Gain;
        if (ps.CompressEnabled) stages |= PipelineStage.Compress;
        if (ps.SaturationEnabled) stages |= PipelineStage.Saturation;
        if (ps.NoiseGateEnabled) stages |= PipelineStage.NoiseGate;
        return stages;
    }
}