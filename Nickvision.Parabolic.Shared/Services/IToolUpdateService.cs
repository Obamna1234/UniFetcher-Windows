using Nickvision.Desktop.Network;
using Nickvision.Parabolic.Shared.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nickvision.Parabolic.Shared.Services;

/// <summary>
/// Keeps yt-dlp and ffmpeg up to date (UniFetcher spec, section 2).
/// Downloads only from the official GitHub releases of yt-dlp and yt-dlp/FFmpeg-Builds.
/// </summary>
public interface IToolUpdateService
{
    /// <summary>Raised when a newer version is found while "Ask me first" is on, or when something was installed.</summary>
    event EventHandler<ToolUpdateResult>? UpdateFound;

    /// <summary>Path of the yt-dlp.exe that should be used now.</summary>
    string YtdlpPath { get; }

    /// <summary>Path of the ffmpeg.exe that should be used now (ffprobe.exe sits next to it).</summary>
    string FfmpegPath { get; }

    /// <summary>When the last update check finished (UTC), or null if never.</summary>
    DateTime? LastCheck { get; }

    /// <summary>True when an older yt-dlp or ffmpeg is kept that we can switch back to.</summary>
    bool CanRollBack { get; }

    Task<string> GetYtdlpVersionAsync();

    Task<string> GetFfmpegVersionAsync();

    /// <summary>Checks for updates. Respects the update mode unless <paramref name="install"/> is true (e.g. the user pressed "Update").</summary>
    Task<ToolUpdateResult> CheckAsync(bool install = false, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Checks only if the last check is older than 6 hours (called before fetching a link).</summary>
    Task EnsureRecentCheckAsync(CancellationToken cancellationToken = default);

    /// <summary>After an extractor / "unsupported" error: installs a newer yt-dlp if one exists and updates are automatic. True if a new version was installed.</summary>
    Task<bool> TryUpdateAfterExtractorErrorAsync(CancellationToken cancellationToken = default);

    /// <summary>Switches back to the previously used yt-dlp and ffmpeg.</summary>
    Task<bool> RollBackAsync();
}
