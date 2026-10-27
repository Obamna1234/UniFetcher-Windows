using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Nickvision.Desktop.Application;
using Nickvision.Desktop.Filesystem;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Nickvision.Parabolic.Shared.Services;

public class LinksReceivedEventArgs : EventArgs
{
    public IReadOnlyList<Uri> Urls { get; }
    /// <summary>True for "Send all tabs" (goes to Bulk Import), false for a single page/link/video.</summary>
    public bool IsBulk { get; }
    public string Title { get; }

    public LinksReceivedEventArgs(IReadOnlyList<Uri> urls, bool isBulk, string title)
    {
        Urls = urls;
        IsBulk = isBulk;
        Title = title;
    }
}

public interface IInboxService
{
    /// <summary>Links sent from the browser extension. Raised on a background thread.</summary>
    event EventHandler<LinksReceivedEventArgs>? LinksReceived;

    /// <summary>UniFetcher was started again: show the existing window. Raised on a background thread.</summary>
    event EventHandler? ActivationRequested;
}

/// <summary>
/// Receives links from the "Send to UniFetcher" browser extension (spec 3b).
/// The extension talks to UniFetcher.NativeHost.exe (native messaging), which passes links here through a local pipe
/// that only the current Windows user can open. Also registers that helper with Firefox, Brave, Chrome and Edge.
/// </summary>
public class InboxService : BackgroundService, IInboxService
{
    public const string HostName = "com.unifetcher.host";
    public const string FirefoxExtensionId = "unifetcher-extension@obamna1234.github.io";
    public const string ChromiumExtensionId = "iofbbjgpimljhofeflakhkjknpelglob";
    private const int MaxLinks = 10000;

    private readonly ILogger<InboxService> _logger;
    private readonly AppInfo _appInfo;
    private readonly IFirefoxProfileService _profile;
    private readonly IConfigurationService _configurationService;

    public event EventHandler<LinksReceivedEventArgs>? LinksReceived;
    public event EventHandler? ActivationRequested;

    public InboxService(ILogger<InboxService> logger, AppInfo appInfo, IFirefoxProfileService profile, IConfigurationService configurationService)
    {
        _configurationService = configurationService;
        _logger = logger;
        _appInfo = appInfo;
        _profile = profile;
    }

    public static string PipeName => "UniFetcher.Inbox." + Environment.UserName;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        // The browser extension is optional and off by default: only then does UniFetcher register itself with your browsers
        if (_configurationService.Get("BrowserExtensionEnabled", false))
        {
            RegisterNativeHost();
            try
            {
                // Put the extension into the UniFetcher Firefox profile when a signed copy ships with UniFetcher
                await _profile.InstallExtensionAsync();
            }
            catch (Exception e)
            {
                _logger.LogWarning($"Could not add the extension to the UniFetcher profile: {e.Message}");
            }
        }
        else
        {
            UnregisterNativeHost();
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(stoppingToken);
                var line = await ReadLineAsync(server, stoppingToken);
                var accepted = line is not null && Handle(line);
                await server.WriteAsync(new[] { (byte)(accepted ? '1' : '0') }, stoppingToken);
                await server.FlushAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                _logger.LogError($"Extension inbox error: {e.Message}");
                await Task.Delay(500, stoppingToken);
            }
        }
    }

    private bool Handle(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();
            if (type == "activate")
            {
                // Someone started UniFetcher again: bring this window forward (and open a link it was started with)
                ActivationRequested?.Invoke(this, EventArgs.Empty);
                if (root.TryGetProperty("urls", out var startUrls) && startUrls.GetArrayLength() > 0 && Uri.TryCreate(startUrls[0].GetString(), UriKind.Absolute, out var startUrl))
                {
                    LinksReceived?.Invoke(this, new LinksReceivedEventArgs([startUrl], false, string.Empty));
                }
                return true;
            }
            if (type != "links")
            {
                return false;
            }
            var urls = new List<Uri>();
            foreach (var element in root.GetProperty("urls").EnumerateArray().Take(MaxLinks))
            {
                if (Uri.TryCreate(element.GetString(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
                {
                    urls.Add(uri);
                }
            }
            if (urls.Count == 0)
            {
                return false;
            }
            var isBulk = root.TryGetProperty("mode", out var mode) && mode.GetString() == "bulk";
            var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty;
            _logger.LogInformation($"Received {urls.Count} link(s) from the browser extension ({(isBulk ? "bulk" : "single")}).");
            LinksReceived?.Invoke(this, new LinksReceivedEventArgs(urls, isBulk, title));
            return true;
        }
        catch (Exception e)
        {
            _logger.LogWarning($"Ignored a malformed message from the extension: {e.Message}");
            return false;
        }
    }

    private static async Task<string?> ReadLineAsync(Stream stream, CancellationToken token)
    {
        var buffer = new MemoryStream();
        var one = new byte[1];
        while (buffer.Length < 64 * 1024 * 1024)
        {
            var n = await stream.ReadAsync(one, token);
            if (n == 0 || one[0] == (byte)'\n')
            {
                break;
            }
            buffer.WriteByte(one[0]);
        }
        return buffer.Length == 0 ? null : Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Tells Firefox, Brave, Chrome and Edge where the helper is (per-user registry keys; no admin rights).</summary>
    private void RegisterNativeHost()
    {
        try
        {
            var hostExe = Path.Combine(Desktop.System.Environment.ExecutingDirectory, "UniFetcher.NativeHost.exe");
            if (!File.Exists(hostExe))
            {
                _logger.LogWarning("UniFetcher.NativeHost.exe not found; the browser extension can't reach UniFetcher.");
                return;
            }
            var dir = Path.Combine(_appInfo.IsPortable ? Desktop.System.Environment.ExecutingDirectory : Path.Combine(UserDirectories.LocalData, _appInfo.Name), "native-host");
            Directory.CreateDirectory(dir);
            var firefoxManifest = Path.Combine(dir, "firefox.json");
            var chromiumManifest = Path.Combine(dir, "chromium.json");
            File.WriteAllText(firefoxManifest, Manifest(hostExe, $"\"allowed_extensions\": [\"{FirefoxExtensionId}\"]"));
            File.WriteAllText(chromiumManifest, Manifest(hostExe, $"\"allowed_origins\": [\"chrome-extension://{ChromiumExtensionId}/\"]"));
            SetKey(@"Software\Mozilla\NativeMessagingHosts\" + HostName, firefoxManifest);
            SetKey(@"Software\Google\Chrome\NativeMessagingHosts\" + HostName, chromiumManifest);
            SetKey(@"Software\BraveSoftware\Brave-Browser\NativeMessagingHosts\" + HostName, chromiumManifest);
            SetKey(@"Software\Microsoft\Edge\NativeMessagingHosts\" + HostName, chromiumManifest);
        }
        catch (Exception e)
        {
            _logger.LogError($"Could not register the browser extension helper: {e.Message}");
        }
    }

    private static string Manifest(string hostExe, string allowed) => $$"""
        {
          "name": "{{HostName}}",
          "description": "Lets the Send to UniFetcher extension pass links to UniFetcher on this PC",
          "path": {{JsonSerializer.Serialize(hostExe, StringJsonContext.Default.String)}},
          "type": "stdio",
          {{allowed}}
        }
        """;

    /// <summary>Removes only UniFetcher's own entries (from earlier test builds) when the extension is switched off.</summary>
    private void UnregisterNativeHost()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        foreach (var browser in new[] { @"Software\Mozilla\NativeMessagingHosts\", @"Software\Google\Chrome\NativeMessagingHosts\", @"Software\BraveSoftware\Brave-Browser\NativeMessagingHosts\", @"Software\Microsoft\Edge\NativeMessagingHosts\" })
        {
            try
            {
                Registry.CurrentUser.DeleteSubKey(browser + HostName, false);
            }
            catch (Exception e)
            {
                _logger.LogDebug($"Could not remove {browser}{HostName}: {e.Message}");
            }
        }
    }

    private static void SetKey(string path, string value)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        using var key = Registry.CurrentUser.CreateSubKey(path);
        if (key.GetValue(null) as string != value)
        {
            key.SetValue(null, value);
        }
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
internal partial class StringJsonContext : System.Text.Json.Serialization.JsonSerializerContext
{
}
