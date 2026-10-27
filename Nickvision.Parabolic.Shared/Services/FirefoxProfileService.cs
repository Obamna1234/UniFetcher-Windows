using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Nickvision.Desktop.Application;
using Nickvision.Desktop.Filesystem;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nickvision.Parabolic.Shared.Services;

/// <summary>
/// UniFetcher's own Firefox profile, called "UniFetcher" (spec section 3a).
/// Sessions (cookies) for sites that need them come only from this profile. Your everyday Firefox / Brave profile is never touched.
/// The profile lives in %APPDATA%\UniFetcher\FirefoxProfile and is registered in Firefox's profile list so you can also open it from about:profiles.
/// </summary>
public interface IFirefoxProfileService
{
    /// <summary>Full path of firefox.exe, or null if Firefox isn't installed.</summary>
    string? FirefoxPath { get; }

    bool IsFirefoxInstalled { get; }

    string ProfileDirectory { get; }

    /// <summary>True while a Firefox window using the UniFetcher profile is open.</summary>
    bool IsProfileRunning { get; }

    /// <summary>Creates the profile on first use (with privacy settings and no first-run pages). False if Firefox isn't installed.</summary>
    Task<bool> EnsureProfileAsync();

    /// <summary>Opens a page in the UniFetcher profile (never in your main browser).</summary>
    Task<bool> OpenAsync(Uri url);

    /// <summary>Opens a UniFetcher profile window with a blank page, e.g. to log in to a site.</summary>
    Task<bool> OpenWindowAsync();

    /// <summary>
    /// Makes a private, up-to-date copy of the profile's cookies that yt-dlp can read with --cookies-from-browser firefox:&lt;dir&gt;,
    /// without closing Firefox. Returns the folder, or null if there are no cookies yet.
    /// </summary>
    Task<string?> GetCookieSnapshotAsync();

    /// <summary>Removes one site's cookies from the UniFetcher profile (closes the UniFetcher Firefox window first if needed).</summary>
    Task<bool> ForgetSiteAsync(string site);

    /// <summary>Wipes the UniFetcher profile's history, cookies and cache (closes the UniFetcher Firefox window first).</summary>
    Task<bool> ClearDataAsync();

    /// <summary>Closes only Firefox windows that use the UniFetcher profile.</summary>
    Task CloseProfileWindowsAsync();

    /// <summary>Forces a fresh cookie copy next time (after a site visit).</summary>
    void InvalidateSnapshot();

    /// <summary>Deletes UniFetcher's private copy of the profile's cookies.</summary>
    void ClearCookieSnapshot();

    /// <summary>Adds the signed Send to UniFetcher extension (shipped with UniFetcher) to the UniFetcher profile, if present.</summary>
    Task InstallExtensionAsync();
}

public class FirefoxProfileService : IFirefoxProfileService
{
    public const string ProfileName = "UniFetcher";

    private readonly ILogger<FirefoxProfileService> _logger;
    private readonly string _snapshotRoot;
    private readonly SemaphoreSlim _snapshotLock;
    private readonly List<Process> _startedProcesses = new List<Process>();
    private DateTime _snapshotTime;
    private string? _snapshotDir;

    public string ProfileDirectory { get; }

    private const string UserJs = """
            // UniFetcher's own Firefox profile. Your everyday Firefox profile is separate and untouched.
            user_pref("browser.shell.checkDefaultBrowser", false);
            user_pref("browser.aboutwelcome.enabled", false);
            user_pref("browser.startup.homepage_override.mstone", "ignore");
            user_pref("startup.homepage_welcome_url", "");
            user_pref("startup.homepage_welcome_url.additional", "");
            user_pref("browser.startup.page", 0);
            user_pref("browser.newtabpage.enabled", false);
            user_pref("datareporting.policy.dataSubmissionEnabled", false);
            user_pref("datareporting.healthreport.uploadEnabled", false);
            user_pref("toolkit.telemetry.enabled", false);
            user_pref("toolkit.telemetry.unified", false);
            user_pref("app.shield.optoutstudies.enabled", false);
            user_pref("browser.discovery.enabled", false);
            user_pref("browser.crashReports.unsubmittedCheck.autoSubmit2", false);
            user_pref("signon.rememberSignons", false);
            user_pref("network.cookie.lifetimePolicy", 0);
            user_pref("privacy.sanitize.sanitizeOnShutdown", false);
            user_pref("privacy.clearOnShutdown.cookies", false);
            user_pref("privacy.clearOnShutdown_v2.cookiesAndStorage", false);
            // Let the Send to UniFetcher extension that UniFetcher puts in this profile run without a prompt
            user_pref("extensions.autoDisableScopes", 0);
            """;

    public FirefoxProfileService(ILogger<FirefoxProfileService> logger, AppInfo appInfo)
    {
        _logger = logger;
        var root = appInfo.IsPortable ? Desktop.System.Environment.ExecutingDirectory : Path.Combine(UserDirectories.Config, appInfo.Name);
        ProfileDirectory = Path.Combine(root, "FirefoxProfile");
        _snapshotRoot = Path.Combine(appInfo.IsPortable ? Desktop.System.Environment.ExecutingDirectory : Path.Combine(UserDirectories.LocalData, appInfo.Name), "cookie-snapshot");
        _snapshotLock = new SemaphoreSlim(1, 1);
        _snapshotTime = DateTime.MinValue;
    }

    public string? FirefoxPath
    {
        get
        {
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }
            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                try
                {
                    using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\firefox.exe");
                    if (key?.GetValue(null) is string path && File.Exists(path.Trim('"')))
                    {
                        return path.Trim('"');
                    }
                }
                catch
                {
                    // look elsewhere
                }
            }
            foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
            {
                var path = Path.Combine(Environment.GetFolderPath(folder), "Mozilla Firefox", "firefox.exe");
                if (File.Exists(path))
                {
                    return path;
                }
            }
            return null;
        }
    }

    public bool IsFirefoxInstalled => FirefoxPath is not null;

    public bool IsProfileRunning => File.Exists(Path.Combine(ProfileDirectory, "parent.lock")) && IsLocked(Path.Combine(ProfileDirectory, "parent.lock"));

    public async Task<bool> EnsureProfileAsync()
    {
        var firefox = FirefoxPath;
        if (firefox is null)
        {
            return false;
        }
        var isNew = !File.Exists(Path.Combine(ProfileDirectory, "user.js"));
        Directory.CreateDirectory(ProfileDirectory);
        if (!isNew)
        {
            await File.WriteAllTextAsync(Path.Combine(ProfileDirectory, "user.js"), UserJs);
            return true;
        }
        // Register "UniFetcher" in Firefox's profile list (about:profiles) without touching the default profile
        try
        {
            using var process = Process.Start(new ProcessStartInfo(firefox, ["-CreateProfile", $"{ProfileName} {ProfileDirectory}"])
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is not null)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await process.WaitForExitAsync(timeout.Token);
            }
        }
        catch (Exception e)
        {
            _logger.LogWarning($"Could not register the UniFetcher Firefox profile (it still works by folder): {e.Message}");
        }
        // Profile settings: quiet first run, no telemetry, keep cookies on disk so they can be read
        await File.WriteAllTextAsync(Path.Combine(ProfileDirectory, "user.js"), UserJs);
        _logger.LogInformation($"Created the UniFetcher Firefox profile at {ProfileDirectory}.");
        return true;
    }

    public async Task<bool> OpenAsync(Uri url)
    {
        if (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp)
        {
            return false;
        }
        if (!await EnsureProfileAsync())
        {
            return false;
        }
        try
        {
            // -profile <folder> always targets the UniFetcher profile; Firefox hands the page to its window if it's already open
            var process = Process.Start(new ProcessStartInfo(FirefoxPath!, ["-profile", ProfileDirectory, "-new-tab", url.ToString()])
            {
                UseShellExecute = false
            });
            Remember(process);
            return process is not null;
        }
        catch (Exception e)
        {
            _logger.LogError($"Could not open the UniFetcher Firefox profile: {e.Message}");
            return false;
        }
    }

    public async Task InstallExtensionAsync()
    {
        var source = Path.Combine(Desktop.System.Environment.ExecutingDirectory, "extension", "unifetcher-firefox.xpi");
        if (!File.Exists(source) || !await EnsureProfileAsync())
        {
            return;
        }
        var target = Path.Combine(ProfileDirectory, "extensions", $"{InboxService.FirefoxExtensionId}.xpi");
        if (File.Exists(target) && new FileInfo(target).Length == new FileInfo(source).Length && File.GetLastWriteTimeUtc(target) >= File.GetLastWriteTimeUtc(source))
        {
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, true);
        _logger.LogInformation("Added the Send to UniFetcher extension to the UniFetcher Firefox profile.");
    }

    public async Task<bool> OpenWindowAsync()
    {
        if (!await EnsureProfileAsync())
        {
            return false;
        }
        try
        {
            var process = Process.Start(new ProcessStartInfo(FirefoxPath!, ["-profile", ProfileDirectory, "-new-window", "about:blank"])
            {
                UseShellExecute = false
            });
            Remember(process);
            return process is not null;
        }
        catch (Exception e)
        {
            _logger.LogError($"Could not open the UniFetcher Firefox profile: {e.Message}");
            return false;
        }
    }

    public async Task<string?> GetCookieSnapshotAsync()
    {
        var source = Path.Combine(ProfileDirectory, "cookies.sqlite");
        if (!File.Exists(source))
        {
            return null;
        }
        await _snapshotLock.WaitAsync();
        try
        {
            // Reuse a snapshot for a few seconds so a burst of downloads doesn't copy the database over and over
            if (_snapshotDir is not null && DateTime.UtcNow - _snapshotTime < TimeSpan.FromSeconds(20) && Directory.Exists(_snapshotDir))
            {
                return _snapshotDir;
            }
            var dir = Path.Combine(_snapshotRoot, "profile");
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
            Directory.CreateDirectory(dir);
            var target = Path.Combine(dir, "cookies.sqlite");
            try
            {
                CopyShared(source, target);
                if (File.Exists(source + "-wal"))
                {
                    CopyShared(source + "-wal", target + "-wal");
                }
            }
            catch (IOException)
            {
                // Firefox is holding the file: close the UniFetcher window (it's ours), then copy
                _logger.LogInformation("Cookie database busy; closing the UniFetcher Firefox window to read it.");
                await CloseProfileWindowsAsync();
                CopyShared(source, target);
                if (File.Exists(source + "-wal"))
                {
                    CopyShared(source + "-wal", target + "-wal");
                }
            }
            // Fold recent changes (the -wal file) into the copy, because yt-dlp only reads the main file
            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = target, Pooling = false }.ToString()))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE); PRAGMA journal_mode=DELETE;";
                await command.ExecuteNonQueryAsync();
            }
            _snapshotDir = dir;
            _snapshotTime = DateTime.UtcNow;
            return dir;
        }
        catch (Exception e)
        {
            _logger.LogError($"Could not read the UniFetcher profile's cookies: {e.Message}");
            return null;
        }
        finally
        {
            _snapshotLock.Release();
        }
    }

    public async Task<bool> ForgetSiteAsync(string site)
    {
        var db = Path.Combine(ProfileDirectory, "cookies.sqlite");
        if (!File.Exists(db))
        {
            return true;
        }
        try
        {
            await CloseProfileWindowsAsync();
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db, Pooling = false }.ToString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM moz_cookies WHERE host = $h OR host = $dh OR host LIKE $sub";
            command.Parameters.AddWithValue("$h", site);
            command.Parameters.AddWithValue("$dh", "." + site);
            command.Parameters.AddWithValue("$sub", "%." + site);
            await command.ExecuteNonQueryAsync();
            InvalidateSnapshot();
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError($"Could not forget {site}: {e.Message}");
            return false;
        }
    }

    public async Task<bool> ClearDataAsync()
    {
        if (!Directory.Exists(ProfileDirectory))
        {
            return true;
        }
        await CloseProfileWindowsAsync();
        // Only files inside UniFetcher's own profile folder; settings (user.js) and the profile itself are kept
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "user.js", "prefs.js", "times.json", "compatibility.ini" };
        var ok = true;
        foreach (var entry in Directory.EnumerateFileSystemEntries(ProfileDirectory))
        {
            if (keep.Contains(Path.GetFileName(entry)))
            {
                continue;
            }
            try
            {
                if (Directory.Exists(entry))
                {
                    Directory.Delete(entry, true);
                }
                else
                {
                    File.Delete(entry);
                }
            }
            catch (Exception e)
            {
                _logger.LogWarning($"Could not delete {entry}: {e.Message}");
                ok = false;
            }
        }
        // Firefox also keeps a cache for the profile under LocalAppData; only clear UniFetcher's
        InvalidateSnapshot();
        return ok;
    }

    public async Task CloseProfileWindowsAsync()
    {
        if (!IsProfileRunning)
        {
            return;
        }
        // Only windows UniFetcher opened itself (it remembers them); it never looks into other programs
        List<Process> ours;
        lock (_startedProcesses)
        {
            ours = _startedProcesses.ToList();
        }
        foreach (var process in ours)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.CloseMainWindow();
                }
            }
            catch
            {
                // already gone
            }
        }
        for (var i = 0; i < 20 && IsProfileRunning; i++)
        {
            await Task.Delay(500);
        }
        if (IsProfileRunning)
        {
            _logger.LogWarning("The UniFetcher browser window is still open; close it to let UniFetcher read its cookies.");
        }
    }

    /// <summary>Remembers a Firefox window UniFetcher opened with its own profile, so it can close it politely later.</summary>
    private void Remember(Process? process)
    {
        if (process is null)
        {
            return;
        }
        lock (_startedProcesses)
        {
            _startedProcesses.RemoveAll(p =>
            {
                try
                {
                    return p.HasExited;
                }
                catch
                {
                    return true;
                }
            });
            _startedProcesses.Add(process);
        }
    }

    public void ClearCookieSnapshot()
    {
        InvalidateSnapshot();
        try
        {
            if (Directory.Exists(_snapshotRoot))
            {
                Directory.Delete(_snapshotRoot, true);
            }
        }
        catch (Exception e)
        {
            _logger.LogWarning($"Could not delete the cookie copy: {e.Message}");
        }
    }

    public void InvalidateSnapshot()
    {
        _snapshotDir = null;
        _snapshotTime = DateTime.MinValue;
    }

    private static void CopyShared(string source, string target)
    {
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
    }

    private static bool IsLocked(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }
}
