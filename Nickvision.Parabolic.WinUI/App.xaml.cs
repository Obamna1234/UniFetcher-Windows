using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Nickvision.Parabolic.WinUI.Views;
using System;

namespace Nickvision.Parabolic.WinUI;

public partial class App : Application
{
    private readonly IServiceProvider _serviceProvider;
    private Window? _window;

    public App(IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _serviceProvider = serviceProvider;
        UnhandledException += (_, e) =>
        {
            _serviceProvider.GetRequiredService<ILogger<App>>().LogError(e.Exception, $"An unhandled exception occurred: {e.Message}");
            // UniFetcher: save it where it can be found, and keep the app open instead of closing without a word
            Nickvision.Parabolic.WinUI.Helpers.StartupGuard.WriteCrashLog(e.Exception, Environment.GetCommandLineArgs());
            e.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (_window is null)
        {
            _window = _serviceProvider.GetRequiredService<MainWindow>();
        }
        _window.Activate();
    }
}
