using Microsoft.Extensions.Logging;
using Nickvision.Desktop.Application;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace Nickvision.Parabolic.Shared.Services;

/// <summary>A video UniFetcher has already downloaded successfully.</summary>
public record DownloadedRecord(string Title, string Path, DateTime DownloadedOn);

/// <summary>
/// Remembers successfully downloaded videos by normalised link and by video ID (site + ID), for duplicate detection.
/// Lives in UniFetcher's database (not in the files), so it still works while the download drive is unplugged.
/// Separate from the visible download history, so clearing or shortening history doesn't forget what was downloaded.
/// </summary>
public interface IDownloadRegistryService
{
    Task<DownloadedRecord?> FindAsync(Uri url, string? videoKey);
    Task MarkDownloadedAsync(Uri url, string? videoKey, string title, string path);
    Task<bool> ForgetAsync(Uri url, string? videoKey);
    Task<bool> ClearAsync();
}

public class DownloadRegistryService : IDownloadRegistryService
{
    private const string TableName = "unifetcher_downloaded";
    private static readonly HashSet<string> TrackingParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content", "utm_id", "fbclid", "gclid", "igshid", "mc_cid", "mc_eid",
        "si", "feature", "pp", "ref_src", "ref_url", "t", "start", "time_continue", "ab_channel"
    };

    private readonly ILogger<DownloadRegistryService> _logger;
    private readonly IDatabaseService _databaseService;
    private bool _tableEnsured;

    public DownloadRegistryService(ILogger<DownloadRegistryService> logger, IDatabaseService databaseService)
    {
        _logger = logger;
        _databaseService = databaseService;
    }

    public async Task<DownloadedRecord?> FindAsync(Uri url, string? videoKey)
    {
        await EnsureTableAsync();
        foreach (var key in Keys(url, videoKey))
        {
            using var command = await _databaseService.SelectFromTableAsync(TableName, "key", key);
            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new DownloadedRecord(reader.GetString(1), reader.GetString(2), DateTime.Parse(reader.GetString(3)));
            }
        }
        return null;
    }

    public async Task MarkDownloadedAsync(Uri url, string? videoKey, string title, string path)
    {
        await EnsureTableAsync();
        var now = DateTime.Now.ToString("o");
        foreach (var key in Keys(url, videoKey))
        {
            await _databaseService.ReplaceIntoTableAsync(TableName, new Dictionary<string, object>
            {
                { "key", key },
                { "title", title },
                { "path", path },
                { "downloadedOn", now }
            });
        }
        _logger.LogDebug($"Remembered download: {NormalizeUrl(url)} {videoKey}");
    }

    public async Task<bool> ForgetAsync(Uri url, string? videoKey)
    {
        await EnsureTableAsync();
        var res = true;
        foreach (var key in Keys(url, videoKey))
        {
            res &= await _databaseService.DeleteFromTableAsync(TableName, "key", key);
        }
        return res;
    }

    public async Task<bool> ClearAsync()
    {
        await EnsureTableAsync();
        return await _databaseService.ClearTableAsync(TableName);
    }

    private static IEnumerable<string> Keys(Uri url, string? videoKey)
    {
        yield return "url:" + NormalizeUrl(url);
        if (!string.IsNullOrEmpty(videoKey))
        {
            yield return "id:" + videoKey.ToLowerInvariant();
        }
    }

    /// <summary>
    /// Makes different spellings of the same link compare equal: lower-case host without "www."/"m.", no #fragment,
    /// no tracking parameters (utm_*, si, fbclid...), sorted query, and YouTube short links turned into watch?v= links.
    /// </summary>
    public static string NormalizeUrl(Uri url)
    {
        if (!url.IsAbsoluteUri)
        {
            return url.ToString();
        }
        var host = url.Host.ToLowerInvariant();
        foreach (var prefix in new[] { "www.", "m.", "mobile." })
        {
            if (host.StartsWith(prefix, StringComparison.Ordinal))
            {
                host = host[prefix.Length..];
                break;
            }
        }
        var path = url.AbsolutePath.TrimEnd('/');
        var query = HttpUtility.ParseQueryString(url.Query);
        // YouTube: youtu.be/ID, /shorts/ID, /live/ID, /embed/ID -> youtube.com/watch?v=ID
        if (host is "youtu.be" or "youtube-nocookie.com" || (host == "youtube.com" && (path.StartsWith("/shorts/", StringComparison.Ordinal) || path.StartsWith("/live/", StringComparison.Ordinal) || path.StartsWith("/embed/", StringComparison.Ordinal))))
        {
            var id = path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            if (!string.IsNullOrEmpty(id))
            {
                host = "youtube.com";
                path = "/watch";
                query.Set("v", id);
            }
        }
        if (host == "music.youtube.com")
        {
            host = "youtube.com";
        }
        var parts = new List<string>();
        foreach (var key in query.AllKeys)
        {
            if (key is null || TrackingParameters.Contains(key) || key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            // Playlist position/context parameters don't change which video it is
            if (host == "youtube.com" && path == "/watch" && key is "list" or "index")
            {
                continue;
            }
            parts.Add($"{key.ToLowerInvariant()}={query[key]}");
        }
        parts.Sort(StringComparer.Ordinal);
        var builder = new StringBuilder();
        builder.Append(host).Append(path);
        if (parts.Count > 0)
        {
            builder.Append('?').Append(string.Join('&', parts));
        }
        return builder.ToString();
    }

    private async Task EnsureTableAsync()
    {
        if (_tableEnsured)
        {
            return;
        }
        await _databaseService.EnsureTableExistsAsync(TableName, "key TEXT PRIMARY KEY, title TEXT, path TEXT, downloadedOn TEXT");
        _tableEnsured = true;
    }
}
