using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using BambiHeavy.Services;
using BambiHeavy.ViewModels;

namespace BambiHeavy;

public class App : Application
{
    public static AppViewModel? ViewModel { get; private set; }

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Light;
        base.Initialize();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Config.Settings = ConfigService.Load();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
        {
            desktopLifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktopLifetime.ShutdownRequested += (_, _) => { AppViewModel.GracefulShutdown(); };
            ViewModel = new AppViewModel(desktopLifetime);
        }

        base.OnFrameworkInitializationCompleted();
    }
}