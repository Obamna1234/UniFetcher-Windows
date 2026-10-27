using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nickvision.Desktop.Application;
using Nickvision.Desktop.Globalization;
using Nickvision.Desktop.Notifications;
using Nickvision.Parabolic.Shared.Events;
using Nickvision.Parabolic.Shared.Helpers;
using Nickvision.Parabolic.Shared.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Nickvision.Parabolic.Shared.Services;

public interface IBulkImportService
{
    /// <summary>Something changed in an import (raised on a background thread).</summary>
    event EventHandler<string>? JobChanged;

    /// <summary>An import has no more work to do (raised on a background thread).</summary>
    event EventHandler<string>? JobFinished;

    Task<BulkPlan> PlanAsync(ParsedLinks links);

    Task<string> StartAsync(BulkPlan plan, string saveFolder);

    Task<IReadOnlyList<BulkJob>> GetJobsAsync();

    Task<IReadOnlyList<BulkItem>> GetItemsAsync(string jobId);

    Task RetryFailedAsync(string jobId);

    Task<string> GetReportAsync(string jobId);

    Task<string> GetFailedLinksAsync(string jobId);

    string StateLabel(BulkItemState state);

    /// <summary>Forgets imports that have nothing left to do (unfinished ones are kept so they can carry on).</summary>
    Task ClearFinishedAsync();
}

/// <summary>
/// Bulk Import (spec section 5 / Android 4a): works through a long list of links on its own, using the normal queue,
/// concurrency limit and polite delays. Progress lives in the database, so it carries on after closing UniFetcher or restarting the PC.
/// </summary>
public partial class BulkImportService : BackgroundService, IBulkImportService
{
    private const string JobsTable = "unifetcher_bulk_jobs";
    private const string ItemsTable = "unifetcher_bulk_items";
    private const int MaxTemporaryRetries = 3;
    private static readonly TimeSpan[] RetryWaits = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15)];

    private readonly ILogger<BulkImportService> _logger;
    private readonly IConfigurationService _configurationService;
    private readonly IDatabaseService _databaseService;
    private readonly IDiscoveryService _discoveryService;
    private readonly IDownloadService _downloadService;
    private readonly IDownloadRegistryService _registry;
    private readonly INotificationService _notificationService;
    private readonly ITranslationService _translationService;
    private readonly SemaphoreSlim _dbLock;
    private readonly SemaphoreSlim _wake;
    private bool _tablesEnsured;

    public event EventHandler<string>? JobChanged;
    public event EventHandler<string>? JobFinished;

    public BulkImportService(ILogger<BulkImportService> logger, IConfigurationService configurationService, IDatabaseService databaseService, IDiscoveryService discoveryService, IDownloadService downloadService, IDownloadRegistryService registry, INotificationService notificationService, ITranslationService translationService)
    {
        _logger = logger;
        _configurationService = configurationService;
        _databaseService = databaseService;
        _discoveryService = discoveryService;
        _downloadService = downloadService;
        _registry = registry;
        _notificationService = notificationService;
        _translationService = translationService;
        _dbLock = new SemaphoreSlim(1, 1);
        _wake = new SemaphoreSlim(0, int.MaxValue);
        _downloadService.DownloadFinished += DownloadService_DownloadFinished;
    }

    // ---------------- Planning (summary screen) ----------------

    public async Task<BulkPlan> PlanAsync(ParsedLinks links)
    {
        await EnsureTablesAsync();
        var plan = new BulkPlan { Format = links.Format, Total = links.Links.Count + links.NotWebPages.Count };
        plan.NotWebPages.AddRange(links.NotWebPages);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var unfinished = await GetUnfinishedNormalizedUrlsAsync();
        foreach (var link in links.Links)
        {
            var normalized = DownloadRegistryService.NormalizeUrl(link.Url);
            PlanStatus status;
            if (!seen.Add(normalized))
            {
                status = PlanStatus.DuplicateInList;
            }
            else if (_configurationService.SkipAlreadyDownloaded && await _registry.FindAsync(link.Url, null) is not null)
            {
                status = PlanStatus.AlreadyDownloaded;
            }
            else if (unfinished.Contains(normalized))
            {
                status = PlanStatus.AlreadyImporting;
            }
            else
            {
                status = PlanStatus.New;
            }
            var isCollection = IsCollection(link.Url);
            plan.Entries.Add(new PlanEntry
            {
                Url = link.Url,
                Title = link.Title,
                Site = SiteSessionService.SiteOf(link.Url),
                NormalizedUrl = normalized,
                Status = status,
                IsCollection = isCollection,
                // Everything new is ticked; playlists/channels/profiles are only expanded if you tick them
                Selected = status == PlanStatus.New && !isCollection
            });
        }
        return plan;
    }

    /// <summary>Links to whole channels, profiles or playlists (not expanded automatically).</summary>
    public static bool IsCollection(Uri url)
    {
        var host = url.Host.ToLowerInvariant();
        var path = url.AbsolutePath.ToLowerInvariant().TrimEnd('/');
        var query = url.Query.ToLowerInvariant();
        if (host.EndsWith("youtube.com", StringComparison.Ordinal))
        {
            if (path == "/watch" || path.StartsWith("/shorts/", StringComparison.Ordinal) || path.StartsWith("/live/", StringComparison.Ordinal))
            {
                return false;
            }
            return path == "/playlist" || path.StartsWith("/@", StringComparison.Ordinal) || path.StartsWith("/channel/", StringComparison.Ordinal) || path.StartsWith("/c/", StringComparison.Ordinal) || path.StartsWith("/user/", StringComparison.Ordinal);
        }
        return CollectionPathRegex().IsMatch(path) || query.Contains("list=", StringComparison.Ordinal) && !query.Contains("v=", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"(^|/)(playlist|playlists|channel|channels|user|users|profile|profiles|videos|collections?|series|album|albums|sets|show|shows)(/|$)", RegexOptions.IgnoreCase)]
    private static partial Regex CollectionPathRegex();

    // ---------------- Starting ----------------

    public async Task<string> StartAsync(BulkPlan plan, string saveFolder)
    {
        await EnsureTablesAsync();
        var jobId = Guid.NewGuid().ToString("N");
        var selected = plan.Entries.Where(x => x.Selected && x.Status == PlanStatus.New).ToList();
        await _dbLock.WaitAsync();
        try
        {
            using var transaction = await _databaseService.CreateTransactionAsync();
            await _databaseService.InsertIntoTableAsync(JobsTable, new Dictionary<string, object>
            {
                { "id", jobId },
                { "created", DateTime.Now.ToString("o") },
                { "folder", saveFolder },
                { "name", _translationService._("Import of {0} links", selected.Count) }
            });
            foreach (var entry in selected)
            {
                await InsertItemAsync(new BulkItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    JobId = jobId,
                    Url = entry.Url.ToString(),
                    NormalizedUrl = entry.NormalizedUrl,
                    Site = entry.Site,
                    Title = entry.Title,
                    State = BulkItemState.Waiting,
                    SaveFolder = saveFolder,
                    IsCollection = entry.IsCollection
                });
            }
            await transaction.CommitAsync();
        }
        finally
        {
            _dbLock.Release();
        }
        _logger.LogInformation($"Bulk import {jobId} started with {selected.Count} links.");
        JobChanged?.Invoke(this, jobId);
        _wake.Release();
        return jobId;
    }

    // ---------------- Worker ----------------

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await EnsureTablesAsync();
            // After a restart, links that were in the download queue go back to waiting (partial files are kept and continued)
            await _dbLock.WaitAsync(stoppingToken);
            try
            {
                await _databaseService.ExecuteNonQueryAsync($"UPDATE {ItemsTable} SET state = {(int)BulkItemState.Waiting} WHERE state = {(int)BulkItemState.InQueue}");
            }
            finally
            {
                _dbLock.Release();
            }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                var worked = false;
                // Keep the download queue topped up, but don't fetch too far ahead (sites see one request at a time)
                if (_downloadService.QueuedCount < 2)
                {
                    var next = await GetNextWaitingAsync();
                    if (next is not null)
                    {
                        await ProcessAsync(next, stoppingToken);
                        worked = true;
                    }
                }
                if (!worked)
                {
                    await _wake.WaitAsync(TimeSpan.FromSeconds(15), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // closing
        }
        catch (Exception e)
        {
            _logger.LogError($"Bulk import stopped unexpectedly: {e}");
        }
    }

    private async Task ProcessAsync(BulkItem item, CancellationToken token)
    {
        _logger.LogDebug($"Bulk import: fetching {item.Url}");
        DiscoveryResult result;
        try
        {
            result = await _discoveryService.GetForUrlAsync(new Uri(item.Url), null, token);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            await ApplyFailureAsync(item, e.Message);
            return;
        }
        if (result.Media.Count == 0)
        {
            await SetStateAsync(item, BulkItemState.NoVideoFound, _translationService._("No video on this page"));
            return;
        }
        if (result.IsPlaylist)
        {
            // A playlist / channel / profile, or a page with several videos: each becomes its own link in this import
            if (result.Media.Count > 50)
            {
                _notificationService.Send(new AppNotification(_translationService._("{0} contains {1} videos. They've been added; downloading this many from one site can get you blocked, but UniFetcher's polite delays help.", item.Url, result.Media.Count), NotificationSeverity.Warning));
            }
            await _dbLock.WaitAsync(token);
            try
            {
                using var transaction = await _databaseService.CreateTransactionAsync();
                foreach (var media in result.Media)
                {
                    await InsertItemAsync(new BulkItem
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        JobId = item.JobId,
                        Url = media.Url.ToString(),
                        NormalizedUrl = DownloadRegistryService.NormalizeUrl(media.Url),
                        Site = SiteSessionService.SiteOf(media.Url),
                        Title = media.Title,
                        State = BulkItemState.Waiting,
                        SaveFolder = item.SaveFolder
                    });
                }
                await transaction.CommitAsync();
            }
            finally
            {
                _dbLock.Release();
            }
            await SetStateAsync(item, BulkItemState.Expanded, _translationService._("{0} videos added", result.Media.Count));
            return;
        }
        var single = result.Media[0];
        item.VideoKey = single.VideoKey;
        if (string.IsNullOrEmpty(item.Title))
        {
            item.Title = single.Title;
        }
        var already = !_configurationService.SkipAlreadyDownloaded ? null : await _registry.FindAsync(single.Url, single.VideoKey) ?? await _registry.FindAsync(new Uri(item.Url), single.VideoKey);
        if (already is not null)
        {
            await SetStateAsync(item, BulkItemState.SkippedDuplicate, _translationService._("Already downloaded on {0}", already.DownloadedOn.ToString("d")));
            return;
        }
        if (!string.IsNullOrEmpty(single.VideoKey) && await IsVideoKeyActiveElsewhereAsync(item, single.VideoKey))
        {
            await SetStateAsync(item, BulkItemState.SkippedDuplicate, _translationService._("Same video as another link in this import"));
            return;
        }
        if (string.IsNullOrEmpty(item.SaveFilename))
        {
            item.SaveFolder = FileNameHelper.SiteFolder(item.SaveFolder, single.Url, _configurationService.SubfolderPerSite);
            // Keep the name for later attempts, so an interrupted download continues instead of starting over under a new name
            item.SaveFilename = FileNameHelper.UniqueName(item.SaveFolder, single.Title.SanitizeForFilename(_configurationService.LimitCharacters));
        }
        var options = new DownloadOptions(single.Url)
        {
            SaveFolder = item.SaveFolder,
            SaveFilename = item.SaveFilename,
            FileType = single.Type == MediaType.Audio ? MediaFileType.Audio : MediaFileType.MP4,
            PlaylistPosition = single.PlaylistPosition,
            RequiresPlaylistItems = single.RequiresPlaylistItems,
            // The quality rule: best up to 1080p, prefer 720p+, never re-encode
            VideoResolution = VideoResolution.Smart,
            AudioBitrate = double.MaxValue,
            VideoKey = single.VideoKey,
            BulkItemId = item.Id
        };
        item.State = BulkItemState.InQueue;
        item.Note = string.Empty;
        await SaveItemAsync(item);
        JobChanged?.Invoke(this, item.JobId);
        await _downloadService.AddAsync(options, _configurationService.Incognito);
    }

    private async void DownloadService_DownloadFinished(object? sender, DownloadFinishedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Options.BulkItemId))
        {
            return;
        }
        try
        {
            var item = await GetItemAsync(e.Options.BulkItemId);
            if (item is null)
            {
                return;
            }
            switch (e.Status)
            {
                case DownloadStatus.Success:
                    item.Title = string.IsNullOrEmpty(item.Title) ? System.IO.Path.GetFileNameWithoutExtension(e.FilePath) : item.Title;
                    await SetStateAsync(item, BulkItemState.Downloaded, e.FilePath);
                    break;
                case DownloadStatus.Stopped:
                    await SetStateAsync(item, BulkItemState.Stopped, _translationService._("Stopped by you"));
                    break;
                default:
                    await ApplyFailureAsync(item, e.Log);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Bulk import could not record a result: {ex.Message}");
        }
        _wake.Release();
    }

    /// <summary>Sorts errors so the app knows when retrying is worthwhile.</summary>
    private async Task ApplyFailureAsync(BulkItem item, string log)
    {
        var kind = ClassifyError(log);
        var detail = LastErrorLine(log);
        switch (kind)
        {
            case ErrorKind.NeedsAction:
                var note = NoteFromLog(log);
                await SetStateAsync(item, BulkItemState.NeedsAction, string.IsNullOrEmpty(note) ? detail : note);
                break;
            case ErrorKind.Permanent:
                await SetStateAsync(item, BulkItemState.Unavailable, detail);
                break;
            case ErrorKind.NoVideo:
                await SetStateAsync(item, BulkItemState.NoVideoFound, _translationService._("No video on this page"));
                break;
            default:
                item.Attempts++;
                if (item.Attempts > MaxTemporaryRetries)
                {
                    await SetStateAsync(item, BulkItemState.Failed, detail);
                }
                else
                {
                    var wait = RetryWaits[Math.Min(item.Attempts - 1, RetryWaits.Length - 1)];
                    item.RetryAfter = DateTime.Now + wait;
                    await SetStateAsync(item, BulkItemState.FailedWillRetry, _translationService._("Will retry in {0} min ({1} of {2}): {3}", (int)wait.TotalMinutes, item.Attempts, MaxTemporaryRetries, detail));
                }
                break;
        }
    }

    private enum ErrorKind
    {
        Temporary,
        Permanent,
        NoVideo,
        NeedsAction
    }

    private static ErrorKind ClassifyError(string fullLog)
    {
        // Only look at the error lines (the verbose log mentions many words that aren't errors)
        var lines = fullLog.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var important = lines.Where(l => l.Contains("ERROR", StringComparison.Ordinal) || l.StartsWith("UniFetcher: ", StringComparison.Ordinal)).ToList();
        var log = important.Count > 0 ? string.Join('\n', important) : string.Join('\n', lines.TakeLast(5));
        bool Has(params string[] parts) => parts.Any(p => log.Contains(p, StringComparison.OrdinalIgnoreCase));
        if (Has("UniFetcher: ") || SiteSessionService.Classify(log) is SessionProblem.NeedsLogin or SessionProblem.HumanCheck or SessionProblem.CookiesUnreadable || Has("blocking your VPN", "Log in once", "check you're a person"))
        {
            return ErrorKind.NeedsAction;
        }
        if (Has("Video unavailable", "This video has been removed", "has been removed", "no longer available", "account associated with this video has been terminated", "HTTP Error 404", "404: Not Found",
                "Private video", "This video is private", "is private", "not available in your country", "geo restrict", "geo-restrict", "available in your region", "DRM protected", "is DRM", "This live event will begin", "Premieres in",
                "bigger than 4 GB", "copyright claim", "violating YouTube's Terms"))
        {
            return ErrorKind.Permanent;
        }
        if (Has("Unsupported URL", "No video formats found", "no video could be found", "There is no video", "does not contain a video", "No media found"))
        {
            return ErrorKind.NoVideo;
        }
        return ErrorKind.Temporary;
    }

    private static string LastErrorLine(string log)
    {
        var lines = log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var error = lines.LastOrDefault(l => l.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)) ?? lines.LastOrDefault() ?? string.Empty;
        error = error.Replace("ERROR: ", string.Empty);
        return error.Length > 300 ? error[..300] + "…" : error;
    }

    private static string NoteFromLog(string log)
    {
        var line = log.Split('\n').LastOrDefault(l => l.StartsWith("UniFetcher: ", StringComparison.Ordinal));
        return line is null ? string.Empty : line["UniFetcher: ".Length..].Trim();
    }

    // ---------------- Retry / report ----------------

    public async Task RetryFailedAsync(string jobId)
    {
        foreach (var item in await GetItemsAsync(jobId))
        {
            if (item.State is BulkItemState.Failed or BulkItemState.FailedWillRetry or BulkItemState.NeedsAction or BulkItemState.Stopped)
            {
                item.Attempts = 0;
                item.RetryAfter = DateTime.MinValue;
                await SetStateAsync(item, BulkItemState.Waiting, string.Empty, raiseFinished: false);
            }
        }
        _wake.Release();
    }

    public async Task<string> GetFailedLinksAsync(string jobId)
    {
        var items = await GetItemsAsync(jobId);
        return string.Join(Environment.NewLine, items.Where(x => x.State is BulkItemState.Failed or BulkItemState.FailedWillRetry or BulkItemState.NeedsAction or BulkItemState.Unavailable or BulkItemState.NoVideoFound or BulkItemState.Stopped).Select(x => x.Url));
    }

    public async Task<string> GetReportAsync(string jobId)
    {
        var items = (await GetItemsAsync(jobId)).Where(x => x.State != BulkItemState.Expanded).ToList();
        var job = (await GetJobsAsync()).FirstOrDefault(x => x.Id == jobId);
        var builder = new StringBuilder();
        builder.AppendLine(_translationService._("UniFetcher Bulk Import report"));
        if (job is not null)
        {
            builder.AppendLine(_translationService._("Started: {0}", job.Created.ToString("g")));
            builder.AppendLine(_translationService._("Folder: {0}", job.SaveFolder));
        }
        builder.AppendLine();
        foreach (var state in Enum.GetValues<BulkItemState>().Where(s => s != BulkItemState.Expanded))
        {
            var count = items.Count(x => x.State == state);
            if (count > 0)
            {
                builder.AppendLine($"{StateLabel(state)}: {count}");
            }
        }
        foreach (var state in new[] { BulkItemState.NeedsAction, BulkItemState.Failed, BulkItemState.FailedWillRetry, BulkItemState.Unavailable, BulkItemState.NoVideoFound })
        {
            var list = items.Where(x => x.State == state).ToList();
            if (list.Count == 0)
            {
                continue;
            }
            builder.AppendLine();
            builder.AppendLine($"{StateLabel(state)}:");
            foreach (var item in list)
            {
                builder.AppendLine($"- {item.Url}{(string.IsNullOrEmpty(item.Note) ? string.Empty : "  —  " + item.Note)}");
            }
        }
        return builder.ToString();
    }

    public string StateLabel(BulkItemState state) => state switch
    {
        BulkItemState.Waiting => _translationService._("Waiting"),
        BulkItemState.InQueue => _translationService._("In download queue"),
        BulkItemState.Downloaded => _translationService._("Downloaded"),
        BulkItemState.SkippedDuplicate => _translationService._("Skipped (duplicate)"),
        BulkItemState.FailedWillRetry => _translationService._("Failed – will retry"),
        BulkItemState.Unavailable => _translationService._("Unavailable"),
        BulkItemState.NoVideoFound => _translationService._("No video found"),
        BulkItemState.NeedsAction => _translationService._("Needs my action"),
        BulkItemState.Failed => _translationService._("Failed"),
        BulkItemState.Expanded => _translationService._("Expanded"),
        BulkItemState.Stopped => _translationService._("Stopped"),
        _ => state.ToString()
    };

    public async Task ClearFinishedAsync()
    {
        foreach (var job in await GetJobsAsync())
        {
            var items = await GetItemsAsync(job.Id);
            if (items.All(x => x.IsFinal))
            {
                await _dbLock.WaitAsync();
                try
                {
                    await _databaseService.DeleteFromTableAsync(ItemsTable, "job", job.Id);
                    await _databaseService.DeleteFromTableAsync(JobsTable, "id", job.Id);
                }
                finally
                {
                    _dbLock.Release();
                }
            }
        }
    }

    // ---------------- Storage ----------------

    public async Task<IReadOnlyList<BulkJob>> GetJobsAsync()
    {
        await EnsureTablesAsync();
        var jobs = new List<BulkJob>();
        using var command = await _databaseService.SelectAllFromTableAsync(JobsTable);
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            jobs.Add(new BulkJob { Id = reader.GetString(0), Created = DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture), SaveFolder = reader.GetString(2), Name = reader.GetString(3) });
        }
        jobs.Sort((a, b) => b.Created.CompareTo(a.Created));
        return jobs;
    }

    public async Task<IReadOnlyList<BulkItem>> GetItemsAsync(string jobId)
    {
        await EnsureTablesAsync();
        var items = new List<BulkItem>();
        using var command = await _databaseService.SelectFromTableAsync(ItemsTable, "job", jobId);
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(ReadItem(reader));
        }
        return items;
    }

    private async Task<BulkItem?> GetItemAsync(string id)
    {
        await EnsureTablesAsync();
        using var command = await _databaseService.SelectFromTableAsync(ItemsTable, "id", id);
        using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadItem(reader) : null;
    }

    private async Task<BulkItem?> GetNextWaitingAsync()
    {
        await EnsureTablesAsync();
        var now = DateTime.Now;
        foreach (var state in new[] { BulkItemState.Waiting, BulkItemState.FailedWillRetry })
        {
            using var command = await _databaseService.SelectFromTableAsync(ItemsTable, "state", (int)state);
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var item = ReadItem(reader);
                if (item.RetryAfter <= now)
                {
                    return item;
                }
            }
        }
        return null;
    }

    private async Task<bool> IsVideoKeyActiveElsewhereAsync(BulkItem item, string videoKey)
    {
        using var command = await _databaseService.SelectFromTableAsync(ItemsTable, "videoKey", videoKey);
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var other = ReadItem(reader);
            if (other.Id != item.Id && other.State is BulkItemState.InQueue or BulkItemState.Downloaded)
            {
                return true;
            }
        }
        return false;
    }

    private async Task<HashSet<string>> GetUnfinishedNormalizedUrlsAsync()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        using var command = await _databaseService.SelectAllFromTableAsync(ItemsTable);
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var item = ReadItem(reader);
            if (item.State is BulkItemState.Waiting or BulkItemState.InQueue or BulkItemState.FailedWillRetry)
            {
                result.Add(item.NormalizedUrl);
            }
        }
        return result;
    }

    private static BulkItem ReadItem(System.Data.Common.DbDataReader reader) => new BulkItem
    {
        Id = reader.GetString(0),
        JobId = reader.GetString(1),
        Url = reader.GetString(2),
        NormalizedUrl = reader.GetString(3),
        Site = reader.GetString(4),
        Title = reader.GetString(5),
        State = (BulkItemState)reader.GetInt32(6),
        Attempts = reader.GetInt32(7),
        Note = reader.GetString(8),
        SaveFolder = reader.GetString(9),
        SaveFilename = reader.GetString(10),
        RetryAfter = DateTime.TryParse(reader.GetString(11), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var retry) ? retry : DateTime.MinValue,
        VideoKey = reader.GetString(12),
        IsCollection = reader.GetInt32(13) != 0
    };

    private static Dictionary<string, object> ToRow(BulkItem item) => new Dictionary<string, object>
    {
        { "id", item.Id },
        { "job", item.JobId },
        { "url", item.Url },
        { "norm", item.NormalizedUrl },
        { "site", item.Site },
        { "title", item.Title },
        { "state", (int)item.State },
        { "attempts", item.Attempts },
        { "note", item.Note },
        { "folder", item.SaveFolder },
        { "filename", item.SaveFilename },
        { "retryAfter", item.RetryAfter == DateTime.MinValue ? string.Empty : item.RetryAfter.ToString("o", CultureInfo.InvariantCulture) },
        { "videoKey", item.VideoKey },
        { "collection", item.IsCollection ? 1 : 0 }
    };

    private async Task InsertItemAsync(BulkItem item) => await _databaseService.ReplaceIntoTableAsync(ItemsTable, ToRow(item));

    private async Task SaveItemAsync(BulkItem item)
    {
        await _dbLock.WaitAsync();
        try
        {
            await _databaseService.ReplaceIntoTableAsync(ItemsTable, ToRow(item));
        }
        finally
        {
            _dbLock.Release();
        }
    }

    private async Task SetStateAsync(BulkItem item, BulkItemState state, string note, bool raiseFinished = true)
    {
        item.State = state;
        item.Note = note;
        await SaveItemAsync(item);
        JobChanged?.Invoke(this, item.JobId);
        if (raiseFinished && item.IsFinal)
        {
            var items = await GetItemsAsync(item.JobId);
            if (items.All(x => x.IsFinal))
            {
                _logger.LogInformation($"Bulk import {item.JobId} finished.");
                JobFinished?.Invoke(this, item.JobId);
                var done = items.Count(x => x.State == BulkItemState.Downloaded);
                var problems = items.Count(x => x.State is BulkItemState.Failed or BulkItemState.NeedsAction or BulkItemState.Unavailable or BulkItemState.NoVideoFound);
                _notificationService.Send(new AppNotification(_translationService._("Bulk Import finished: {0} downloaded, {1} need a look. Open Bulk Import for the report.", done, problems), problems > 0 ? NotificationSeverity.Warning : NotificationSeverity.Success)
                {
                    Action = "bulk-report",
                    ActionParam = item.JobId
                });
            }
        }
    }

    private async Task EnsureTablesAsync()
    {
        if (_tablesEnsured)
        {
            return;
        }
        await _databaseService.EnsureTableExistsAsync(JobsTable, "id TEXT PRIMARY KEY, created TEXT, folder TEXT, name TEXT");
        await _databaseService.EnsureTableExistsAsync(ItemsTable, "id TEXT PRIMARY KEY, job TEXT, url TEXT, norm TEXT, site TEXT, title TEXT, state INTEGER, attempts INTEGER, note TEXT, folder TEXT, filename TEXT, retryAfter TEXT, videoKey TEXT, collection INTEGER");
        _tablesEnsured = true;
    }
}
