using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nickvision.Desktop.Application;
using Nickvision.Desktop.Filesystem;
using Nickvision.Desktop.Helpers;
using Nickvision.Parabolic.Shared.Controllers;
using Nickvision.Parabolic.Shared.Services;
using System;
using System.IO;
using System.Reflection;

namespace Nickvision.Parabolic.Shared.Helpers;

public static class HostApplicationBuilderExtensions
{
    extension(IHostApplicationBuilder builder)
    {
        public IHostApplicationBuilder ConfigureUniFetcher(string[] args)
        {
            var appInfo = new AppInfo("com.unifetcher.windows", "UniFetcher", "UniFetcher")
            {
                // The version comes from the build (the release workflow sets it from the git tag, e.g. v1.2.0 -> 1.2.0)
                Version = new AppVersion(GetBuildVersion()),
                Changelog = """
                - First UniFetcher release, based on Parabolic by Nickvision
                """,
                SourceRepository = new Uri("https://github.com/Obamna1234/UniFetcher-Windows"),
                IssueTracker = new Uri("https://github.com/Obamna1234/UniFetcher-Windows/issues/new"),
                IsPortable = OperatingSystem.IsWindows() && args.Contains("--portable")
            };
            builder.Properties.Add("AppInfo", appInfo);
            builder.Services.AddSingleton(appInfo);
            builder.ConfigureNickvision(args, appInfo.IsPortable ? "app.log" : Path.Combine(UserDirectories.LocalData, appInfo.Name, "app.log"));
            builder.Services.AddSingleton<IEventsService, EventsService>();
            builder.Services.AddSingleton<IDenoExecutableService, DenoExecutableService>();
            builder.Services.AddSingleton<IDiscoveryService, DiscoveryService>();
            builder.Services.AddSingleton<IDownloadRegistryService, DownloadRegistryService>();
            builder.Services.AddSingleton<IActionNeededService, ActionNeededService>();
            builder.Services.AddSingleton<IDownloadService, DownloadService>();
            builder.Services.AddSingleton<IHistoryService, HistoryService>();
            builder.Services.AddSingleton<IRecoveryService, RecoveryService>();
            builder.Services.AddSingleton<IThumbnailService, ThumbnailService>();
            builder.Services.AddSingleton<IToolUpdateService, ToolUpdateService>();
            builder.Services.AddSingleton<IQueueGuardService, QueueGuardService>();
            builder.Services.AddSingleton<IFirefoxProfileService, FirefoxProfileService>();
            builder.Services.AddSingleton<ISiteSessionService, SiteSessionService>();
            builder.Services.AddSingleton<IYtdlpExecutableService, YtdlpExecutableService>();
            builder.Services.AddTransient<AddDownloadDialogController>();
            builder.Services.AddTransient<HistoryViewController>();
            builder.Services.AddTransient<KeyringViewController>();
            builder.Services.AddSingleton<MainWindowController>();
            builder.Services.AddTransient<PreferencesViewController>();
            builder.Services.AddHostedService<ConfigurationMigrationService>();
            builder.Services.AddHostedService<ToolUpdateScheduler>();
            builder.Services.AddSingleton<InboxService>();
            builder.Services.AddSingleton<IInboxService>(sp => sp.GetRequiredService<InboxService>());
            builder.Services.AddHostedService(sp => sp.GetRequiredService<InboxService>());
            builder.Services.AddSingleton<BulkImportService>();
            builder.Services.AddSingleton<IBulkImportService>(sp => sp.GetRequiredService<BulkImportService>());
            builder.Services.AddHostedService(sp => sp.GetRequiredService<BulkImportService>());
            builder.Services.AddTransient<BulkImportController>();
            builder.Services.AddTransient<LibraryController>();
            return builder;
        }
    }

    private static string GetBuildVersion()
    {
        var info = typeof(HostApplicationBuilderExtensions).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.1.0";
        // Strip any "+commit" suffix the .NET SDK adds
        var plus = info.IndexOf('+');
        return plus == -1 ? info : info[..plus];
    }
}
