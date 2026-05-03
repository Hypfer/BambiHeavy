using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reflection;
using System.Windows.Input;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Threading;
using BambiHeavy.Converters;
using BambiHeavy.Models;
using BambiHeavy.Services;
using BambiHeavy.Types;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BambiHeavy.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly MqttDiscoveryService _discoveryService;

    private readonly AppSettings _settings;

    // Appearance
    [ObservableProperty] private BambiStyle _activeStyle;

    // Lights — single source of truth
    [ObservableProperty] private ObservableCollection<LightMapping> _allLights;
    [ObservableProperty] private Rgb _bottomPreviewColor;
    [ObservableProperty] private string _bottomPreviewHex;
    [ObservableProperty] private int _bypassCoefficient;
    [ObservableProperty] private bool _compressEnabled;
    [ObservableProperty] private Thickness _contentMargin;
    [ObservableProperty] private string _discoveryStatus = "";
    [ObservableProperty] private string _filterText = "";

    // Pipeline settings
    [ObservableProperty] private bool _gainEnabled;
    [ObservableProperty] private double _globalBrightnessLimit;
    [ObservableProperty] private bool _integrationEnabled;

    [ObservableProperty] private bool _isDiscovering;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isTestingConnection;

    // Preview
    [ObservableProperty] private Rgb _leftPreviewColor;
    [ObservableProperty] private string _leftPreviewHex;
    [ObservableProperty] private int _mqttBrokerPort;

    // Connection
    [ObservableProperty] private string _mqttBrokerUrl;
    [ObservableProperty] private string _mqttClientId;
    [ObservableProperty] private string _mqttConnectionError = "";
    [ObservableProperty] private MqttConnectionStatus _mqttConnectionStatus;
    [ObservableProperty] private string _mqttPassword;
    [ObservableProperty] private string _mqttUsername;
    [ObservableProperty] private bool _mqttUseTls;
    [ObservableProperty] private int _networkFps;
    [ObservableProperty] private bool _noiseGateEnabled;

    // Snapshot of original pipeline values (set on ResetFromSettings)
    private bool _origGainEnabled,
        _origCompressEnabled,
        _origSaturationEnabled,
        _origNoiseGateEnabled,
        _origIntegrationEnabled;

    private bool _origTransientBypassEnabled;
    private int _origTransientThreshold, _origSettleThreshold, _origSettleFrames, _origBypassCoefficient;
    [ObservableProperty] private Rgb _rightPreviewColor;
    [ObservableProperty] private string _rightPreviewHex;
    [ObservableProperty] private bool _saturationEnabled;
    [ObservableProperty] private int _screenHeight = 1080;

    // Preview bounds
    [ObservableProperty] private int _screenWidth = 1920;
    [ObservableProperty] private int _settleFrames;
    [ObservableProperty] private int _settleThreshold;
    [ObservableProperty] private bool _showPassword;
    [ObservableProperty] private HashSet<string?> _staleIeeeAddresses = new();


    // Stats bar
    [ObservableProperty] private string _statsText = "";
    [ObservableProperty] private Rgb _topPreviewColor;
    [ObservableProperty] private string _topPreviewHex;
    [ObservableProperty] private bool _transientBypassEnabled;
    [ObservableProperty] private int _transientThreshold;
    [ObservableProperty] private bool _useAuthentication;

    public SettingsViewModel(AppSettings settings, MqttDiscoveryService discoveryService)
    {
        _settings = settings;
        _discoveryService = discoveryService;

        MqttBrokerUrl = settings.MqttBrokerUrl;
        MqttBrokerPort = settings.MqttBrokerPort;
        MqttUsername = settings.MqttUsername;
        MqttPassword = settings.MqttPassword;
        MqttUseTls = settings.MqttUseTls;
        MqttClientId = settings.MqttClientId;
        UseAuthentication = !string.IsNullOrEmpty(settings.MqttUsername);
        AllLights = new ObservableCollection<LightMapping>(settings.LightMappings);
        foreach (var light in AllLights)
            light.NeedsSave = false;
        ActiveStyle = settings.ActiveStyle;
        GlobalBrightnessLimit = settings.GlobalBrightnessLimit;
        NetworkFps = settings.NetworkFps;

        GainEnabled = settings.Pipeline.GainEnabled;
        CompressEnabled = settings.Pipeline.CompressEnabled;
        SaturationEnabled = settings.Pipeline.SaturationEnabled;
        NoiseGateEnabled = settings.Pipeline.NoiseGateEnabled;
        IntegrationEnabled = settings.Pipeline.IntegrationEnabled;
        TransientBypassEnabled = settings.Pipeline.TransientBypassEnabled;
        TransientThreshold = settings.Pipeline.TransientThreshold;
        SettleThreshold = settings.Pipeline.SettleThreshold;
        SettleFrames = settings.Pipeline.SettleFrames;
        BypassCoefficient = settings.Pipeline.BypassCoefficient;

        LeftPreviewColor = Rgb.Black;
        RightPreviewColor = Rgb.Black;
        TopPreviewColor = Rgb.Black;
        BottomPreviewColor = Rgb.Black;
        LeftPreviewHex = "00 00 00";
        RightPreviewHex = "00 00 00";
        TopPreviewHex = "00 00 00";
        BottomPreviewHex = "00 00 00";

        SaveCommand = new RelayCommand(Save);
        DiscardCommand = new RelayCommand(ResetFromSettings);
        DiscoverLightsCommand = new AsyncRelayCommand(DiscoverLightsAsync);
        RemoveFromZoneCommand = new RelayCommand<LightMapping?>(l =>
        {
            if (l != null) RemoveFromZone(l);
        });
        ToggleBambiCommand = new RelayCommand(() => ToggleBambiRequested?.Invoke(!IsRunning));
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync);
        SelectStyleCommand = new RelayCommand<BambiStyle>(s => ActiveStyle = s);

        // Watch for collection changes to refresh zone views
        ((INotifyCollectionChanged)AllLights).CollectionChanged += (_, e) =>
        {
            RefreshZoneCollections();
            OnPropertyChanged(nameof(HasUnassignedLights));
            if (e?.NewItems != null)
                foreach (LightMapping light in e.NewItems)
                    light.PropertyChanged += (_, __) => OnPropertyChanged(nameof(HasChanges));
            if (e?.OldItems != null)
                foreach (LightMapping light in e.OldItems)
                    light.PropertyChanged -= (_, __) => OnPropertyChanged(nameof(HasChanges));
        };

        // Subscribe to existing items
        foreach (var light in AllLights)
            light.PropertyChanged += (_, __) => OnPropertyChanged(nameof(HasChanges));
    }

    public ICommand ToggleShowPasswordCommand => new RelayCommand(() => ShowPassword = !ShowPassword);

    public ObservableCollection<LightMapping> LeftLights { get; } = new();

    public ObservableCollection<LightMapping> RightLights { get; } = new();

    public ObservableCollection<LightMapping> TopLights { get; } = new();

    public ObservableCollection<LightMapping> BottomLights { get; } = new();

    public ObservableCollection<LightMapping> UnassignedLights { get; } = new();

    public AvaloniaList<string> Zones { get; } = ["left", "right", "top", "bottom"];

    public bool HasChanges
    {
        get
        {
            if (MqttBrokerUrl != _settings.MqttBrokerUrl) return true;
            if (MqttBrokerPort != _settings.MqttBrokerPort) return true;
            if (MqttUsername != _settings.MqttUsername) return true;
            if (MqttPassword != _settings.MqttPassword) return true;
            if (MqttUseTls != _settings.MqttUseTls) return true;
            if (MqttClientId != _settings.MqttClientId) return true;
            if (ActiveStyle != _settings.ActiveStyle) return true;
            if (GlobalBrightnessLimit != _settings.GlobalBrightnessLimit) return true;
            if (NetworkFps != _settings.NetworkFps) return true;
            if (GainEnabled != _origGainEnabled) return true;
            if (CompressEnabled != _origCompressEnabled) return true;
            if (SaturationEnabled != _origSaturationEnabled) return true;
            if (NoiseGateEnabled != _origNoiseGateEnabled) return true;
            if (IntegrationEnabled != _origIntegrationEnabled) return true;
            if (TransientBypassEnabled != _origTransientBypassEnabled) return true;
            if (TransientThreshold != _origTransientThreshold) return true;
            if (SettleThreshold != _origSettleThreshold) return true;
            if (SettleFrames != _origSettleFrames) return true;
            if (BypassCoefficient != _origBypassCoefficient) return true;
            var assigned = AllLights.Where(l => !string.IsNullOrEmpty(l.Zone)).ToList();
            if (assigned.Count != _settings.LightMappings.Count) return true;
            if (assigned.Any(l => l.NeedsSave)) return true;
            return false;
        }
    }

    public bool HasUnassignedLights => UnassignedLights.Count > 0;

    public IReadOnlyList<LightMapping> FilteredUnassignedLights =>
        string.IsNullOrEmpty(FilterText)
            ? UnassignedLights
            : UnassignedLights.Where(l => l.FriendlyName.Contains(FilterText, StringComparison.OrdinalIgnoreCase))
                .ToList();

    public AvaloniaList<BambiStyle> Styles { get; } =
    [
        BambiStyle.Standard,
        BambiStyle.Cinema,
        BambiStyle.Sports,
        BambiStyle.Gaming
    ];

    public ICommand SelectStyleCommand { get; }

    // Computed preview monitor dimensions (height fixed at 120, width follows aspect ratio)
    public double PreviewMonitorWidth => ScreenHeight > 0 ? 120.0 * ScreenWidth / ScreenHeight : 213.33;
    public double PreviewMonitorHeight => 120;

    public string AppVersion =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";

    public ICommand SaveCommand { get; }
    public ICommand DiscardCommand { get; }
    public ICommand DiscoverLightsCommand { get; }
    public ICommand RemoveFromZoneCommand { get; }
    public ICommand ToggleBambiCommand { get; }
    public ICommand TestConnectionCommand { get; }

    public bool BypassControlsEnabled => IntegrationEnabled && TransientBypassEnabled;

    partial void OnUseAuthenticationChanged(bool value)
    {
        if (!value)
        {
            MqttUsername = "";
            MqttPassword = "";
        }

        OnPropertyChanged(nameof(HasChanges));
        ResetTestConnection();
    }

    private void RefreshZoneCollections()
    {
        LeftLights.Clear();
        RightLights.Clear();
        TopLights.Clear();
        BottomLights.Clear();
        UnassignedLights.Clear();
        OnPropertyChanged(nameof(FilteredUnassignedLights));
        OnPropertyChanged(nameof(HasUnassignedLights));

        foreach (var light in AllLights)
            switch (light.Zone)
            {
                case "left": LeftLights.Add(light); break;
                case "right": RightLights.Add(light); break;
                case "top": TopLights.Add(light); break;
                case "bottom": BottomLights.Add(light); break;
                default: UnassignedLights.Add(light); break;
            }
    }

    partial void OnFilterTextChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredUnassignedLights));
        OnPropertyChanged(nameof(HasChanges));
    }

    public event Action<AppSettings>? SettingsSaved;
    public event Action<bool>? ToggleBambiRequested;

    partial void OnScreenWidthChanged(int value)
    {
        OnPropertyChanged(nameof(PreviewMonitorWidth));
    }

    partial void OnScreenHeightChanged(int value)
    {
        OnPropertyChanged(nameof(PreviewMonitorWidth));
    }

    partial void OnMqttBrokerUrlChanged(string value)
    {
        OnPropertyChanged(nameof(HasChanges));
        ResetTestConnection();
    }

    partial void OnMqttBrokerPortChanged(int value)
    {
        OnPropertyChanged(nameof(HasChanges));
        ResetTestConnection();
    }

    partial void OnMqttUsernameChanged(string value)
    {
        OnPropertyChanged(nameof(HasChanges));
        ResetTestConnection();
    }

    partial void OnMqttPasswordChanged(string value)
    {
        OnPropertyChanged(nameof(HasChanges));
        ResetTestConnection();
    }

    partial void OnMqttUseTlsChanged(bool value)
    {
        OnPropertyChanged(nameof(HasChanges));
        ResetTestConnection();
    }

    partial void OnMqttClientIdChanged(string value)
    {
        OnPropertyChanged(nameof(HasChanges));
        ResetTestConnection();
    }

    partial void OnActiveStyleChanged(BambiStyle value)
    {
        OnPropertyChanged(nameof(HasChanges));
    }

    partial void OnGlobalBrightnessLimitChanged(double value)
    {
        OnPropertyChanged(nameof(HasChanges));
    }

    partial void OnNetworkFpsChanged(int value)
    {
        NetworkFps = Math.Clamp(value, 1, 60);
        OnPropertyChanged(nameof(HasChanges));
    }

    partial void OnGainEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(HasChanges));
    }

    partial void OnCompressEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(HasChanges));
    }

    partial void OnSaturationEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(HasChanges));
    }

    partial void OnNoiseGateEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(HasChanges));
    }

    partial void OnIntegrationEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(BypassControlsEnabled));
    }

    partial void OnTransientBypassEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(BypassControlsEnabled));
    }

    partial void OnTransientThresholdChanged(int value)
    {
        TransientThreshold = Math.Clamp(value, 10, 100);
        OnPropertyChanged(nameof(HasChanges));
    }

    partial void OnSettleThresholdChanged(int value)
    {
        SettleThreshold = Math.Clamp(value, 5, 30);
        OnPropertyChanged(nameof(HasChanges));
    }

    partial void OnSettleFramesChanged(int value)
    {
        SettleFrames = Math.Clamp(value, 1, 10);
        OnPropertyChanged(nameof(HasChanges));
    }

    partial void OnBypassCoefficientChanged(int value)
    {
        BypassCoefficient = Math.Clamp(value, 2, 12);
        OnPropertyChanged(nameof(HasChanges));
    }

    public void UpdatePreview(Rgb left, Rgb right, Rgb top, Rgb bottom, Rectangle screenBounds,
        (int left, int top, int right, int bottom) contentBounds)
    {
        LeftPreviewColor = left;
        RightPreviewColor = right;
        TopPreviewColor = top;
        BottomPreviewColor = bottom;
        LeftPreviewHex = $"{left.R:X2} {left.G:X2} {left.B:X2}";
        RightPreviewHex = $"{right.R:X2} {right.G:X2} {right.B:X2}";
        TopPreviewHex = $"{top.R:X2} {top.G:X2} {top.B:X2}";
        BottomPreviewHex = $"{bottom.R:X2} {bottom.G:X2} {bottom.B:X2}";

        if (screenBounds.Width > 0 && screenBounds.Height > 0)
        {
            ScreenWidth = screenBounds.Width;
            ScreenHeight = screenBounds.Height;
            ContentMargin = new Thickness(
                contentBounds.left,
                contentBounds.top,
                screenBounds.Width - contentBounds.right,
                screenBounds.Height - contentBounds.bottom
            );
            // Console.WriteLine($"[PREVIEW] Screen: {ScreenWidth}x{ScreenHeight}, Content: {contentBounds.left},{contentBounds.top},{contentBounds.right},{contentBounds.bottom}, Margin: {ContentMargin}");
        }
    }

    public void UpdateStatus(bool isRunning)
    {
        IsRunning = isRunning;
    }

    public void UpdateMqttConnection(bool connected)
    {
        if (connected)
            MqttConnectionStatus = MqttConnectionStatus.LiveConnected;
        else if (MqttConnectionStatus == MqttConnectionStatus.LiveConnected)
            MqttConnectionStatus = MqttConnectionStatus.Idle;
        MqttConnectionError = "";
    }

    public void UpdateStats(double captureMs, double edgeMs, double pipeMs, double sendMs, double netFps,
        double bypassPct)
    {
        StatsText =
            $"Cap {captureMs,4:00.0}ms | Edge {edgeMs,4:00.0}ms | Pipe {pipeMs,4:00.0}ms | Send {sendMs,4:00.0}ms | Net {netFps,4:00.0} FPS | Bypass {bypassPct,4:00.0}%";
    }

    public void ClearStats()
    {
        StatsText = "Cap  ??.?ms | Edge  ??.?ms | Pipe  ??.?ms | Send  ??.?ms | Net  ??.? FPS | Bypass  ??.?%";
    }

    public void ResetFromSettings()
    {
        MqttBrokerUrl = _settings.MqttBrokerUrl;
        MqttBrokerPort = _settings.MqttBrokerPort;
        MqttUsername = _settings.MqttUsername;
        MqttPassword = _settings.MqttPassword;
        MqttUseTls = _settings.MqttUseTls;
        MqttClientId = _settings.MqttClientId;
        UseAuthentication = !string.IsNullOrEmpty(_settings.MqttUsername);
        ActiveStyle = _settings.ActiveStyle;
        GlobalBrightnessLimit = _settings.GlobalBrightnessLimit;
        NetworkFps = _settings.NetworkFps;
        GainEnabled = _settings.Pipeline.GainEnabled;
        CompressEnabled = _settings.Pipeline.CompressEnabled;
        SaturationEnabled = _settings.Pipeline.SaturationEnabled;
        NoiseGateEnabled = _settings.Pipeline.NoiseGateEnabled;
        IntegrationEnabled = _settings.Pipeline.IntegrationEnabled;
        TransientBypassEnabled = _settings.Pipeline.TransientBypassEnabled;
        TransientThreshold = _settings.Pipeline.TransientThreshold;
        SettleThreshold = _settings.Pipeline.SettleThreshold;
        SettleFrames = _settings.Pipeline.SettleFrames;
        BypassCoefficient = _settings.Pipeline.BypassCoefficient;
        _origGainEnabled = GainEnabled;
        _origCompressEnabled = CompressEnabled;
        _origSaturationEnabled = SaturationEnabled;
        _origNoiseGateEnabled = NoiseGateEnabled;
        _origIntegrationEnabled = IntegrationEnabled;
        _origTransientBypassEnabled = TransientBypassEnabled;
        _origTransientThreshold = TransientThreshold;
        _origSettleThreshold = SettleThreshold;
        _origSettleFrames = SettleFrames;
        _origBypassCoefficient = BypassCoefficient;
        AllLights = new ObservableCollection<LightMapping>(_settings.LightMappings);
        ((INotifyCollectionChanged)AllLights).CollectionChanged += (_, e) =>
        {
            RefreshZoneCollections();
            OnPropertyChanged(nameof(HasUnassignedLights));
            if (e?.NewItems != null)
                foreach (LightMapping light in e.NewItems)
                    light.PropertyChanged += (_, __) => OnPropertyChanged(nameof(HasChanges));
            if (e?.OldItems != null)
                foreach (LightMapping light in e.OldItems)
                    light.PropertyChanged -= (_, __) => OnPropertyChanged(nameof(HasChanges));
        };
        foreach (var light in AllLights)
        {
            light.NeedsSave = false;
            light.PropertyChanged += (_, __) => OnPropertyChanged(nameof(HasChanges));
        }

        RefreshZoneCollections();
        OnPropertyChanged(nameof(HasUnassignedLights));
        StaleIeeeAddresses = new HashSet<string?>();
        DiscoveryStatus = "";
    }

    public void Save()
    {
        _settings.MqttBrokerUrl = MqttBrokerUrl;
        _settings.MqttBrokerPort = MqttBrokerPort;
        _settings.MqttUsername = MqttUsername;
        _settings.MqttPassword = MqttPassword;
        _settings.MqttUseTls = MqttUseTls;
        _settings.MqttClientId = MqttClientId;
        _settings.ActiveStyle = ActiveStyle;
        _settings.GlobalBrightnessLimit = GlobalBrightnessLimit;
        _settings.NetworkFps = NetworkFps;
        _settings.Pipeline.GainEnabled = GainEnabled;
        _settings.Pipeline.CompressEnabled = CompressEnabled;
        _settings.Pipeline.SaturationEnabled = SaturationEnabled;
        _settings.Pipeline.NoiseGateEnabled = NoiseGateEnabled;
        _settings.Pipeline.IntegrationEnabled = IntegrationEnabled;
        _settings.Pipeline.TransientBypassEnabled = TransientBypassEnabled;
        _settings.Pipeline.TransientThreshold = TransientThreshold;
        _settings.Pipeline.SettleThreshold = SettleThreshold;
        _settings.Pipeline.SettleFrames = SettleFrames;
        _settings.Pipeline.BypassCoefficient = BypassCoefficient;

        _settings.LightMappings.Clear();
        foreach (var light in AllLights.Where(l => !string.IsNullOrEmpty(l.Zone)))
            _settings.LightMappings.Add(light);

        ConfigService.Save(_settings);
        foreach (var light in AllLights)
            light.NeedsSave = false;
        _origGainEnabled = GainEnabled;
        _origCompressEnabled = CompressEnabled;
        _origSaturationEnabled = SaturationEnabled;
        _origNoiseGateEnabled = NoiseGateEnabled;
        _origIntegrationEnabled = IntegrationEnabled;
        _origTransientBypassEnabled = TransientBypassEnabled;
        _origTransientThreshold = TransientThreshold;
        _origSettleThreshold = SettleThreshold;
        _origSettleFrames = SettleFrames;
        _origBypassCoefficient = BypassCoefficient;
        OnPropertyChanged(nameof(HasChanges));
        SettingsSaved?.Invoke(_settings);
    }

    private async Task DiscoverLightsAsync()
    {
        IsDiscovering = true;
        DiscoveryStatus = "Discovering...";
        try
        {
            var discovered = await _discoveryService.DiscoverLightsAsync(MqttBrokerUrl, MqttBrokerPort, MqttUsername,
                MqttPassword, MqttUseTls, MqttClientId);

            Console.WriteLine($"[Discovery] Got {discovered.Count} lights from MQTT");
            // Marshal back to UI thread for collection modifications
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var discoveredIeees = new HashSet<string?>(discovered.Select(d => d.IeeeAddress));

                foreach (var light in AllLights)
                    light.IsStale = !discoveredIeees.Contains(light.IeeeAddress);

                foreach (var light in discovered)
                {
                    var existing = AllLights.FirstOrDefault(l => l.IeeeAddress == light.IeeeAddress);
                    if (existing != null)
                    {
                        existing.FriendlyName = light.FriendlyName;
                        existing.ShortAddress = light.ShortAddress;
                        existing.ModelId = light.ModelId;
                    }
                    else
                    {
                        AllLights.Add(light);
                    }
                }

                StaleIeeeAddresses = new HashSet<string?>(
                    AllLights.Where(l => !string.IsNullOrEmpty(l.Zone) && !discoveredIeees.Contains(l.IeeeAddress))
                        .Select(l => l.IeeeAddress));

                Console.WriteLine($"[Discovery] AllLights: {AllLights.Count}, Unassigned: {UnassignedLights.Count}");
                DiscoveryStatus = $"{AllLights.Count} light(s)";
                Save();
            });
        }
        catch (Exception ex)
        {
            DiscoveryStatus = $"Error: {ex.Message}";
            Console.WriteLine($"[Discovery] Error: {ex.Message}");
        }
        finally
        {
            IsDiscovering = false;
        }
    }

    private void ResetTestConnection()
    {
        if (MqttConnectionStatus == MqttConnectionStatus.Successful ||
            MqttConnectionStatus == MqttConnectionStatus.Failed)
            MqttConnectionStatus = MqttConnectionStatus.Idle;
        MqttConnectionError = "";
    }

    private async Task TestConnectionAsync()
    {
        IsTestingConnection = true;
        MqttConnectionStatus = MqttConnectionStatus.Testing;
        MqttConnectionError = "";
        try
        {
            var ct = new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;
            var success = await _discoveryService.TestConnectionAsync(
                MqttBrokerUrl, MqttBrokerPort,
                string.IsNullOrEmpty(MqttUsername) ? null : MqttUsername,
                MqttPassword,
                MqttUseTls,
                string.IsNullOrEmpty(MqttClientId) ? null : MqttClientId,
                ct);

            if (success)
            {
                MqttConnectionStatus = MqttConnectionStatus.Successful;
            }
            else
            {
                MqttConnectionStatus = MqttConnectionStatus.Failed;
                MqttConnectionError = "Could not connect to broker";
            }
        }
        catch (Exception ex)
        {
            MqttConnectionStatus = MqttConnectionStatus.Failed;
            MqttConnectionError = ex.Message;
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    public void AssignToZone(LightMapping light, string zone)
    {
        if (string.IsNullOrEmpty(zone)) return;
        light.Zone = zone;
        StaleIeeeAddresses.Remove(light.IeeeAddress);
        UnassignedLights.Remove(light);
        OnPropertyChanged(nameof(FilteredUnassignedLights));
        OnPropertyChanged(nameof(HasUnassignedLights));
        switch (zone)
        {
            case "left": LeftLights.Add(light); break;
            case "right": RightLights.Add(light); break;
            case "top": TopLights.Add(light); break;
            case "bottom": BottomLights.Add(light); break;
        }

        OnPropertyChanged(nameof(HasChanges));
        Save();
    }

    private void RemoveFromZone(LightMapping light)
    {
        light.Zone = "";
        StaleIeeeAddresses.Remove(light.IeeeAddress);
        LeftLights.Remove(light);
        RightLights.Remove(light);
        TopLights.Remove(light);
        BottomLights.Remove(light);
        UnassignedLights.Add(light);
        OnPropertyChanged(nameof(FilteredUnassignedLights));
        OnPropertyChanged(nameof(HasUnassignedLights));
        OnPropertyChanged(nameof(HasChanges));
        Save();
    }
}