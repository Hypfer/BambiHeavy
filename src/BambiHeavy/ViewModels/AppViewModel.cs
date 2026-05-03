using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using BambiHeavy.Services;
using BambiHeavy.Views;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace BambiHeavy.ViewModels;

public class AppViewModel
{
    private static bool _shuttingDown;
    private static BambiService? s_service;
    private static IClassicDesktopStyleApplicationLifetime? s_desktopLifetime;
    private readonly AutostartManager _autostartManager = new();
    private readonly IClassicDesktopStyleApplicationLifetime _desktopLifetime;
    private readonly TrayIcon _icon = new();
    private readonly BambiService _service;
    private readonly SettingsViewModel? _settingsViewModel;
    private SettingsWindow? _settingsWindow;
    private NativeMenuItem? _toggleItem;

    public AppViewModel(IClassicDesktopStyleApplicationLifetime desktopLifetime)
    {
        _desktopLifetime = desktopLifetime;
        s_desktopLifetime = desktopLifetime;

        var screenZones = ScreenZoneBuilder.BuildFromSettings();

        _service = new BambiService(screenZones, Config.Settings);
        s_service = _service;
        _service.StatusChanged += OnStatusChanged;
        _service.Error += OnError;
        _service.StatsUpdated += (cap, edge, pipe, send, net, bypass) =>
            _settingsViewModel!.UpdateStats(cap, edge, pipe, send, net, bypass);

        _settingsViewModel = new SettingsViewModel(Config.Settings, new MqttDiscoveryService());
        _settingsViewModel.SettingsSaved += s => _service.ApplySettings(s);
        _settingsViewModel.ToggleBambiRequested += ToggleBambi;
        _service.StatusChanged += (isRunning, pf, nf) =>
        {
            _settingsViewModel!.UpdateStatus(isRunning);
            if (isRunning) _settingsViewModel.ClearStats();
            else _settingsViewModel.StatsText = "";
        };
        _service.MqttConnectionChanged += connected => { _settingsViewModel!.UpdateMqttConnection(connected); };
        _service.ColorPreviewUpdated += (l, r, t, b, sBounds, cBounds) =>
            _settingsViewModel!.UpdatePreview(l, r, t, b, sBounds, cBounds);

        InitIcon();

        Console.CancelKeyPress += (_, _) => GracefulShutdown();
#if WINDOWS
        SetConsoleCtrlHandler(ctrlType =>
        {
            if (ctrlType == CTRL_C_EVENT || ctrlType == CTRL_BREAK_EVENT)
            {
                GracefulShutdown();
                return true;
            }

            return false;
        }, true);
#endif
    }

    public static void GracefulShutdown()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        ConfigService.Save(Config.Settings);
        s_service?.Stop();
        Dispatcher.UIThread.Invoke(() => s_desktopLifetime?.Shutdown());
    }

    private void InitIcon()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://BambiHeavy/Assets/icon.ico"));
            _icon.Icon = new WindowIcon(stream);
        }
        catch
        {
        }

        _icon.ToolTipText = "Stopped";

        var menu = new NativeMenu();

        _toggleItem = new NativeMenuItem
        {
            Header = "Enable",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = false
        };
        _toggleItem.Click += (_, _) => { ToggleBambi(_toggleItem.IsChecked); };
        menu.Items.Add(_toggleItem);

        menu.Items.Add(new NativeMenuItem { Header = "-" });

        var settingsItem = new NativeMenuItem("Settings");
        settingsItem.Click += (_, _) => ShowSettings();
        menu.Items.Add(settingsItem);

        menu.Items.Add(new NativeMenuItem { Header = "-" });

        if (_autostartManager is { IsSupported: true, IsReady: true })
        {
            var autoStartItem = new NativeMenuItem
            {
                Header = "Run on startup",
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = _autostartManager.IsAutostartEnabled
            };

            autoStartItem.Click += (_, _) =>
            {
                if (autoStartItem.IsChecked)
                    _autostartManager.EnableAutostart();
                else
                    _autostartManager.DisableAutostart();
            };

            menu.Items.Add(autoStartItem);
            menu.Items.Add(new NativeMenuItem { Header = "-" });
        }

        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += (_, _) => _desktopLifetime.Shutdown();
        menu.Items.Add(exitItem);

        _icon.Menu = menu;
        _icon.IsVisible = true;
        _icon.Clicked += (_, _) => ShowSettings();
    }

    private void ShowSettings(bool focusLightsTab = false)
    {
        if (_settingsWindow != null && _settingsWindow.IsVisible)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsViewModel!.ResetFromSettings();

        _settingsWindow = new SettingsWindow
        {
            DataContext = _settingsViewModel
        };

        var screen = _settingsWindow.Screens?.All.FirstOrDefault(s => s.IsPrimary);
        if (screen != null)
        {
            var area = screen.WorkingArea;
            var w = Math.Clamp(area.Width * 0.6, 960.0, 1400.0);
            var h = w * 9.0 / 16.0;
            h = Math.Clamp(h, 540.0, 800.0);
            w = h * 16.0 / 9.0;
            _settingsWindow.Width = w;
            _settingsWindow.Height = h;
            _settingsWindow.Position = new PixelPoint(
                (int)(area.X + area.Width / 2 - w / 2),
                (int)(area.Y + area.Height / 2 - h / 2));
        }

        _settingsWindow.Show();

        if (focusLightsTab)
            if (_settingsWindow.Content is TabControl tc)
                tc.SelectedIndex = 1;
    }

    private void OnStatusChanged(bool isRunning, double pipelineFps, double networkFps)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            _icon.ToolTipText = isRunning ? "Started" : "Stopped";
            _toggleItem!.IsChecked = isRunning;
        });
    }

    private void OnError(Exception ex)
    {
        Console.WriteLine($"[ERROR] {ex}");
        Dispatcher.UIThread.Post(async () =>
        {
            var parent = _settingsWindow;
            if (parent is null)
                ShowSettings();
            parent ??= _settingsWindow;
            if (parent is null)
                return;

            var box = MessageBoxManager.GetMessageBoxStandard(
                "Error",
                ex.Message,
                ButtonEnum.Ok,
                Icon.Error,
                windowStartupLocation: WindowStartupLocation.CenterOwner
            );

            await box.ShowWindowDialogAsync(parent);
        });
    }

    private void ToggleBambi(bool shouldStart)
    {
        if (shouldStart)
            _service.Start();
        else
            _service.Stop();
    }

#if WINDOWS
    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate? handler, bool add);

    private delegate bool ConsoleCtrlDelegate(uint ctrlType);

    private const uint CTRL_C_EVENT = 0;
    private const uint CTRL_BREAK_EVENT = 1;
#endif
}