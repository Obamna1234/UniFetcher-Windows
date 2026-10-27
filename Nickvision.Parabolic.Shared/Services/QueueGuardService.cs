using Microsoft.Extensions.Logging;
using Nickvision.Desktop.Application;
using Nickvision.Desktop.Globalization;
using Nickvision.Desktop.Notifications;
using Nickvision.Parabolic.Shared.Helpers;
using Nickvision.Parabolic.Shared.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Nickvision.Parabolic.Shared.Services;

public enum QueuePauseReason
{
    None,
    NoInternet,
    DriveMissing,
    DriveFull,
    /// <summary>The temporary files folder you chose isn't available (drive unplugged).</summary>
    TempMissing
}

/// <summary>Why a download failed, as far as the queue is concerned.</summary>
public enum FailureKind
{
    /// <summary>A normal failure of this download (site error, removed video...).</summary>
    Other,
    /// <summary>The internet is down (e.g. VPN reconnecting / kill switch). The download goes back in the queue.</summary>
    NoInternet,
    /// <summary>The download drive was unplugged or is asleep. The download goes back in the queue.</summary>
    DriveMissing,
    /// <summary>The download drive is full. The download goes back in the queue.</summary>
    DriveFull
}

/// <summary>
/// Pauses the download queue instead of failing downloads when the internet drops (spec section 4)
/// or the download drive is unplugged, renamed to another letter, or full (spec section 6a), and resumes on its own.
/// </summary>
public interface IQueueGuardService
{
    event EventHandler? Resumed;

    QueuePauseReason PauseReason { get; }

    bool IsPaused { get; }

    /// <summary>Remembers which drive a download is going to (by volume serial number, not letter).</summary>
    void StampDrive(DownloadOptions options);

    /// <summary>Checks a download can start now. May fix its folder if the drive came back with a different letter. False pauses the queue.</summary>
    bool CanStart(DownloadOptions options);

    /// <summary>Decides whether a failed download failed because of the connection or the drive. If so, the queue pauses.</summary>
    Task<FailureKind> ClassifyFailureAsync(DownloadOptions options, string log);

    /// <summary>True if the folder is on a FAT32 drive (no files over 4 GB).</summary>
    bool IsFat32(string folder);

    /// <summary>Free bytes on the folder's drive, or null if unknown.</summary>
    long? FreeSpace(string folder);
}

public partial class QueueGuardService : IQueueGuardService, IDisposable
{
    // UniFetcher: the free-space limit is a setting (Settings → Downloads); 0 means only pause when the drive is actually full
    private long MinimumFreeBytes => Math.Max(0, _configurationService.FreeSpaceLimitMb) * 1024L * 1024L;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(10);

    private readonly ILogger<QueueGuardService> _logger;
    private readonly IConfigurationService _configurationService;
    private readonly INotificationService _notificationService;
    private readonly ITranslationService _translationService;
    private readonly object _sync;
    private Timer? _monitor;
    private DownloadOptions? _blockedOptions;
    private string _probeHost;

    public event EventHandler? Resumed;

    public QueuePauseReason PauseReason { get; private set; }

    public bool IsPaused => PauseReason != QueuePauseReason.None;

    public QueueGuardService(ILogger<QueueGuardService> logger, IConfigurationService configurationService, INotificationService notificationService, ITranslationService translationService)
    {
        _logger = logger;
        _configurationService = configurationService;
        _notificationService = notificationService;
        _translationService = translationService;
        _sync = new object();
        _probeHost = "github.com";
        PauseReason = QueuePauseReason.None;
        NetworkChange.NetworkAvailabilityChanged += NetworkChange_NetworkAvailabilityChanged;
    }

    public void Dispose()
    {
        NetworkChange.NetworkAvailabilityChanged -= NetworkChange_NetworkAvailabilityChanged;
        _monitor?.Dispose();
        GC.SuppressFinalize(this);
    }

    public void StampDrive(DownloadOptions options)
    {
        if (!string.IsNullOrEmpty(options.DriveId) || string.IsNullOrEmpty(options.SaveFolder))
        {
            return;
        }
        var root = Path.GetPathRoot(options.SaveFolder);
        if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
        {
            options.DriveId = GetVolumeSerial(root) ?? string.Empty;
        }
        WarnOnceIfFat32(options.SaveFolder);
    }

    public bool CanStart(DownloadOptions options)
    {
        if (PauseReason == QueuePauseReason.NoInternet)
        {
            return false;
        }
        // Find the drive: same letter with the same serial, or the drive with that serial under a new letter
        var root = Path.GetPathRoot(options.SaveFolder) ?? string.Empty;
        var rootOk = !string.IsNullOrEmpty(root) && Directory.Exists(root);
        if (!string.IsNullOrEmpty(options.DriveId) && (!rootOk || GetVolumeSerial(root) != options.DriveId))
        {
            var newRoot = FindDriveBySerial(options.DriveId);
            if (newRoot is null)
            {
                Pause(QueuePauseReason.DriveMissing, options);
                return false;
            }
            var newFolder = newRoot + options.SaveFolder[root.Length..];
            _logger.LogInformation($"Download drive moved from {root} to {newRoot}; using {newFolder}.");
            if (_configurationService.PreviousSaveFolder.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                _configurationService.PreviousSaveFolder = newRoot + _configurationService.PreviousSaveFolder[root.Length..];
            }
            options.SaveFolder = newFolder;
            root = newRoot;
        }
        else if (!rootOk)
        {
            Pause(QueuePauseReason.DriveMissing, options);
            return false;
        }
        var free = FreeSpace(root);
        // When the size is known (a chosen format), check that it fits as well
        var needed = MinimumFreeBytes + EstimatedBytes(options);
        if (free is not null && needed > 0 && free.Value < needed)
        {
            Pause(QueuePauseReason.DriveFull, options);
            return false;
        }
        // UniFetcher: the temporary files folder (if you chose one) must be there and have room too; never fall back to another drive
        if (!TempOk())
        {
            Pause(QueuePauseReason.TempMissing, options);
            return false;
        }
        if (TempFree() is long tempFree && tempFree < MinimumFreeBytes)
        {
            Pause(QueuePauseReason.DriveFull, options);
            return false;
        }
        if (PauseReason is QueuePauseReason.DriveMissing or QueuePauseReason.DriveFull or QueuePauseReason.TempMissing)
        {
            // This download's drive is fine, but another one isn't: keep the queue paused until the monitor says otherwise
            return false;
        }
        return true;
    }

    public async Task<FailureKind> ClassifyFailureAsync(DownloadOptions options, string log)
    {
        var root = Path.GetPathRoot(options.SaveFolder) ?? string.Empty;
        var driveGone = string.IsNullOrEmpty(root) || !Directory.Exists(root) || (!string.IsNullOrEmpty(options.DriveId) && GetVolumeSerial(root) != options.DriveId);
        if (driveGone)
        {
            Pause(QueuePauseReason.DriveMissing, options);
            return FailureKind.DriveMissing;
        }
        if (!TempOk())
        {
            Pause(QueuePauseReason.TempMissing, options);
            return FailureKind.DriveMissing;
        }
        if (LooksLikeDiskFull(log) || (FreeSpace(root) is long free && free < MinimumFreeBytes) || (TempFree() is long tempFree && tempFree < MinimumFreeBytes))
        {
            Pause(QueuePauseReason.DriveFull, options);
            return FailureKind.DriveFull;
        }
        if (LooksLikeNetworkError(log) && !await IsInternetUpAsync(options.Url.Host))
        {
            _probeHost = options.Url.Host;
            Pause(QueuePauseReason.NoInternet, options);
            return FailureKind.NoInternet;
        }
        return FailureKind.Other;
    }

    public bool IsFat32(string folder)
    {
        try
        {
            var root = Path.GetPathRoot(folder);
            if (string.IsNullOrEmpty(root))
            {
                return false;
            }
            var drive = new DriveInfo(root);
            return drive.IsReady && drive.DriveFormat.StartsWith("FAT", StringComparison.OrdinalIgnoreCase) && !drive.DriveFormat.Equals("exFAT", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public long? FreeSpace(string folder)
    {
        try
        {
            var root = Path.GetPathRoot(folder);
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }
            var drive = new DriveInfo(root);
            return drive.IsReady ? drive.AvailableFreeSpace : null;
        }
        catch
        {
            return null;
        }
    }

    private void Pause(QueuePauseReason reason, DownloadOptions options)
    {
        lock (_sync)
        {
            if (PauseReason == reason)
            {
                return;
            }
            PauseReason = reason;
            _blockedOptions = options;
            _monitor?.Dispose();
            _monitor = new Timer(_ => _ = MonitorAsync(), null, CheckInterval, CheckInterval);
        }
        var message = reason switch
        {
            QueuePauseReason.NoInternet => _translationService._("No internet connection. Downloads are paused and will carry on by themselves when the connection is back."),
            QueuePauseReason.DriveMissing => _translationService._("Download drive not connected ({0}). Downloads are paused and will carry on when the drive is plugged back in.", Path.GetPathRoot(options.SaveFolder) ?? options.SaveFolder),
            QueuePauseReason.TempMissing => _translationService._("The temporary files folder isn't available ({0}). Downloads are paused and will carry on when its drive is back. You can change it in Settings → Downloads.", _configurationService.TempFolder),
            _ => _translationService._("The download drive is full ({0}). Downloads are paused; free up some space and they will carry on.", Path.GetPathRoot(options.SaveFolder) ?? options.SaveFolder)
        };
        _logger.LogWarning($"Queue paused: {reason}");
        _notificationService.Send(new AppNotification(message, NotificationSeverity.Warning));
    }

    private async Task MonitorAsync()
    {
        DownloadOptions? blocked;
        QueuePauseReason reason;
        lock (_sync)
        {
            blocked = _blockedOptions;
            reason = PauseReason;
        }
        if (reason == QueuePauseReason.None || blocked is null)
        {
            return;
        }
        var ok = reason switch
        {
            QueuePauseReason.NoInternet => await IsInternetUpAsync(_probeHost),
            QueuePauseReason.DriveMissing => DriveIsBack(blocked),
            QueuePauseReason.DriveFull => FreeSpace(blocked.SaveFolder) is long free && free >= Math.Max(MinimumFreeBytes, 50L * 1024 * 1024) && (TempFree() is not long tempFree || tempFree >= MinimumFreeBytes),
            QueuePauseReason.TempMissing => TempOk(),
            _ => true
        };
        if (!ok)
        {
            return;
        }
        lock (_sync)
        {
            if (PauseReason != reason)
            {
                return;
            }
            PauseReason = QueuePauseReason.None;
            _blockedOptions = null;
            _monitor?.Dispose();
            _monitor = null;
        }
        _logger.LogInformation($"Queue resumed after {reason}.");
        _notificationService.Send(new AppNotification(reason == QueuePauseReason.NoInternet ? _translationService._("Connection is back. Downloads have resumed.") : _translationService._("Download drive is ready. Downloads have resumed."), NotificationSeverity.Success));
        Resumed?.Invoke(this, EventArgs.Empty);
    }

    private static long EstimatedBytes(DownloadOptions options)
    {
        long bytes = 0;
        if (options.VideoFormat is not null && options.VideoFormat.Bytes > 0)
        {
            bytes += (long)options.VideoFormat.Bytes;
        }
        if (options.AudioFormat is not null && options.AudioFormat.Bytes > 0)
        {
            bytes += (long)options.AudioFormat.Bytes;
        }
        return bytes;
    }

    private bool TempOk()
    {
        var temp = _configurationService.TempFolder;
        if (string.IsNullOrEmpty(temp))
        {
            return true;
        }
        var root = Path.GetPathRoot(temp);
        return !string.IsNullOrEmpty(root) && Directory.Exists(root);
    }

    private long? TempFree()
    {
        var temp = _configurationService.TempFolder;
        return string.IsNullOrEmpty(temp) ? null : FreeSpace(temp);
    }

    private bool DriveIsBack(DownloadOptions options)
    {
        if (!string.IsNullOrEmpty(options.DriveId))
        {
            return FindDriveBySerial(options.DriveId) is not null;
        }
        var root = Path.GetPathRoot(options.SaveFolder);
        return !string.IsNullOrEmpty(root) && Directory.Exists(root);
    }

    private async void NetworkChange_NetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (!e.IsAvailable && PauseReason == QueuePauseReason.None)
        {
            // Stop starting new downloads straight away; running ones requeue themselves if they fail
            if (!await IsInternetUpAsync(_probeHost))
            {
                Pause(QueuePauseReason.NoInternet, new DownloadOptions(new Uri("https://" + _probeHost)));
            }
        }
    }

    /// <summary>Tries a plain connection to the site being downloaded from (an allowed host) to tell "site problem" from "no internet".</summary>
    private static async Task<bool> IsInternetUpAsync(string host)
    {
        foreach (var candidate in new[] { host, "github.com" }.Distinct())
        {
            try
            {
                using var client = new TcpClient();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await client.ConnectAsync(candidate, 443, timeout.Token);
                return true;
            }
            catch
            {
                // try the next one
            }
        }
        return false;
    }

    private static bool LooksLikeNetworkError(string log) =>
        new[]
        {
            "Unable to download", "Connection reset", "ConnectionResetError", "Connection aborted", "Connection refused", "getaddrinfo failed", "Failed to resolve",
            "Temporary failure in name resolution", "Name or service not known", "timed out", "Network is unreachable", "No route to host", "RemoteDisconnected",
            "IncompleteRead", "TransportError", "SSLError", "WinError 10051", "WinError 10053", "WinError 10054", "WinError 10060", "WinError 10061", "WinError 10065", "WinError 11001",
            "Errno 11001", "Errno 101", "Errno 104", "Errno 110", "Errno 113"
        }.Any(x => log.Contains(x, StringComparison.OrdinalIgnoreCase));

    private static bool LooksLikeDiskFull(string log) =>
        new[] { "No space left on device", "Errno 28", "WinError 112", "not enough space on the disk", "disk is full" }.Any(x => log.Contains(x, StringComparison.OrdinalIgnoreCase));

    private void WarnOnceIfFat32(string folder)
    {
        if (!IsFat32(folder))
        {
            return;
        }
        var root = Path.GetPathRoot(folder) ?? folder;
        var id = GetVolumeSerial(root) ?? root;
        var warned = _configurationService.Fat32WarnedDrives;
        if (warned.Split(';').Contains(id))
        {
            return;
        }
        _configurationService.Fat32WarnedDrives = string.IsNullOrEmpty(warned) ? id : $"{warned};{id}";
        _notificationService.Send(new AppNotification(_translationService._("The download drive ({0}) uses FAT32, which can't store files bigger than 4 GB. Videos over 4 GB will be skipped and listed as failed. Formatting the drive as exFAT or NTFS removes this limit (formatting erases the drive, so copy your files off first).", root), NotificationSeverity.Warning));
    }

    private static string? FindDriveBySerial(string serial)
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.IsReady && GetVolumeSerial(drive.RootDirectory.FullName) == serial)
                {
                    return drive.RootDirectory.FullName;
                }
            }
            catch
            {
                // drive went away while looking
            }
        }
        return null;
    }

    /// <summary>The volume serial number identifies a drive even when Windows gives it a different letter.</summary>
    public static string? GetVolumeSerial(string root)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }
        try
        {
            if (!root.EndsWith('\\'))
            {
                root += '\\';
            }
            return GetVolumeInformation(root, IntPtr.Zero, 0, out var serial, out _, out _, IntPtr.Zero, 0) ? serial.ToString("X8") : null;
        }
        catch
        {
            return null;
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumeInformation(string rootPathName, IntPtr volumeNameBuffer, uint volumeNameSize, out uint volumeSerialNumber, out uint maximumComponentLength, out uint fileSystemFlags, IntPtr fileSystemNameBuffer, uint fileSystemNameSize);
}
