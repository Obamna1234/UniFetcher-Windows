using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nickvision.Desktop.WinUI.Helpers;
using Nickvision.Parabolic.Shared.Helpers;
using Nickvision.Parabolic.WinUI.Helpers;
using System;
using System.Runtime.InteropServices;

namespace Nickvision.Parabolic.WinUI;

public static partial class Program
{
    [LibraryImport("Microsoft.ui.xaml.dll", EntryPoint = "XamlCheckProcessRequirements")]
    private static partial void XamlCheckProcessRequirements();

    [STAThread]
    private static void Main(string[] args)
    {
        // UniFetcher: anything that goes wrong, even on a background thread, is saved to crash.log
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                StartupGuard.WriteCrashLog(ex, args);
            }
        };
        var started = false;
        try
        {
            // Only one copy at a time: a second start brings the open window forward
            if (!StartupGuard.TryBecomeOnlyInstance(args))
            {
                return;
            }
            XamlCheckProcessRequirements();
            var builder = Host.CreateApplicationBuilder(args);
            builder.ConfigureUniFetcher(args);
            builder.ConfigureWinUI<App>();
            builder.Services.AddControls();
            var app = builder.Build();
            // Open the settings database once, here, before the window and the background tasks start.
            // Opening it from two places at the same moment made the second start-up crash.
            app.Services.GetRequiredService<Nickvision.Desktop.Application.IConfigurationService>().Get("UniFetcherStartup", string.Empty);
            started = true;
            app.Run();
        }
        catch (Exception e)
        {
            if (started)
            {
                StartupGuard.WriteCrashLog(e, args);
            }
            else
            {
                // Show a message instead of silently not opening
                StartupGuard.ReportCrash(e, args);
            }
        }
        finally
        {
            StartupGuard.Release();
        }
        // Make sure nothing keeps UniFetcher running in the background after the window has closed
        Environment.Exit(0);
    }
}
