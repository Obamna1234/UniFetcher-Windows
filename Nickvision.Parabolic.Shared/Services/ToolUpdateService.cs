using Microsoft.Extensions.Logging;
using Nickvision.Desktop.Application;
using Nickvision.Desktop.Filesystem;
using Nickvision.Desktop.Network;
using Nickvision.Parabolic.Shared.Helpers;
using Nickvision.Parabolic.Shared.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Nickvision.Parabolic.Shared.Services;

/// <summary>
/// Keeps yt-dlp and ffmpeg current (spec section 2).
///
/// Layout (installed copy):  %LOCALAPPDATA%\UniFetcher\tools\yt-dlp\&lt;version&gt;\yt-dlp.exe
///                           %LOCALAPPDATA%\UniFetcher\tools\ffmpeg\&lt;build&gt;\ffmpeg.exe (+ ffprobe.exe)
/// The copies bundled with the installer are the starting point ("bundled"); they are never modified.
/// Only the version in use and the one before it are kept, so "Roll back" always has something to go back to.
/// </summary>
public partial class ToolUpdateService : IToolUpdateService, IDisposable
{
    public const string Bundled = "bundled";
    private static readonly TimeSpan FetchCheckInterval = TimeSpan.FromHours(6);
    // FFmpeg-Builds publishes a new "latest" every day; ~100 MB a day would be wasteful, so ffmpeg is refreshed weekly at most.
    private static readonly TimeSpan FfmpegMinAge = TimeSpan.FromDays(7);

    private readonly ILogger<ToolUpdateService> _logger;
    private readonly IConfigurationService _configurationService;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _lock;
    private readonly string _toolsRoot;
    private string? _ytdlpVersionCache;
    private string? _ytdlpVersionCachePath;

    public event EventHandler<ToolUpdateResult>? UpdateFound;

    public ToolUpdateService(ILogger<ToolUpdateService> logger, AppInfo appInfo, IConfigurationService configurationService, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _configurationService = configurationService;
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.Timeout = TimeSpan.FromMinutes(10);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("UniFetcher");
        _lock = new SemaphoreSlim(1, 1);
        _toolsRoot = appInfo.IsPortable ? Path.Combine(Desktop.System.Environment.ExecutingDirectory, "tools") : Path.Combine(UserDirectories.LocalData, appInfo.Name, "tools");
    }

    public void Dispose()
    {
        _lock.Dispose();
        GC.SuppressFinalize(this);
    }

    public string YtdlpPath
    {
        get
        {
            var active = _configurationService.YtdlpActiveVersion;
            if (active != Bundled)
            {
                var path = Path.Combine(_toolsRoot, "yt-dlp", active, "yt-dlp.exe");
                if (File.Exists(path))
                {
                    return path;
                }
                _logger.LogWarning($"Installed yt-dlp {active} is missing, using the bundled copy.");
                _configurationService.YtdlpActiveVersion = Bundled;
            }
            return BundledPath("yt-dlp");
        }
    }

    public string FfmpegPath
    {
        get
        {
            var active = _configurationService.FfmpegActiveVersion;
            if (active != Bundled)
            {
                var path = Path.Combine(_toolsRoot, "ffmpeg", active, "ffmpeg.exe");
                if (File.Exists(path) && File.Exists(Path.Combine(_toolsRoot, "ffmpeg", active, "ffprobe.exe")))
                {
                    return path;
                }
                _logger.LogWarning($"Installed ffmpeg {active} is missing, using the bundled copy.");
                _configurationService.FfmpegActiveVersion = Bundled;
            }
            return BundledPath("ffmpeg");
        }
    }

    public DateTime? LastCheck => DateTime.TryParse(_configurationService.ToolsLastCheck, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date) ? date : null;

    public bool CanRollBack => !string.IsNullOrEmpty(_configurationService.YtdlpPreviousVersion) || !string.IsNullOrEmpty(_configurationService.FfmpegPreviousVersion);

    public async Task<string> GetYtdlpVersionAsync()
    {
        var path = YtdlpPath;
        if (_ytdlpVersionCache is not null && _ytdlpVersionCachePath == path)
        {
            return _ytdlpVersionCache;
        }
        var output = await RunAsync(path, "--version");
        _ytdlpVersionCache = string.IsNullOrWhiteSpace(output) ? "not found" : output.Trim();
        _ytdlpVersionCachePath = path;
        return _ytdlpVersionCache;
    }

    public async Task<string> GetFfmpegVersionAsync()
    {
        var output = await RunAsync(FfmpegPath, "-version");
        var match = FfmpegVersionRegex().Match(output);
        return match.Success ? match.Groups[1].Value : "not found";
    }

    public async Task EnsureRecentCheckAsync(CancellationToken cancellationToken = default)
    {
        var last = LastCheck;
        if (last is not null && DateTime.UtcNow - last.Value < FetchCheckInterval)
        {
            return;
        }
        // Don't hold up the fetch for long if GitHub is slow
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        try
        {
            await CheckAsync(false, null, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Update check before fetch took too long; continuing with the current yt-dlp.");
        }
    }

    public async Task<bool> TryUpdateAfterExtractorErrorAsync(CancellationToken cancellationToken = default)
    {
        if (_configurationService.ToolUpdateMode != ToolUpdateMode.Automatic)
        {
            _logger.LogInformation("Extractor error, but automatic yt-dlp updates are off.");
            return false;
        }
        _logger.LogInformation("Extractor error: checking for a newer yt-dlp before retrying.");
        var before = _configurationService.YtdlpActiveVersion;
        var res = await CheckAsync(true, null, cancellationToken, ytdlpOnly: true);
        return res.Outcome == ToolUpdateOutcome.Installed && _configurationService.YtdlpActiveVersion != before;
    }

    public Task<ToolUpdateResult> CheckAsync(bool install = false, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default) => CheckAsync(install, progress, cancellationToken, false);

    private async Task<ToolUpdateResult> CheckAsync(bool install, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken, bool ytdlpOnly)
    {
        var mode = _configurationService.ToolUpdateMode;
        if (mode == ToolUpdateMode.Off && !install)
        {
            return new ToolUpdateResult(ToolUpdateOutcome.Skipped);
        }
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var doInstall = install || mode == ToolUpdateMode.Automatic;
            string? newYtdlp = null;
            var ffmpegChanged = false;
            string? error = null;
            var anyAvailable = false;

            // yt-dlp
            try
            {
                var (repo, release) = await GetLatestYtdlpReleaseAsync(cancellationToken);
                var latest = release.GetProperty("tag_name").GetString()!.TrimStart('v');
                var current = await GetYtdlpVersionAsync();
                _logger.LogInformation($"yt-dlp: using {current}, latest on {repo} is {latest}.");
                if (CompareYtdlpVersions(latest, current) > 0)
                {
                    anyAvailable = true;
                    if (doInstall)
                    {
                        await InstallYtdlpAsync(release, latest, progress, cancellationToken);
                    }
                    newYtdlp = latest;
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _logger.LogError($"yt-dlp update check failed: {e.Message}");
                error = e.Message;
            }

            // ffmpeg
            if (!ytdlpOnly)
            {
                try
                {
                    var release = await GetJsonAsync("https://api.github.com/repos/yt-dlp/FFmpeg-Builds/releases/tags/latest", cancellationToken);
                    var asset = FindAsset(release, FfmpegAssetName) ?? throw new InvalidOperationException($"{FfmpegAssetName} not found in yt-dlp/FFmpeg-Builds");
                    var updated = asset.GetProperty("updated_at").GetDateTime().ToUniversalTime();
                    var stamp = updated.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                    var installedAt = DateTime.TryParse(_configurationService.FfmpegInstalledAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d : AppBuildDateUtc();
                    if (stamp != _configurationService.FfmpegActiveVersion && updated - installedAt >= FfmpegMinAge)
                    {
                        anyAvailable = true;
                        if (doInstall)
                        {
                            await InstallFfmpegAsync(release, asset, stamp, progress, cancellationToken);
                            ffmpegChanged = true;
                        }
                    }
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    _logger.LogError($"ffmpeg update check failed: {e.Message}");
                    error ??= e.Message;
                }
            }

            if (error is null)
            {
                _configurationService.ToolsLastCheck = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            }
            ToolUpdateResult result;
            if (anyAvailable && doInstall && (newYtdlp is not null || ffmpegChanged))
            {
                result = new ToolUpdateResult(ToolUpdateOutcome.Installed, newYtdlp, ffmpegChanged, error);
            }
            else if (anyAvailable && !doInstall)
            {
                result = new ToolUpdateResult(ToolUpdateOutcome.Available, newYtdlp, false, error);
            }
            else if (error is not null)
            {
                result = new ToolUpdateResult(ToolUpdateOutcome.Failed, null, false, error);
            }
            else
            {
                result = new ToolUpdateResult(ToolUpdateOutcome.UpToDate);
            }
            if (result.Outcome is ToolUpdateOutcome.Installed or ToolUpdateOutcome.Available)
            {
                UpdateFound?.Invoke(this, result);
            }
            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> RollBackAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var rolled = false;
            var prevYtdlp = _configurationService.YtdlpPreviousVersion;
            if (!string.IsNullOrEmpty(prevYtdlp))
            {
                var current = _configurationService.YtdlpActiveVersion;
                _configurationService.YtdlpActiveVersion = prevYtdlp;
                _configurationService.YtdlpPreviousVersion = string.Empty;
                // Don't immediately re-install the version we just left
                _configurationService.YtdlpSkippedVersion = current;
                _logger.LogInformation($"Rolled yt-dlp back from {current} to {prevYtdlp}.");
                rolled = true;
            }
            var prevFfmpeg = _configurationService.FfmpegPreviousVersion;
            if (!string.IsNullOrEmpty(prevFfmpeg))
            {
                var current = _configurationService.FfmpegActiveVersion;
                _configurationService.FfmpegActiveVersion = prevFfmpeg;
                _configurationService.FfmpegPreviousVersion = string.Empty;
                // Keep the build date so the weekly rule doesn't pull the same build straight back
                _configurationService.FfmpegInstalledAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                _logger.LogInformation($"Rolled ffmpeg back from {current} to {prevFfmpeg}.");
                rolled = true;
            }
            _ytdlpVersionCache = null;
            CleanUp();
            return rolled;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>yt-dlp messages that mean the extractor for a site is broken or missing — usually fixed by a newer yt-dlp.</summary>
    public static bool IsExtractorError(string log)
    {
        if (string.IsNullOrEmpty(log))
        {
            return false;
        }
        return log.Contains("Unsupported URL", StringComparison.OrdinalIgnoreCase)
            || log.Contains("Unable to extract", StringComparison.OrdinalIgnoreCase)
            || log.Contains("nsig extraction failed", StringComparison.OrdinalIgnoreCase)
            || log.Contains("Signature extraction failed", StringComparison.OrdinalIgnoreCase)
            || log.Contains("you are on the latest version", StringComparison.OrdinalIgnoreCase)
            || log.Contains("please report this issue", StringComparison.OrdinalIgnoreCase)
            || ExtractorErrorRegex().IsMatch(log);
    }

    private async Task<(string Repo, JsonElement Release)> GetLatestYtdlpReleaseAsync(CancellationToken cancellationToken)
    {
        var repo = _configurationService.YtdlpChannel switch
        {
            YtdlpChannel.Stable => "yt-dlp/yt-dlp",
            YtdlpChannel.Master => "yt-dlp/yt-dlp-master-builds",
            _ => "yt-dlp/yt-dlp-nightly-builds"
        };
        var release = await GetJsonAsync($"https://api.github.com/repos/{repo}/releases/latest", cancellationToken);
        var tag = release.GetProperty("tag_name").GetString()?.TrimStart('v') ?? string.Empty;
        if (tag == _configurationService.YtdlpSkippedVersion)
        {
            // The user rolled back from this exact version: pretend there is nothing new until the next one
            release = JsonDocument.Parse($"{{\"tag_name\":\"0\"}}").RootElement.Clone();
        }
        return (repo, release);
    }

    private async Task InstallYtdlpAsync(JsonElement release, string version, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        var asset = FindAsset(release, YtdlpAssetName) ?? throw new InvalidOperationException($"{YtdlpAssetName} not found in the yt-dlp release");
        var dir = Path.Combine(_toolsRoot, "yt-dlp", SafeName(version));
        var tmp = dir + ".tmp";
        RecreateDirectory(tmp);
        var exe = Path.Combine(tmp, "yt-dlp.exe");
        await DownloadVerifiedAsync(release, asset, exe, "SHA2-256SUMS", progress, cancellationToken);
        var reported = (await RunAsync(exe, "--version")).Trim();
        if (string.IsNullOrEmpty(reported))
        {
            Directory.Delete(tmp, true);
            throw new InvalidOperationException("The downloaded yt-dlp did not start");
        }
        ReplaceDirectory(tmp, dir);
        var previous = _configurationService.YtdlpActiveVersion;
        _configurationService.YtdlpPreviousVersion = previous;
        _configurationService.YtdlpActiveVersion = SafeName(version);
        _configurationService.YtdlpSkippedVersion = string.Empty;
        _ytdlpVersionCache = null;
        _logger.LogInformation($"Installed yt-dlp {version} (previous: {previous}).");
        CleanUp();
    }

    private async Task InstallFfmpegAsync(JsonElement release, JsonElement asset, string stamp, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        var dir = Path.Combine(_toolsRoot, "ffmpeg", stamp);
        var tmp = dir + ".tmp";
        RecreateDirectory(tmp);
        var zip = Path.Combine(tmp, "ffmpeg.zip");
        await DownloadVerifiedAsync(release, asset, zip, "checksums.sha256", progress, cancellationToken);
        using (var archive = ZipFile.OpenRead(zip))
        {
            foreach (var name in new[] { "ffmpeg.exe", "ffprobe.exe" })
            {
                var entry = archive.Entries.FirstOrDefault(x => x.FullName.EndsWith("/bin/" + name, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException($"{name} missing from {FfmpegAssetName}");
                entry.ExtractToFile(Path.Combine(tmp, name), true);
            }
        }
        File.Delete(zip);
        if (string.IsNullOrEmpty(await RunAsync(Path.Combine(tmp, "ffmpeg.exe"), "-version")))
        {
            Directory.Delete(tmp, true);
            throw new InvalidOperationException("The downloaded ffmpeg did not start");
        }
        ReplaceDirectory(tmp, dir);
        var previous = _configurationService.FfmpegActiveVersion;
        _configurationService.FfmpegPreviousVersion = previous;
        _configurationService.FfmpegActiveVersion = stamp;
        _configurationService.FfmpegInstalledAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        _logger.LogInformation($"Installed ffmpeg build {stamp} (previous: {previous}).");
        CleanUp();
    }

    /// <summary>Downloads an asset and checks its SHA-256 against GitHub's digest or the release's checksum file. Refuses unverified files.</summary>
    private async Task DownloadVerifiedAsync(JsonElement release, JsonElement asset, string path, string checksumFileName, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        var name = asset.GetProperty("name").GetString()!;
        var url = asset.GetProperty("browser_download_url").GetString()!;
        if (!url.StartsWith("https://github.com/yt-dlp/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to download from an unexpected address: {url}");
        }
        string? expected = null;
        if (asset.TryGetProperty("digest", out var digest) && digest.ValueKind == JsonValueKind.String && digest.GetString()!.StartsWith("sha256:", StringComparison.Ordinal))
        {
            expected = digest.GetString()!["sha256:".Length..];
        }
        else if (FindAsset(release, checksumFileName) is JsonElement sums)
        {
            var text = await _httpClient.GetStringAsync(sums.GetProperty("browser_download_url").GetString(), cancellationToken);
            foreach (var line in text.Split('\n'))
            {
                var parts = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && parts[1].TrimStart('*').Trim() == name)
                {
                    expected = parts[0];
                    break;
                }
            }
        }
        if (string.IsNullOrEmpty(expected))
        {
            throw new InvalidOperationException($"No checksum published for {name}; not installing it");
        }
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? 0L;
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
        {
            var buffer = new byte[81920];
            long read = 0;
            int n;
            var lastReport = 0L;
            while ((n = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, n), cancellationToken);
                read += n;
                if (read - lastReport > 512 * 1024)
                {
                    progress?.Report(new DownloadProgress(total, read, false));
                    lastReport = read;
                }
            }
            progress?.Report(new DownloadProgress(total, read, true));
        }
        await using (var check = File.OpenRead(path))
        {
            var actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(check, cancellationToken));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(path);
                throw new InvalidOperationException($"Checksum mismatch for {name}; the download was discarded");
            }
        }
    }

    private async Task<JsonElement> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, default, cancellationToken);
        return json.RootElement.Clone();
    }

    private static JsonElement? FindAsset(JsonElement release, string name)
    {
        if (!release.TryGetProperty("assets", out var assets))
        {
            return null;
        }
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() == name)
            {
                return asset;
            }
        }
        return null;
    }

    /// <summary>Compares yt-dlp versions like 2026.10.24 and 2026.10.24.233510 number by number.</summary>
    public static int CompareYtdlpVersions(string a, string b)
    {
        static long[] Parse(string v) => v.Split('.', '-').Select(x => long.TryParse(x, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0).ToArray();
        var pa = Parse(a);
        var pb = Parse(b);
        for (var i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            var x = i < pa.Length ? pa[i] : 0;
            var y = i < pb.Length ? pb[i] : 0;
            if (x != y)
            {
                return x.CompareTo(y);
            }
        }
        return 0;
    }

    /// <summary>Removes old copies in UniFetcher's own tools folder, keeping only the version in use and the previous one.</summary>
    private void CleanUp()
    {
        foreach (var (tool, keep) in new[] { ("yt-dlp", new[] { _configurationService.YtdlpActiveVersion, _configurationService.YtdlpPreviousVersion }), ("ffmpeg", new[] { _configurationService.FfmpegActiveVersion, _configurationService.FfmpegPreviousVersion }) })
        {
            var root = Path.Combine(_toolsRoot, tool);
            if (!Directory.Exists(root))
            {
                continue;
            }
            foreach (var dir in Directory.GetDirectories(root))
            {
                var name = Path.GetFileName(dir);
                if (keep.Contains(name))
                {
                    continue;
                }
                try
                {
                    Directory.Delete(dir, true);
                }
                catch (Exception e)
                {
                    _logger.LogWarning($"Could not remove old {tool} copy {name}: {e.Message}");
                }
            }
        }
    }

    private static void RecreateDirectory(string dir)
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, true);
        }
        Directory.CreateDirectory(dir);
    }

    private static void ReplaceDirectory(string source, string destination)
    {
        if (Directory.Exists(destination))
        {
            Directory.Delete(destination, true);
        }
        Directory.Move(source, destination);
    }

    private static string SafeName(string version) => string.Concat(version.Where(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_'));

    private static string BundledPath(string name) => Desktop.System.Environment.FindDependency(name, Desktop.System.DependencySearchOption.Global) ?? name;

    private static DateTime AppBuildDateUtc()
    {
        try
        {
            return File.GetLastWriteTimeUtc(Desktop.System.Environment.ExecutingPath);
        }
        catch
        {
            return DateTime.UtcNow;
        }
    }

    private static string YtdlpAssetName => RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "yt-dlp_arm64.exe" : "yt-dlp.exe";

    private static string FfmpegAssetName => RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "ffmpeg-master-latest-winarm64-gpl.zip" : "ffmpeg-master-latest-win64-gpl.zip";

    private async Task<string> RunAsync(string exe, string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(exe, arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await process.WaitForExitAsync(timeout.Token);
            return process.ExitCode == 0 ? await output : string.Empty;
        }
        catch (Exception e)
        {
            _logger.LogWarning($"Could not run {exe}: {e.Message}");
            return string.Empty;
        }
    }

    [GeneratedRegex(@"ffmpeg version (\S+)")]
    private static partial Regex FfmpegVersionRegex();

    [GeneratedRegex(@"ERROR: \[[^\]]+\] [^:\n]*: Unable to|ERROR: \[generic\] Unsupported", RegexOptions.IgnoreCase)]
    private static partial Regex ExtractorErrorRegex();
}
