using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Nickvision.Parabolic.WinUI.Helpers;

/// <summary>
/// UniFetcher: makes starting the app predictable.
/// - Only one copy runs. Starting it again brings the open window to the front.
/// - If an earlier copy is stuck in the background (no window, not answering), it tells you how to end it in Task Manager.
/// - If UniFetcher can't start, a message says so and the details are saved to crash.log, instead of nothing happening.
/// </summary>
public static partial class StartupGuard
{
    private const uint MB_OK = 0x0;
    private const uint MB_ICONERROR = 0x10;
    private const uint MB_ICONWARNING = 0x30;
    private const uint MB_SETFOREGROUND = 0x10000;

    private static Mutex? _instanceMutex;

    private static string MutexName => @"Local\UniFetcher.Instance." + Environment.UserName;

    private static string PipeName => "UniFetcher.Inbox." + Environment.UserName;

    public static bool IsPortable(string[] args) => args.Contains("--portable");

    /// <summary>Folder for crash.log: next to the app when portable, otherwise %LOCALAPPDATA%\UniFetcher.</summary>
    public static string LogFolder(string[] args) => IsPortable(args) ? AppContext.BaseDirectory : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UniFetcher");

    /// <returns>True if this copy should start; false if another copy is handling it.</returns>
    public static bool TryBecomeOnlyInstance(string[] args)
    {
        _instanceMutex = new Mutex(true, MutexName, out var createdNew);
        if (createdNew)
        {
            return true;
        }
        // Another copy is running: ask it to show its window (and pass on a link if we were started with one)
        if (AskRunningCopyToActivate(args))
        {
            return false;
        }
        // It didn't answer: it's stuck in the background without a window. UniFetcher doesn't end other programs itself;
        // it tells you how to do it, then waits a little in case you do.
        MessageBox("UniFetcher is already running in the background, but it isn't responding.\n\nTo fix it: press Ctrl+Shift+Esc to open Task Manager, find UniFetcher under Processes (or UniFetcher.exe on the Details tab), click End task, then start UniFetcher again.", "UniFetcher", MB_OK | MB_ICONWARNING | MB_SETFOREGROUND);
        try
        {
            if (!_instanceMutex.WaitOne(TimeSpan.FromSeconds(2)))
            {
                return false;
            }
        }
        catch (AbandonedMutexException)
        {
            // The stuck copy was ended meanwhile; we own it now
        }
        return true;
    }

    public static void Release()
    {
        try
        {
            _instanceMutex?.ReleaseMutex();
        }
        catch
        {
            // not held
        }
    }

    /// <summary>Saves the error to crash.log and tells you, instead of UniFetcher silently not opening.</summary>
    public static void ReportCrash(Exception e, string[] args)
    {
        var path = WriteCrashLog(e, args);
        MessageBox($"UniFetcher couldn't start.\n\n{e.Message}\n\nThe details were saved to:\n{path}\n\nPlease send that file's text when reporting the problem.", "UniFetcher", MB_OK | MB_ICONERROR | MB_SETFOREGROUND);
    }

    public static string WriteCrashLog(Exception e, string[] args)
    {
        var folder = LogFolder(args);
        var path = Path.Combine(folder, "crash.log");
        try
        {
            Directory.CreateDirectory(folder);
            File.AppendAllText(path, $"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====\nUniFetcher {typeof(StartupGuard).Assembly.GetName().Version}\nWindows {Environment.OSVersion.Version}\nApp folder: {AppContext.BaseDirectory}\n{e}\n\n");
        }
        catch
        {
            path = "(couldn't save the details)";
        }
        return path;
    }

    private static bool AskRunningCopyToActivate(string[] args)
    {
        try
        {
            // Only accept http(s) URLs passed on the command line. The "unifetcher://" custom-scheme
            // rewrite was removed: the installer does not register that protocol handler, so the branch
            // was unreachable dead code and a latent URI-handler surface if a handler were ever re-added.
            var urls = args.Where(a => a.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var message = "{\"type\":\"activate\",\"urls\":[" + string.Join(",", urls.Select(u => JsonSerializer.Serialize(u, StartupJsonContext.Default.String))) + "]}\n";
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            pipe.Connect(3000);
            var bytes = Encoding.UTF8.GetBytes(message);
            pipe.Write(bytes, 0, bytes.Length);
            pipe.Flush();
            pipe.ReadTimeout = 3000;
            var answer = new byte[1];
            return pipe.Read(answer, 0, 1) == 1 && answer[0] == (byte)'1';
        }
        catch
        {
            return false;
        }
    }

    private static int MessageBox(string text, string caption, uint type) => MessageBoxW(IntPtr.Zero, text, caption, type);

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}

[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
internal partial class StartupJsonContext : System.Text.Json.Serialization.JsonSerializerContext
{
}
