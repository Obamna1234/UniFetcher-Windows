using Nickvision.Desktop.Globalization;
using Nickvision.Parabolic.Shared.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Nickvision.Parabolic.Shared.Controllers;

public enum LibrarySort
{
    Newest,
    Oldest,
    Name,
    Size,
    Duration,
    Site
}

public class LibraryEntry
{
    public string Title { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public Uri Url { get; init; } = new Uri("https://example.invalid/");
    public string Site { get; init; } = string.Empty;
    public DateTime DownloadedOn { get; init; }
    public long Size { get; init; }
    public TimeSpan Duration { get; set; }
    public bool Exists { get; init; }
}

/// <summary>UniFetcher downloads library (spec section 6): search, sort, open, show in folder, copy link, remove.</summary>
public class LibraryController
{
    private readonly IHistoryService _historyService;
    private readonly IDownloadRegistryService _registry;
    private readonly IEventsService _eventsService;
    private readonly ITranslationService _translationService;

    public LibraryController(IHistoryService historyService, IDownloadRegistryService registry, IEventsService eventsService, ITranslationService translationService)
    {
        _historyService = historyService;
        _registry = registry;
        _eventsService = eventsService;
        _translationService = translationService;
    }

    public async Task<List<LibraryEntry>> GetAsync()
    {
        var list = new List<LibraryEntry>();
        foreach (var item in await _historyService.GetAllAsync())
        {
            var exists = !string.IsNullOrEmpty(item.Path) && File.Exists(item.Path);
            list.Add(new LibraryEntry
            {
                Title = string.IsNullOrEmpty(item.Title) ? System.IO.Path.GetFileNameWithoutExtension(item.Path) : item.Title,
                Path = item.Path,
                Url = item.Url,
                Site = SiteSessionService.SiteOf(item.Url),
                DownloadedOn = item.DownloadedOn,
                Size = exists ? new FileInfo(item.Path).Length : 0,
                Exists = exists
            });
        }
        // Reading durations opens each file, so do it in the background
        await Task.Run(() =>
        {
            foreach (var entry in list.Where(x => x.Exists))
            {
                try
                {
                    entry.Duration = TimeSpan.FromSeconds(new ATL.Track(entry.Path).Duration);
                }
                catch
                {
                    // not a media file ATL understands
                }
            }
        });
        return list;
    }

    public static IEnumerable<LibraryEntry> Filter(IEnumerable<LibraryEntry> entries, string search, LibrarySort sort)
    {
        var query = entries;
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(x => x.Title.Contains(search, StringComparison.CurrentCultureIgnoreCase) || x.Site.Contains(search, StringComparison.OrdinalIgnoreCase) || x.Url.ToString().Contains(search, StringComparison.OrdinalIgnoreCase));
        }
        return sort switch
        {
            LibrarySort.Oldest => query.OrderBy(x => x.DownloadedOn),
            LibrarySort.Name => query.OrderBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase),
            LibrarySort.Size => query.OrderByDescending(x => x.Size),
            LibrarySort.Duration => query.OrderByDescending(x => x.Duration),
            LibrarySort.Site => query.OrderBy(x => x.Site).ThenByDescending(x => x.DownloadedOn),
            _ => query.OrderByDescending(x => x.DownloadedOn)
        };
    }

    public string SortLabel(LibrarySort sort) => sort switch
    {
        LibrarySort.Newest => _translationService._("Newest first"),
        LibrarySort.Oldest => _translationService._("Oldest first"),
        LibrarySort.Name => _translationService._("Name"),
        LibrarySort.Size => _translationService._("Size"),
        LibrarySort.Duration => _translationService._("Duration"),
        _ => _translationService._("Site")
    };

    public string Describe(LibraryEntry entry)
    {
        var parts = new List<string> { entry.Site, entry.DownloadedOn.ToString("g") };
        if (entry.Size > 0)
        {
            parts.Add(entry.Size >= 1073741824 ? $"{entry.Size / 1073741824.0:0.0} GB" : $"{entry.Size / 1048576.0:0} MB");
        }
        if (entry.Duration > TimeSpan.Zero)
        {
            parts.Add(entry.Duration.TotalHours >= 1 ? entry.Duration.ToString(@"h\:mm\:ss") : entry.Duration.ToString(@"m\:ss"));
        }
        if (!entry.Exists)
        {
            parts.Add(_translationService._("file not found (moved, deleted, or drive unplugged)"));
        }
        return string.Join("  ·  ", parts);
    }

    /// <summary>Removes the entry from the history. The file is deleted only when you ticked "Also delete file".</summary>
    public async Task RemoveAsync(LibraryEntry entry, bool alsoDeleteFile)
    {
        await _historyService.RemoveAsync(entry.Url);
        if (alsoDeleteFile && entry.Exists)
        {
            File.Delete(entry.Path);
            // Let it be downloaded again later without a duplicate warning
            await _registry.ForgetAsync(entry.Url, null);
        }
    }

    public void DownloadAgain(Uri url) => _eventsService.InvokeDownloadRequested(url);
}
