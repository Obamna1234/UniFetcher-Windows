using Nickvision.Parabolic.Shared.Models;
using System;

namespace Nickvision.Parabolic.Shared.Events;

public class DownloadCompletedEventArgs : DownloadEventArgs
{
    public DownloadStatus Status { get; }
    public string Path { get; }
    public ReadOnlyMemory<char> Log { get; }
    public bool ShowNotification { get; }
    /// <summary>UniFetcher: plain-language reason when the download failed.</summary>
    public FailureReason Reason { get; init; }
    /// <summary>UniFetcher: whether a browser session (cookies) was attached, and where it came from.</summary>
    public string CookieSource { get; init; } = string.Empty;
    /// <summary>UniFetcher: what UniFetcher tried for this download (session made, refreshed, yt-dlp updated...).</summary>
    public System.Collections.Generic.IReadOnlyList<string> Steps { get; init; } = [];
    public string Url { get; init; } = string.Empty;

    public DownloadCompletedEventArgs(int id, DownloadStatus status, string path, ReadOnlyMemory<char> log, bool showNotification) : base(id)
    {
        Status = status;
        Path = path;
        Log = log;
        ShowNotification = showNotification;
    }
}
