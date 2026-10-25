using System;
using System.Collections.Generic;

namespace Nickvision.Parabolic.Shared.Models;

/// <summary>Where each Bulk Import link is (spec: Downloaded, Skipped (duplicate), Failed – will retry, Unavailable, No video found, Needs my action).</summary>
public enum BulkItemState
{
    Waiting = 0,
    InQueue = 1,
    Downloaded = 2,
    SkippedDuplicate = 3,
    FailedWillRetry = 4,
    Unavailable = 5,
    NoVideoFound = 6,
    NeedsAction = 7,
    /// <summary>Gave up after the automatic retries (press "Retry failed" to try again).</summary>
    Failed = 8,
    /// <summary>A playlist/channel/profile that was expanded into its videos.</summary>
    Expanded = 9,
    /// <summary>Stopped by you.</summary>
    Stopped = 10
}

public class BulkItem
{
    public string Id { get; init; } = string.Empty;
    public string JobId { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string NormalizedUrl { get; init; } = string.Empty;
    public string Site { get; init; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public BulkItemState State { get; set; }
    public int Attempts { get; set; }
    public string Note { get; set; } = string.Empty;
    public string SaveFolder { get; set; } = string.Empty;
    public string SaveFilename { get; set; } = string.Empty;
    public DateTime RetryAfter { get; set; } = DateTime.MinValue;
    public string VideoKey { get; set; } = string.Empty;
    public bool IsCollection { get; set; }

    public bool IsFinal => State is BulkItemState.Downloaded or BulkItemState.SkippedDuplicate or BulkItemState.Unavailable or BulkItemState.NoVideoFound or BulkItemState.NeedsAction or BulkItemState.Failed or BulkItemState.Expanded or BulkItemState.Stopped;
}

public class BulkJob
{
    public string Id { get; init; } = string.Empty;
    public DateTime Created { get; init; }
    public string SaveFolder { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
}

/// <summary>What a link is, as seen on the summary screen before starting.</summary>
public enum PlanStatus
{
    New,
    DuplicateInList,
    AlreadyDownloaded,
    /// <summary>Already in an earlier, unfinished import: it continues there instead of being added twice.</summary>
    AlreadyImporting
}

public class PlanEntry
{
    public Uri Url { get; init; } = new Uri("https://example.invalid/");
    public string Title { get; init; } = string.Empty;
    public string Site { get; init; } = string.Empty;
    public string NormalizedUrl { get; init; } = string.Empty;
    public PlanStatus Status { get; init; }
    /// <summary>Playlist, channel or profile page: not expanded unless you choose to.</summary>
    public bool IsCollection { get; init; }
    public bool Selected { get; set; }
}

public class BulkPlan
{
    public string Format { get; init; } = string.Empty;
    public List<PlanEntry> Entries { get; } = new();
    public List<string> NotWebPages { get; } = new();
    public int Total { get; init; }
}
