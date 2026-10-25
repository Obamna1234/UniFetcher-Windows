using Nickvision.Parabolic.Shared.Models;
using System;

namespace Nickvision.Parabolic.Shared.Events;

/// <summary>UniFetcher: a download reached its final result (no further automatic retry is planned for it).</summary>
public class DownloadFinishedEventArgs : EventArgs
{
    public DownloadOptions Options { get; }
    public DownloadStatus Status { get; }
    public string Log { get; }
    public string FilePath { get; }

    public DownloadFinishedEventArgs(DownloadOptions options, DownloadStatus status, string log, string filePath)
    {
        Options = options;
        Status = status;
        Log = log;
        FilePath = filePath;
    }
}
