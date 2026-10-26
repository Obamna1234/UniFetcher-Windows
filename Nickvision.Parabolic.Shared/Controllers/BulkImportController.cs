using Nickvision.Desktop.Application;
using Nickvision.Desktop.Globalization;
using Nickvision.Parabolic.Shared.Helpers;
using Nickvision.Parabolic.Shared.Models;
using Nickvision.Parabolic.Shared.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Nickvision.Parabolic.Shared.Controllers;

/// <summary>Bulk Import screen logic: read a list, show the summary, start, follow progress, report.</summary>
public class BulkImportController
{
    private readonly IConfigurationService _configurationService;
    private readonly ITranslationService _translationService;
    private readonly IBulkImportService _bulkImportService;
    private readonly IQueueGuardService _queueGuard;

    public event EventHandler<string>? JobChanged;

    public BulkImportController(IConfigurationService configurationService, ITranslationService translationService, IBulkImportService bulkImportService, IQueueGuardService queueGuard)
    {
        _configurationService = configurationService;
        _translationService = translationService;
        _bulkImportService = bulkImportService;
        _queueGuard = queueGuard;
        _bulkImportService.JobChanged += (_, id) => JobChanged?.Invoke(this, id);
        _bulkImportService.JobFinished += (_, id) => JobChanged?.Invoke(this, id);
    }

    public string SaveFolder
    {
        get => _configurationService.PreviousSaveFolder;

        set => _configurationService.PreviousSaveFolder = value;
    }

    public async Task<BulkPlan> PlanFromTextAsync(string text, string? fileName = null) => await _bulkImportService.PlanAsync(LinkImportParser.Parse(text, fileName));

    /// <summary>Reads links from any supported file: text lists and exports, or documents (PDF, Word, Samsung Notes, e-mail...).</summary>
    public async Task<BulkPlan> PlanFromFileAsync(string path)
    {
        var (text, format, bestEffort) = await DocumentTextExtractor.ExtractAsync(path);
        if (string.IsNullOrEmpty(format))
        {
            // A text list or export: let the parser recognise bookmarks HTML, JSON, CSV, OneTab...
            return await PlanFromTextAsync(text, path);
        }
        var parsed = LinkImportParser.Parse(text);
        // Text around links in a document isn't a title; the real title comes from the site later
        var links = parsed.Links.Select(x => new ImportedLink(x.Url, string.Empty)).ToList();
        var label = bestEffort ? _translationService._("{0} (best-effort scan)", _translationService._(format)) : _translationService._(format);
        return await _bulkImportService.PlanAsync(new ParsedLinks(label, links, parsed.NotWebPages));
    }

    public async Task<BulkPlan> PlanFromLinksAsync(IReadOnlyList<Uri> urls) => await _bulkImportService.PlanAsync(new ParsedLinks(_translationService._("Browser tabs"), urls.Select(x => new ImportedLink(x, string.Empty)).ToList(), []));

    public string Summarize(BulkPlan plan)
    {
        var lines = new List<string>
        {
            _translationService._("Format: {0}", plan.Format),
            _translationService._("Total links: {0}", plan.Total),
            _translationService._("New: {0}", plan.Entries.Count(x => x.Status == PlanStatus.New && !x.IsCollection)),
            _translationService._("Duplicates inside the list (skipped): {0}", plan.Entries.Count(x => x.Status == PlanStatus.DuplicateInList)),
            _translationService._("Already downloaded (skipped): {0}", plan.Entries.Count(x => x.Status == PlanStatus.AlreadyDownloaded)),
            _translationService._("Already in an earlier import (continues there): {0}", plan.Entries.Count(x => x.Status == PlanStatus.AlreadyImporting)),
            _translationService._("Playlists, channels and profiles (tick to expand): {0}", plan.Entries.Count(x => x.Status == PlanStatus.New && x.IsCollection)),
            _translationService._("Not web pages (ignored): {0}", plan.NotWebPages.Count)
        };
        var free = _queueGuard.FreeSpace(SaveFolder);
        var count = plan.Entries.Count(x => x.Selected);
        if (free is not null && count > 0)
        {
            // ~250 MB per video is a rough average for videos up to 1080p
            var estimate = count * 250L * 1024 * 1024;
            lines.Add(_translationService._("Free space on the download drive: {0:0.0} GB", free.Value / 1073741824.0));
            if (estimate > free.Value)
            {
                lines.Add(_translationService._("Warning: {0} videos could need around {1:0.0} GB. UniFetcher pauses if the drive fills up.", count, estimate / 1073741824.0));
            }
        }
        if (_queueGuard.IsFat32(SaveFolder))
        {
            lines.Add(_translationService._("Note: the download drive is FAT32, so videos over 4 GB will be skipped."));
        }
        return string.Join(Environment.NewLine, lines);
    }

    public string StatusLabel(PlanEntry entry) => entry.Status switch
    {
        PlanStatus.DuplicateInList => _translationService._("Duplicate in list"),
        PlanStatus.AlreadyDownloaded => _translationService._("Already downloaded"),
        PlanStatus.AlreadyImporting => _translationService._("In an earlier import"),
        _ => entry.IsCollection ? _translationService._("Playlist / channel — tick to expand") : string.Empty
    };

    public async Task<string> StartAsync(BulkPlan plan) => await _bulkImportService.StartAsync(plan, SaveFolder);

    public async Task<IReadOnlyList<BulkJob>> GetJobsAsync() => await _bulkImportService.GetJobsAsync();

    public async Task<IReadOnlyList<BulkItem>> GetItemsAsync(string jobId) => await _bulkImportService.GetItemsAsync(jobId);

    public string StateLabel(BulkItemState state) => _bulkImportService.StateLabel(state);

    public async Task<string> GetCountsAsync(string jobId)
    {
        var items = (await GetItemsAsync(jobId)).Where(x => x.State != BulkItemState.Expanded).ToList();
        var parts = Enum.GetValues<BulkItemState>().Where(s => s != BulkItemState.Expanded).Select(s => (s, items.Count(x => x.State == s))).Where(x => x.Item2 > 0).Select(x => $"{StateLabel(x.s)}: {x.Item2}");
        return string.Join("   ·   ", parts);
    }

    public async Task RetryFailedAsync(string jobId) => await _bulkImportService.RetryFailedAsync(jobId);

    public async Task<string> GetReportAsync(string jobId) => await _bulkImportService.GetReportAsync(jobId);

    public async Task<string> GetFailedLinksAsync(string jobId) => await _bulkImportService.GetFailedLinksAsync(jobId);
}
