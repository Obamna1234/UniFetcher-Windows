using Microsoft.Extensions.Logging;
using Nickvision.Desktop.Application;
using Nickvision.Desktop.Globalization;
using Nickvision.Desktop.Notifications;
using Nickvision.Parabolic.Shared.Events;
using Nickvision.Parabolic.Shared.Helpers;
using Nickvision.Parabolic.Shared.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Nickvision.Parabolic.Shared.Services;

public partial class DownloadService : IDisposable, IDownloadService
{
    private readonly ILogger<DownloadService> _logger;
    private readonly IConfigurationService _configurationService;
    private readonly IHistoryService _historyService;
    private readonly IRecoveryService _recoveryService;
    private readonly ITranslationService _translationService;
    private readonly IYtdlpExecutableService _ytdlpService;
    private readonly IToolUpdateService _toolUpdateService;
    private readonly IDownloadRegistryService _downloadRegistryService;
    private readonly IQueueGuardService _queueGuard;
    private readonly ISiteSessionService _sessions;
    private readonly IActionNeededService _actionNeeded;
    private readonly INotificationService _notificationService;
    // Session retries per download (first session + one refresh at most)
    private readonly Dictionary<string, int> _sessionRetries = new Dictionary<string, int>();
    private readonly object _queueLock = new object();
    // Downloads already retried once after a yt-dlp update (spec: retry once only)
    private readonly HashSet<string> _retriedAfterUpdate;
    private readonly Dictionary<int, Download> _downloading;
    private readonly Dictionary<int, Download> _queued;
    private readonly Dictionary<int, Download> _completed;

    public event EventHandler<DownloadAddedEventArgs>? DownloadAdded;
    public event EventHandler<DownloadCompletedEventArgs>? DownloadCompleted;
    public event EventHandler<DownloadProgressChangedEventArgs>? DownloadProgressChanged;
    public event EventHandler<DownloadEventArgs>? DownloadRetired;
    public event EventHandler<DownloadEventArgs>? DownloadStartedFromQueue;
    public event EventHandler<DownloadEventArgs>? DownloadStopped;
    public event EventHandler<DownloadFinishedEventArgs>? DownloadFinished;

    public int DownloadingCount => _downloading.Count;
    public int QueuedCount => _queued.Count;
    public int CompletedCount => _completed.Count;

    public DownloadService(ILogger<DownloadService> logger, IConfigurationService configurationService, IHistoryService historyService, IRecoveryService recoveryService, ITranslationService translationService, IYtdlpExecutableService ytdlpService, IToolUpdateService toolUpdateService, IDownloadRegistryService downloadRegistryService, IQueueGuardService queueGuard, ISiteSessionService sessions, INotificationService notificationService, IActionNeededService actionNeeded)
    {
        _logger = logger;
        _configurationService = configurationService;
        _historyService = historyService;
        _recoveryService = recoveryService;
        _translationService = translationService;
        _ytdlpService = ytdlpService;
        _toolUpdateService = toolUpdateService;
        _downloadRegistryService = downloadRegistryService;
        _queueGuard = queueGuard;
        _sessions = sessions;
        _actionNeeded = actionNeeded;
        _notificationService = notificationService;
        _queueGuard.Resumed += (_, _) => StartNextQueued();
        _retriedAfterUpdate = new HashSet<string>();
        _downloading = new Dictionary<int, Download>();
        _queued = new Dictionary<int, Download>();
        _completed = new Dictionary<int, Download>();
    }

    ~DownloadService()
    {
        Dispose(false);
    }

    public int FailedCount
    {
        get
        {
            var failed = 0;
            foreach (var pair in _completed)
            {
                if (pair.Value.Status == DownloadStatus.Error && !pair.Value.Reason.NeedsAction())
                {
                    failed++;
                }
            }
            return failed;
        }
    }

    /// <summary>UniFetcher: failed downloads waiting for you (this session).</summary>
    public int ActionNeededCount => CountCompleted(d => d.Status == DownloadStatus.Error && d.Reason.NeedsAction());

    public int StoppedCount => CountCompleted(d => d.Status == DownloadStatus.Stopped);

    public int FinishedCount => CountCompleted(d => d.Status == DownloadStatus.Success);

    private int CountCompleted(Func<Download, bool> match)
    {
        var count = 0;
        foreach (var pair in _completed)
        {
            if (match(pair.Value))
            {
                count++;
            }
        }
        return count;
    }

    public async Task AddAsync(DownloadOptions options, bool excludeFromHistory)
    {
        _queueGuard.StampDrive(options);
        var download = new Download(_configurationService, _translationService, _ytdlpService, options);
        _logger.LogDebug($"Adding download ({download.Id}): {JsonSerializer.Serialize(options, ApplicationJsonContext.Default.DownloadOptions)}");
        download.Completed += Download_Completed;
        download.ProgressChanged += Download_ProgressChanged;
        if (string.IsNullOrEmpty(options.BulkItemId))
        {
            await _recoveryService.AddAsync(new RecoverableDownload(download.Id, download.Options));
        }
        if (!excludeFromHistory && !_configurationService.Incognito)
        {
            await _historyService.AddAsync(new HistoricDownload(download.Options.Url)
            {
                Title = Path.GetFileNameWithoutExtension(download.FilePath),
                Path = download.FilePath
            });
        }
        if (_downloading.Count < _configurationService.MaxNumberOfActiveDownloads && _queueGuard.CanStart(download.Options))
        {
            _logger.LogDebug($"Starting download ({download.Id}).");
            download.RefreshFilePath();
            _downloading.Add(download.Id, download);
            DownloadAdded?.Invoke(this, new DownloadAddedEventArgs(download.Id, download.FilePath, download.Options.Url, DownloadStatus.Running));
            await BeginAsync(download);
        }
        else
        {
            _logger.LogDebug($"Queueing download ({download.Id})...");
            _queued.Add(download.Id, download);
            DownloadAdded?.Invoke(this, new DownloadAddedEventArgs(download));
        }
    }

    public async Task AddAsync(IReadOnlyList<DownloadOptions> options, bool excludeFromHistory)
    {
        var recoverableDownloads = new List<RecoverableDownload>();
        var historicDownloads = new List<HistoricDownload>();
        var downloadsToStart = new List<Download>();
        foreach (var option in options)
        {
            _queueGuard.StampDrive(option);
            var download = new Download(_configurationService, _translationService, _ytdlpService, option);
            _logger.LogDebug($"Adding download ({download.Id}): {JsonSerializer.Serialize(option, ApplicationJsonContext.Default.DownloadOptions)}");
            download.Completed += Download_Completed;
            download.ProgressChanged += Download_ProgressChanged;
            if (string.IsNullOrEmpty(option.BulkItemId))
            {
                recoverableDownloads.Add(new RecoverableDownload(download.Id, download.Options));
            }
            if (!excludeFromHistory && !_configurationService.Incognito)
            {
                historicDownloads.Add(new HistoricDownload(download.Options.Url)
                {
                    Title = Path.GetFileNameWithoutExtension(download.FilePath),
                    Path = download.FilePath
                });
            }
            if (_downloading.Count < _configurationService.MaxNumberOfActiveDownloads && _queueGuard.CanStart(download.Options))
            {
                _logger.LogDebug($"Starting download ({download.Id}).");
                download.RefreshFilePath();
                _downloading.Add(download.Id, download);
                DownloadAdded?.Invoke(this, new DownloadAddedEventArgs(download.Id, download.FilePath, download.Options.Url, DownloadStatus.Running));
                downloadsToStart.Add(download);
            }
            else
            {
                _logger.LogDebug($"Queueing download ({download.Id})...");
                _queued.Add(download.Id, download);
                DownloadAdded?.Invoke(this, new DownloadAddedEventArgs(download));
            }
        }
        await _recoveryService.AddAsync(recoverableDownloads);
        await _historyService.AddAsync(historicDownloads);
        foreach (var download in downloadsToStart)
        {
            await BeginAsync(download);
        }
    }

    public IReadOnlyList<int> ClearCompleted()
    {
        _logger.LogDebug($"Clearing completed downloads...");
        var ids = new List<int>(_completed.Keys);
        foreach (var pair in _completed)
        {
            pair.Value.Completed -= Download_Completed;
            pair.Value.ProgressChanged -= Download_ProgressChanged;
            pair.Value.Dispose();
        }
        _completed.Clear();
        _logger.LogDebug($"Cleared {ids.Count} completed download(s).");
        return ids;
    }

    /// <summary>UniFetcher: removes failed and stopped downloads from the list (finished ones stay).</summary>
    public IReadOnlyList<int> ClearFailedAndStopped()
    {
        var ids = new List<int>();
        foreach (var pair in _completed)
        {
            if (pair.Value.Status is DownloadStatus.Error or DownloadStatus.Stopped)
            {
                ids.Add(pair.Key);
            }
        }
        foreach (var id in ids)
        {
            var download = _completed[id];
            download.Completed -= Download_Completed;
            download.ProgressChanged -= Download_ProgressChanged;
            download.Dispose();
            _completed.Remove(id);
        }
        return ids;
    }

    /// <summary>UniFetcher: puts everything you stopped back in the queue.</summary>
    public async Task ResumeStoppedAsync()
    {
        var ids = new List<int>();
        foreach (var pair in _completed)
        {
            if (pair.Value.Status == DownloadStatus.Stopped)
            {
                ids.Add(pair.Key);
            }
        }
        foreach (var id in ids)
        {
            await RetryAsync(id);
        }
    }

    /// <summary>UniFetcher: true if this link is downloading or waiting in the queue right now.</summary>
    public bool IsInQueue(string url)
    {
        foreach (var download in _downloading.Values)
        {
            if (download.Options.Url.ToString() == url)
            {
                return true;
            }
        }
        foreach (var download in _queued.Values)
        {
            if (download.Options.Url.ToString() == url)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>UniFetcher: ids of failed downloads from one site that are waiting for you.</summary>
    public IReadOnlyList<int> WaitingIdsForSite(string site)
    {
        var ids = new List<int>();
        foreach (var pair in _completed)
        {
            if (pair.Value.Status == DownloadStatus.Error && pair.Value.Reason.NeedsAction() && SiteSessionService.SiteOf(pair.Value.Options.Url) == site)
            {
                ids.Add(pair.Key);
            }
        }
        return ids;
    }

    public IReadOnlyList<int> ClearQueued()
    {
        _logger.LogDebug($"Clearing queued downloads...");
        var ids = new List<int>(_queued.Keys);
        foreach (var pair in _queued)
        {
            pair.Value.Completed -= Download_Completed;
            pair.Value.ProgressChanged -= Download_ProgressChanged;
            pair.Value.Dispose();
        }
        _queued.Clear();
        _logger.LogDebug($"Cleared {ids.Count} queued download(s).");
        return ids;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public bool Pause(int id)
    {
        _logger.LogDebug($"Pausing download ({id})...");
        if (_downloading.TryGetValue(id, out var download))
        {
            download.Pause();
            _logger.LogDebug($"Paused download ({id}).");
            return true;
        }
        _logger.LogWarning($"Unable to pause download ({id}), not found.");
        return false;
    }

    public async Task RecoverAllAsync()
    {
        var downloads = await _recoveryService.GetAllAsync();
        await _recoveryService.ClearAsync();
        var options = new List<DownloadOptions>(downloads.Count);
        foreach (var recoverableDownload in downloads)
        {
            options.Add(recoverableDownload.Options);
        }
        await AddAsync(options, false);
    }

    public bool Resume(int id)
    {
        _logger.LogDebug($"Resuming download ({id})...");
        if (_downloading.TryGetValue(id, out var download))
        {
            download.Resume();
            _logger.LogDebug($"Resumed download ({id}).");
            return true;
        }
        _logger.LogWarning($"Unable to resume download ({id}), not found.");
        return false;
    }

    public async Task<bool> RetryAsync(int id)
    {
        _logger.LogDebug($"Retrying download ({id})...");
        if (_completed.TryGetValue(id, out var download))
        {
            _logger.LogDebug($"Retried download ({id}).");
            DownloadRetired?.Invoke(this, new DownloadEventArgs(id));
            await AddAsync(download.Options, true);
            download.Completed -= Download_Completed;
            download.ProgressChanged -= Download_ProgressChanged;
            download.Dispose();
            _completed.Remove(id);
            return true;
        }
        _logger.LogWarning($"Unable to retry download ({id}), not found.");
        return false;
    }

    public async Task RetryFailedAsync()
    {
        _logger.LogDebug($"Retrying failed downloads...");
        var retryDownloadOptions = new List<DownloadOptions>();
        var ids = new List<int>();
        foreach (var pair in _completed)
        {
            // UniFetcher: downloads that need you (login, person check, VPN) aren't retried blindly
            if (pair.Value.Status == DownloadStatus.Error && !pair.Value.Reason.NeedsAction())
            {
                ids.Add(pair.Key);
            }
        }
        foreach (var id in ids)
        {
            var download = _completed[id];
            retryDownloadOptions.Add(download.Options);
            DownloadRetired?.Invoke(this, new DownloadEventArgs(id));
            download.Completed -= Download_Completed;
            download.ProgressChanged -= Download_ProgressChanged;
            download.Dispose();
            _completed.Remove(id);
            _logger.LogDebug($"Retried download ({id}).");
        }
        _logger.LogDebug($"Retried {retryDownloadOptions.Count} failed download(s).");
        await AddAsync(retryDownloadOptions, true);
    }

    public async Task<bool> StopAsync(int id)
    {
        _logger.LogDebug($"Stopping download ({id})...");
        Download? download = null;
        if (_downloading.TryGetValue(id, out download) || _queued.TryGetValue(id, out download))
        {
            download.Stop();
            download.Completed -= Download_Completed;
            download.ProgressChanged -= Download_ProgressChanged;
            download.Dispose();
            _downloading.Remove(id);
            _queued.Remove(id);
            _completed.Add(id, download);
            await _recoveryService.RemoveAsync(id);
            _logger.LogDebug($"Stopped download ({id}).");
            DownloadStopped?.Invoke(this, new DownloadEventArgs(id));
            DownloadFinished?.Invoke(this, new DownloadFinishedEventArgs(download.Options, DownloadStatus.Stopped, download.Log, download.FilePath));
            return true;
        }
        _logger.LogWarning($"Unable to stop download ({id}), not found.");
        return false;
    }

    public async Task StopAllAsync()
    {
        _logger.LogDebug($"Stopping all downloads...");
        var ids = new List<int>(_downloading.Count + _queued.Count);
        foreach (var id in _downloading.Keys)
        {
            ids.Add(id);
        }
        foreach (var id in _queued.Keys)
        {
            ids.Add(id);
        }
        foreach (var pair in _downloading)
        {
            pair.Value.Stop();
            pair.Value.Completed -= Download_Completed;
            pair.Value.ProgressChanged -= Download_ProgressChanged;
            pair.Value.Dispose();
            _completed.Add(pair.Key, pair.Value);
            _logger.LogDebug($"Stopped download ({pair.Key}).");
            DownloadStopped?.Invoke(this, new DownloadEventArgs(pair.Key));
            DownloadFinished?.Invoke(this, new DownloadFinishedEventArgs(pair.Value.Options, DownloadStatus.Stopped, pair.Value.Log, pair.Value.FilePath));
        }
        foreach (var pair in _queued)
        {
            pair.Value.Stop();
            pair.Value.Completed -= Download_Completed;
            pair.Value.ProgressChanged -= Download_ProgressChanged;
            pair.Value.Dispose();
            _completed.Add(pair.Key, pair.Value);
            _logger.LogDebug($"Stopped download ({pair.Key}).");
            DownloadStopped?.Invoke(this, new DownloadEventArgs(pair.Key));
            DownloadFinished?.Invoke(this, new DownloadFinishedEventArgs(pair.Value.Options, DownloadStatus.Stopped, pair.Value.Log, pair.Value.FilePath));
        }
        _downloading.Clear();
        _queued.Clear();
        await _recoveryService.RemoveAsync(ids);
        _logger.LogDebug($"Stopped {ids.Count} download(s).");
    }

    private void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }
        foreach (var pair in _downloading)
        {
            pair.Value.Completed -= Download_Completed;
            pair.Value.ProgressChanged -= Download_ProgressChanged;
            pair.Value.Dispose();
        }
        foreach (var pair in _queued)
        {
            pair.Value.Completed -= Download_Completed;
            pair.Value.ProgressChanged -= Download_ProgressChanged;
            pair.Value.Dispose();
        }
        foreach (var pair in _completed)
        {
            pair.Value.Completed -= Download_Completed;
            pair.Value.ProgressChanged -= Download_ProgressChanged;
            pair.Value.Dispose();
        }
    }

    private async void Download_Completed(object? sender, DownloadCompletedEventArgs e)
    {
        if (!_downloading.TryGetValue(e.Id, out var download) || download.Status == DownloadStatus.Stopped)
        {
            return;
        }
        _completed.Add(e.Id, download);
        _downloading.Remove(e.Id);
        await _recoveryService.RemoveAsync(e.Id);
        if (e.Status == DownloadStatus.Error)
        {
            _logger.LogError($"Download failed ({e.Id}): {download.Log}");
            // UniFetcher: lost internet / unplugged or full drive is not the download's fault: put it back in the queue (the queue pauses)
            var kind = await _queueGuard.ClassifyFailureAsync(download.Options, download.Log);
            if (kind != FailureKind.Other)
            {
                _logger.LogInformation($"Download ({e.Id}) interrupted ({kind}); putting it back in the queue.");
                await RequeueAsync(download);
                return;
            }
            // UniFetcher: if moving the finished video into the download folder failed, the file is still in the temporary
            // folder: never delete it, and say where it is
            if (MoveFailedRegex().IsMatch(download.Log))
            {
                var tempFolder = string.IsNullOrEmpty(_configurationService.TempFolder) ? download.Options.SaveFolder : Path.Combine(_configurationService.TempFolder, "UniFetcher temp");
                download.AddNote(_translationService._("The video was downloaded but couldn't be moved into the download folder. It's safe in: {0}", tempFolder));
                download.Options.Steps.Add($"Moving the finished file failed; it was kept in the temporary folder");
            }
            // UniFetcher: 403/410 -> get (or refresh once) a session in the UniFetcher Firefox profile and retry; logins/CAPTCHAs -> needs my action
            var usedCookies = !string.IsNullOrEmpty(download.Options.CookieProfile);
            var outcome = await _sessions.HandleFailureAsync(download.Options.Url, download.Log, usedCookies);
            var sessionKey = download.Options.Url.ToString() + "|" + download.FilePath;
            _sessionRetries.TryGetValue(sessionKey, out var sessionTries);
            if (outcome.Decision == SessionDecision.RetryWithCookies && sessionTries < 2)
            {
                _sessionRetries[sessionKey] = sessionTries + 1;
                download.Options.Steps.Add(usedCookies ? "Refreshed the site's browser session and retried" : "Made a browser session for the site (UniFetcher profile) and retried");
                DownloadCompleted?.Invoke(this, e);
                _logger.LogInformation($"Retrying download ({e.Id}) with a site session.");
                await RetryAsync(e.Id);
                StartNextQueued();
                return;
            }
            if (outcome.Decision == SessionDecision.NeedsAction)
            {
                // One grouped "N videos need your action" message is shown instead of one per download
                download.AddNote(outcome.Message);
            }
            var retryKey = download.Options.Url.ToString() + "|" + download.FilePath;
            if (ToolUpdateService.IsExtractorError(download.Log) && _retriedAfterUpdate.Add(retryKey))
            {
                // UniFetcher: extractor / "unsupported" error -> update yt-dlp if a newer one exists, then retry once
                DownloadCompleted?.Invoke(this, e);
                if (await _toolUpdateService.TryUpdateAfterExtractorErrorAsync())
                {
                    download.Options.Steps.Add($"Updated yt-dlp to {await _toolUpdateService.GetYtdlpVersionAsync()} and retried");
                    _logger.LogInformation($"Retrying download ({e.Id}) with the updated yt-dlp.");
                    await RetryAsync(e.Id);
                }
                else
                {
                    download.Options.Steps.Add("Checked for a newer yt-dlp: none available");
                    DownloadFinished?.Invoke(this, new DownloadFinishedEventArgs(download.Options, e.Status, download.Log, download.FilePath));
                }
                StartNextQueued();
                return;
            }
        }
        else if (e.Status == DownloadStatus.Success)
        {
            _logger.LogDebug($"Download completed ({e.Id}): {download.Log}");
            await _sessions.MarkSuccessAsync(download.Options.Url, !string.IsNullOrEmpty(download.Options.CookieProfile));
            // UniFetcher: remember it, so the same video isn't downloaded twice by accident
            try
            {
                if (!_configurationService.Incognito)
                await _downloadRegistryService.MarkDownloadedAsync(download.Options.Url, download.Options.VideoKey, Path.GetFileNameWithoutExtension(download.FilePath), download.FilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Could not remember completed download ({e.Id}): {ex.Message}");
            }
        }
        else if (e.Status == DownloadStatus.Stopped)
        {
            _logger.LogDebug($"Download stopped ({e.Id}): {download.Log}");
        }
        if (e.Status == DownloadStatus.Error)
        {
            // Work out the plain-language reason now that UniFetcher's own notes are in the log
            e = download.BuildCompletedArgs(e.ShowNotification);
            if (e.Reason.NeedsAction() && string.IsNullOrEmpty(download.Options.BulkItemId))
            {
                // Kept in the "Action needed" list (survives closing UniFetcher) until you press Done
                await _actionNeeded.AddAsync(download.Options, e.Reason);
            }
        }
        else if (e.Status == DownloadStatus.Success)
        {
            await _actionNeeded.RemoveAsync(download.Options.Url.ToString());
        }
        DownloadCompleted?.Invoke(this, e);
        DownloadFinished?.Invoke(this, new DownloadFinishedEventArgs(download.Options, e.Status, download.Log, download.FilePath));
        StartNextQueued();
    }

    private void StartNextQueued()
    {
        lock (_queueLock)
        {
            while (_queued.Count > 0 && _downloading.Count < _configurationService.MaxNumberOfActiveDownloads)
            {
                using var queuedEnumerator = _queued.GetEnumerator();
                if (!queuedEnumerator.MoveNext())
                {
                    return;
                }
                var firstDownload = queuedEnumerator.Current.Value;
                if (!_queueGuard.CanStart(firstDownload.Options))
                {
                    // Paused (no internet / drive missing or full); the guard calls back when it's resolved
                    return;
                }
                firstDownload.RefreshFilePath();
                _downloading.Add(firstDownload.Id, firstDownload);
                _queued.Remove(firstDownload.Id);
                _logger.LogDebug($"Starting download from queue ({firstDownload.Id}).");
                DownloadStartedFromQueue?.Invoke(this, new DownloadEventArgs(firstDownload.Id));
                _ = BeginAsync(firstDownload);
            }
        }
    }

    /// <summary>Starts a download, adding the UniFetcher profile's cookies first if its site needs a session.</summary>
    private async Task BeginAsync(Download download)
    {
        try
        {
            download.Options.CookieProfile = await _sessions.GetCookieProfileAsync(download.Options.Url);
        }
        catch (Exception e)
        {
            _logger.LogWarning($"Could not prepare cookies for download ({download.Id}): {e.Message}");
            download.Options.CookieProfile = null;
        }
        download.Start();
    }

    /// <summary>Puts an interrupted download back in the queue as a fresh item (partial files are kept, so yt-dlp continues where it stopped).</summary>
    private async Task RequeueAsync(Download old)
    {
        _completed.Remove(old.Id);
        old.Completed -= Download_Completed;
        old.ProgressChanged -= Download_ProgressChanged;
        DownloadRetired?.Invoke(this, new DownloadEventArgs(old.Id));
        var download = new Download(_configurationService, _translationService, _ytdlpService, old.Options);
        download.Completed += Download_Completed;
        download.ProgressChanged += Download_ProgressChanged;
        old.Dispose();
        lock (_queueLock)
        {
            _queued.Add(download.Id, download);
        }
        if (string.IsNullOrEmpty(download.Options.BulkItemId))
        {
            await _recoveryService.AddAsync(new RecoverableDownload(download.Id, download.Options));
        }
        DownloadAdded?.Invoke(this, new DownloadAddedEventArgs(download));
    }

    private void Download_ProgressChanged(object? sender, DownloadProgressChangedEventArgs e)
    {
        if (!_downloading.TryGetValue(e.Id, out var download) || download.Status != DownloadStatus.Running)
        {
            return;
        }
        DownloadProgressChanged?.Invoke(this, e);
    }

    [GeneratedRegex(@"Unable to rename file|Unable to move|Errno 18|Invalid cross-device link", RegexOptions.IgnoreCase)]
    private static partial Regex MoveFailedRegex();
}
