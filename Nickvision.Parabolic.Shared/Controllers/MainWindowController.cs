using Microsoft.Extensions.Logging;
using Nickvision.Desktop.Application;
using Nickvision.Desktop.Filesystem;
using Nickvision.Desktop.Globalization;
using Nickvision.Desktop.Network;
using Nickvision.Desktop.Notifications;
using Nickvision.Desktop.System;
using Nickvision.Parabolic.Shared.Helpers;
using Nickvision.Parabolic.Shared.Models;
using Nickvision.Parabolic.Shared.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Nickvision.Parabolic.Shared.Controllers;

public class MainWindowController
{
    private readonly ILogger<MainWindowController> _logger;
    private readonly AppInfo _appInfo;
    private readonly IArgumentsService _argumentsService;
    private readonly IConfigurationService _configurationService;
    private readonly IDatabaseService _databaseService;
    private readonly IDenoExecutableService _denoExecutableService;
    private readonly IDownloadService _downloadService;
    private readonly INotificationService _notificationService;
    private readonly IPowerService _powerService;
    private readonly IRecoveryService _recoveryService;
    private readonly ITranslationService _translationService;
    private readonly IUpdaterService _updaterService;
    private readonly IToolUpdateService _toolUpdateService;
    private readonly ISiteSessionService _sessions;
    private readonly IYtdlpExecutableService _ytdlpExecutableService;
    private AppVersion _latestAppVersion;
    private AppVersion _latestYtdlpVersion;
    private AppVersion _latestDenoVersion;

    public MainWindowController(ILogger<MainWindowController> logger, AppInfo appInfo, IArgumentsService argumentsService, IConfigurationService configurationService, IDatabaseService databaseService, IDenoExecutableService denoExecutableService, IDownloadService downloadService, INotificationService notificationService, IPowerService powerService, IRecoveryService recoveryService, ITranslationService translationService, IUpdaterService updaterService, IToolUpdateService toolUpdateService, ISiteSessionService sessions, IYtdlpExecutableService ytdlpExecutableService)
    {
        _logger = logger;
        _appInfo = appInfo;
        _argumentsService = argumentsService;
        _configurationService = configurationService;
        _databaseService = databaseService;
        _denoExecutableService = denoExecutableService;
        _downloadService = downloadService;
        _notificationService = notificationService;
        _powerService = powerService;
        _recoveryService = recoveryService;
        _translationService = translationService;
        _updaterService = updaterService;
        _toolUpdateService = toolUpdateService;
        _sessions = sessions;
        _toolUpdateService.UpdateFound += ToolUpdateService_UpdateFound;
        _ytdlpExecutableService = ytdlpExecutableService;
        _latestAppVersion = appInfo.Version!;
        _latestYtdlpVersion = _ytdlpExecutableService.BundledVersion;
        _latestDenoVersion = _denoExecutableService.BundledVersion;
        _translationService.Language = _configurationService.TranslationLanguage;
        _logger.LogInformation($"Received command-line arguments: [{string.Join(", ", argumentsService.Data)}]");
        // Events
        _configurationService.Saved += ConfigurationService_Saved;
        // Translate strings
        _appInfo.ShortName = translationService._("UniFetcher");
        _appInfo.Description = translationService._("Download web video and audio.");
        // UniFetcher is a fork of Parabolic: keep credit for the original authors
        _appInfo.ExtraLinks.Add(translationService._("Based on Parabolic by Nickvision"), new Uri("https://github.com/NickvisionApps/Parabolic"));
        _appInfo.Developers.Add("Nicholas Logozzo (Parabolic)", "https://github.com/nlogozzo");
        _appInfo.Developers.Add(translationService._("Parabolic contributors on GitHub"), "https://github.com/NickvisionApps/Parabolic/graphs/contributors");
        _appInfo.Developers.Add("Obamna1234 (UniFetcher)", "https://github.com/Obamna1234");
        _appInfo.Designers.Add("Nicholas Logozzo (Parabolic)", "https://github.com/nlogozzo");
        _appInfo.Designers.Add(translationService._("Fyodor Sobolev"), "https://github.com/fsobolev");
        _appInfo.Designers.Add("DaPigGuy", "https://github.com/DaPigGuy");
        _appInfo.Artists.Add(translationService._("David Lapshin (Parabolic)"), "https://github.com/daudix");
        _appInfo.TranslationCredits = translationService._("translation-credits");
    }

    public bool CanShutdown => _downloadService.RemainingCount == 0;

    public int CompletedDownloadsCount => _downloadService.CompletedCount;

    public int FailedDownloadsCount => _downloadService.FailedCount;

    public int QueuedDownloadsCount => _downloadService.QueuedCount;

    public int RecoverableDownloadsCount => _recoveryService.Count;

    public int RemainingDownloadsCount => _downloadService.RemainingCount;

    public int RunningDownloadsCount => _downloadService.DownloadingCount;

    public bool ShowDisclaimerOnStartup
    {
        get => _configurationService.ShowDisclaimerOnStartup;

        set => _configurationService.ShowDisclaimerOnStartup = value;
    }

    public Theme Theme => _configurationService.Theme;

    public WindowGeometry WindowGeometry
    {
        get => _configurationService.WindowGeometry;

        set => _configurationService.WindowGeometry = value;
    }

    public Uri? UrlFromArgs
    {
        get
        {
            for (var i = 0; i < _argumentsService.Data.Count; i++)
            {
                var urlText = _argumentsService.Data[i].Trim();
                if (urlText.StartsWith("unifetcher://", StringComparison.Ordinal))
                {
                    urlText = urlText.Replace("unifetcher://", "https://");
                }
                if (Uri.TryCreate(urlText, UriKind.Absolute, out var url))
                {
                    return url;
                }
            }
            return null;
        }
    }

    public async Task CheckForUpdatesAsync(bool showNotificationForNoUpdates)
    {
        _logger.LogInformation("Checking for UniFetcher updates...");
        var stableAppVersion = await _updaterService.GetLatestStableVersionAsync();
        if (stableAppVersion is not null)
        {
            _latestAppVersion = stableAppVersion;
        }
        if (_configurationService.AllowPreviewUpdates)
        {
            var previewAppVersion = await _updaterService.GetLatestPreviewVersionAsync();
            if (previewAppVersion is not null && previewAppVersion > stableAppVersion)
            {
                _latestAppVersion = previewAppVersion;
            }
        }
        if (_latestAppVersion > _appInfo.Version!)
        {
            _logger.LogInformation($"New application update available: {_latestAppVersion}");
            _notificationService.Send(new AppNotification(_translationService._("New {0} update available: {1}", _appInfo.ShortName!, _latestAppVersion.ToString()), NotificationSeverity.Success)
            {
                Action = "update"
            });
            return;
        }
        // yt-dlp / ffmpeg are checked by the tool update service on start and every 12 hours.
        // When the user asks from the Help menu, check them now too.
        if (showNotificationForNoUpdates)
        {
            var tools = await _toolUpdateService.CheckAsync();
            if (tools.Outcome is ToolUpdateOutcome.UpToDate or ToolUpdateOutcome.Skipped)
            {
                _notificationService.Send(new AppNotification(_translationService._("No update available"), NotificationSeverity.Warning));
            }
        }
    }

    public IEnumerable<int> ClearCompletedDownloads()
    {
        try
        {
            return _downloadService.ClearCompleted();
        }
        catch (Exception e)
        {
            _logger.LogError($"An error occurred while clearing completed downloads: {e}");
            _notificationService.Send(new AppNotification(_translationService._("An error occurred while clearing completed downloads"), NotificationSeverity.Error)
            {
                Action = "error",
                ActionParam = e.ToString()
            });
            return [];
        }
    }

    public async Task ClearRecoverableDownloadsAsync()
    {
        try
        {
            await _recoveryService.ClearAsync();
        }
        catch (Exception e)
        {
            _logger.LogError($"An error occurred while clearing recoverable downloads: {e}");
            _notificationService.Send(new AppNotification(_translationService._("An error occurred while clearing recoverable downloads"), NotificationSeverity.Error)
            {
                Action = "error",
                ActionParam = e.ToString()
            });
        }
    }

    public IEnumerable<int> ClearQueuedDownloads()
    {
        try
        {
            return _downloadService.ClearQueued();
        }
        catch (Exception e)
        {
            _logger.LogError($"An error occurred while clearing queued downloads: {e}");
            _notificationService.Send(new AppNotification(_translationService._("An error occurred while clearing queued downloads"), NotificationSeverity.Error)
            {
                Action = "error",
                ActionParam = e.ToString()
            });
            return [];
        }
    }

    public async Task DenoUpdateAsync(IProgress<DownloadProgress> progress)
    {
        var res = await _denoExecutableService.DownloadUpdateAsync(_latestDenoVersion, progress);
        if (res)
        {
            _notificationService.Send(new AppNotification(_translationService._("Deno {0} installed successfully", _latestDenoVersion.ToString()), NotificationSeverity.Success));
        }
        else
        {
            _notificationService.Send(new AppNotification(_translationService._("Unable to download and install the Deno update"), NotificationSeverity.Error));
        }
    }

    public async Task<string> GetDebugInformationAsync(string extraInformation = "")
    {
        var ytdlpVersion = await _toolUpdateService.GetYtdlpVersionAsync();
        var ffmpegVersion = await _toolUpdateService.GetFfmpegVersionAsync();
        var denoVersion = await _denoExecutableService.GetExecutableVersionAsync();
        extraInformation += string.IsNullOrEmpty(extraInformation) ? string.Empty : "\n";
        extraInformation += $"Database encrypted: {_databaseService.IsEncrypted}\nLog path: {(_appInfo.IsPortable ? "app.log" : Path.Combine(UserDirectories.LocalData, _appInfo.Name, "app.log"))}";
        extraInformation += $"\n\nyt-dlp: {ytdlpVersion} ({_toolUpdateService.YtdlpPath})";
        extraInformation += $"\ndeno: {(denoVersion is not null ? denoVersion.ToString() : "not found")}";
        extraInformation += $"\nffmpeg: {ffmpegVersion} ({_toolUpdateService.FfmpegPath})";
        extraInformation += $"\n\n{await _configurationService.ToStringAsync()}";
        return Desktop.System.Environment.GetDebugInformation(_appInfo, extraInformation);
    }

    public bool PauseDownload(int id)
    {
        try
        {
            return _downloadService.Pause(id);
        }
        catch (Exception e)
        {
            _logger.LogError($"An error occurred while pausing download ({id}): {e}");
            _notificationService.Send(new AppNotification(_translationService._("An error occurred while pausing the download"), NotificationSeverity.Error)
            {
                Action = "error",
                ActionParam = e.ToString()
            });
            return false;
        }
    }

    public async Task RecoverAllDownloadsAsync()
    {
        try
        {
            await _downloadService.RecoverAllAsync();
        }
        catch (Exception e)
        {
            _logger.LogError($"An error occurred while recovering downloads: {e}");
            _notificationService.Send(new AppNotification(_translationService._("An error occurred while recovering downloads"), NotificationSeverity.Error)
            {
                Action = "error",
                ActionParam = e.ToString()
            });
        }
    }

    public bool ResumeDownload(int id)
    {
        try
        {
            return _downloadService.Resume(id);
        }
        catch (Exception e)
        {
            _logger.LogError($"An error occurred while resuming download ({id}): {e}");
            _notificationService.Send(new AppNotification(_translationService._("An error occurred while resuming the download"), NotificationSeverity.Error)
            {
                Action = "error",
                ActionParam = e.ToString()
            });
            return false;
        }
    }

    public async Task RetryFailedDownloadsAsync()
    {
        try
        {
            await _downloadService.RetryFailedAsync();
        }
        catch (Exception e)
        {
            _logger.LogError($"An error occurred while retrying failed downloads: {e}");
            _notificationService.Send(new AppNotification(_translationService._("An error occurred while retrying failed downloads"), NotificationSeverity.Error)
            {
                Action = "error",
                ActionParam = e.ToString()
            });
        }
    }

    public async Task<bool> RetryDownloadAsync(int id)
    {
        try
        {
            return await _downloadService.RetryAsync(id);
        }
        catch (Exception e)
        {
            _logger.LogError($"An error occurred while retrying download ({id}): {e}");
            _notificationService.Send(new AppNotification(_translationService._("An error occurred while retrying the download"), NotificationSeverity.Error)
            {
                Action = "error",
                ActionParam = e.ToString()
            });
            return false;
        }
    }

    public async Task StopAllDownloadsAsync()
    {
        try
        {
            await _downloadService.StopAllAsync();
        }
        catch (Exception e)
        {
            _logger.LogError($"An error occurred while stopping all downloads: {e}");
            _notificationService.Send(new AppNotification(_translationService._("An error occurred while stopping all downloads"), NotificationSeverity.Error)
            {
                Action = "error",
                ActionParam = e.ToString()
            });
        }
    }

    public async Task<bool> StopDownloadAsync(int id)
    {
        try
        {
            return await _downloadService.StopAsync(id);
        }
        catch (Exception e)
        {
            _logger.LogError($"An error occurred while stopping download ({id}): {e}");
            _notificationService.Send(new AppNotification(_translationService._("An error occurred while stopping the download"), NotificationSeverity.Error)
            {
                Action = "error",
                ActionParam = e.ToString()
            });
            return false;
        }
    }

    public async Task WindowsUpdateAsync(IProgress<DownloadProgress> progress)
    {
        var res = await _updaterService.WindowsApplicationUpdateAsync(_latestAppVersion, progress);
        if (res)
        {
            _notificationService.Send(new AppNotification(_translationService._("Starting {0} installer...", _appInfo.ShortName!), NotificationSeverity.Success));
        }
        else
        {
            _notificationService.Send(new AppNotification(_translationService._("Unable to download and install the update"), NotificationSeverity.Error));
        }
    }

    public async Task YtdlpUpdateAsync(IProgress<DownloadProgress> progress)
    {
        var res = await _toolUpdateService.CheckAsync(true, progress);
        if (res.Outcome == ToolUpdateOutcome.Failed)
        {
            _notificationService.Send(new AppNotification(_translationService._("Unable to download and install the yt-dlp update"), NotificationSeverity.Error)
            {
                Action = "error",
                ActionParam = res.Error ?? string.Empty
            });
        }
        else if (res.Outcome == ToolUpdateOutcome.UpToDate)
        {
            _notificationService.Send(new AppNotification(_translationService._("yt-dlp and ffmpeg are up to date"), NotificationSeverity.Success));
        }
        // "Installed" is announced by ToolUpdateService_UpdateFound
    }

    /// <summary>UniFetcher: opens a link in the UniFetcher Firefox profile (for logins / "verify you are human" checks).</summary>
    public async Task OpenInProfileAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !await _sessions.OpenInProfileAsync(uri))
        {
            _notificationService.Send(new AppNotification(_translationService._("Couldn't open the UniFetcher Firefox profile. Is Firefox installed?"), NotificationSeverity.Error));
        }
    }

    private void ToolUpdateService_UpdateFound(object? sender, ToolUpdateResult e)
    {
        if (e.Outcome == ToolUpdateOutcome.Available)
        {
            var what = e.YtdlpVersion is not null ? $"yt-dlp {e.YtdlpVersion}" : "ffmpeg";
            _notificationService.Send(new AppNotification(_translationService._("New update available: {0}", what), NotificationSeverity.Success)
            {
                Action = "update-ytdlp"
            });
        }
        else if (e.Outcome == ToolUpdateOutcome.Installed && e.YtdlpVersion is not null)
        {
            _notificationService.Send(new AppNotification(_translationService._("yt-dlp {0} installed successfully", e.YtdlpVersion), NotificationSeverity.Success));
        }
    }

    private async Task<string> ExecuteAsync(string executable, string arguments)
    {
        using var process = new Process()
        {
            StartInfo = new ProcessStartInfo(executable, arguments)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };
        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask;
        return process.ExitCode == 0 ? output : string.Empty;
    }

    private async void ConfigurationService_Saved(object? sender, ConfigurationSavedEventArgs e)
    {
        if (e.ChangedPropertyName == "PreventSuspend")
        {
            if (_configurationService.PreventSuspend)
            {
                await _powerService.PreventSuspendAsync();
            }
            else
            {
                await _powerService.AllowSuspendAsync();
            }
        }
    }
}
