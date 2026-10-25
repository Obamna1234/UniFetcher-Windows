namespace Nickvision.Parabolic.Shared.Models;

/// <summary>What UniFetcher does when a new yt-dlp / ffmpeg version is found.</summary>
public enum ToolUpdateMode
{
    Automatic = 0,
    AskFirst = 1,
    Off = 2
}

/// <summary>Which yt-dlp release channel to follow. Nightly gets site fixes fastest.</summary>
public enum YtdlpChannel
{
    Nightly = 0,
    Stable = 1,
    Master = 2
}

/// <summary>Outcome of an update check.</summary>
public enum ToolUpdateOutcome
{
    /// <summary>Nothing newer was found.</summary>
    UpToDate,
    /// <summary>A newer version was downloaded and is now in use.</summary>
    Installed,
    /// <summary>A newer version exists and the "Ask me first" setting is on.</summary>
    Available,
    /// <summary>Updates are switched off, or the check was skipped because it ran recently.</summary>
    Skipped,
    /// <summary>The check or download failed (for example, no internet).</summary>
    Failed
}

public record ToolUpdateResult(ToolUpdateOutcome Outcome, string? YtdlpVersion = null, bool FfmpegChanged = false, string? Error = null);
