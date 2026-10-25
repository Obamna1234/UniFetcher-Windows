using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nickvision.Parabolic.Shared.Helpers;

/// <summary>UniFetcher never overwrites files (spec section 8): a name that's taken gets " (1)", " (2)", ... added.</summary>
public static class FileNameHelper
{
    /// <param name="folder">Folder the file will be saved in.</param>
    /// <param name="name">File name without extension (yt-dlp adds the extension).</param>
    /// <param name="reserved">Names already given to other downloads in the same batch (case-insensitive, full paths without extension). Updated.</param>
    public static string UniqueName(string folder, string name, ISet<string>? reserved = null)
    {
        var candidate = name;
        for (var i = 1; i < 10000; i++)
        {
            var full = Path.Combine(folder, candidate);
            if (!IsTaken(folder, candidate) && (reserved is null || !reserved.Contains(full)))
            {
                reserved?.Add(full);
                return candidate;
            }
            candidate = $"{name} ({i})";
        }
        return $"{name} ({Guid.NewGuid():N})";
    }

    /// <summary>"example.com" subfolder inside the download folder when "Subfolder per site" is on.</summary>
    public static string SiteFolder(string folder, Uri url, bool perSite)
    {
        if (!perSite)
        {
            return folder;
        }
        var site = Services.SiteSessionService.SiteOf(url);
        var safe = string.Concat(site.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return string.IsNullOrEmpty(safe) ? folder : Path.Combine(folder, safe);
    }

    private static bool IsTaken(string folder, string name)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                return false;
            }
            // Any file "<name>.<ext>" (including yt-dlp's .part / .ytdl working files) means the name is in use
            return Directory.EnumerateFiles(folder, name + ".*", SearchOption.TopDirectoryOnly)
                .Any(f => string.Equals(Path.GetFileNameWithoutExtension(f), name, StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileName(f).StartsWith(name + ".", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }
}
