using System;
using System.Diagnostics;
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
/// - If an earlier copy is stuck in the background (no window, not answering), you're asked whether to close it.
/// - If UniFetcher can't start, a message says so and the details are saved to crash.log, instead of nothing happening.
/// </summary>
public static partial class StartupGuard
{
    private const uint MB_OK = 0x0;
    private const uint MB_YESNO = 0x4;
    private const uint MB_ICONERROR = 0x10;
    private const uint MB_ICONWARNING = 0x30;
    private const uint MB_SETFOREGROUND = 0x10000;
    private const int IDYES = 6;

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
        // It didn't answer: it's stuck in the background without a window
        var answer = MessageBox("UniFetcher is already running in the background, but it isn't responding.\n\nClose it and start UniFetcher again?\n\n(Any downloads it was doing will continue where they stopped next time.)", "UniFetcher", MB_YESNO | MB_ICONWARNING | MB_SETFOREGROUND);
        if (answer != IDYES)
        {
            return false;
        }
        CloseOtherCopies();
        try
        {
            // Wait for the stuck copy to let go; when a process is ended its lock is released ("abandoned")
            if (!_instanceMutex.WaitOne(TimeSpan.FromSeconds(15)))
            {
                MessageBox("UniFetcher couldn't close the copy that's stuck. Open Task Manager (Ctrl+Shift+Esc), find UniFetcher under Processes or on the Details tab, click End task, then start UniFetcher again.", "UniFetcher", MB_OK | MB_ICONERROR | MB_SETFOREGROUND);
                return false;
            }
        }
        catch (AbandonedMutexException)
        {
            // We own it now
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
            var urls = args.Where(a => a.StartsWith("http", StringComparison.OrdinalIgnoreCase) || a.StartsWith("unifetcher://", StringComparison.OrdinalIgnoreCase))
                .Select(a => a.StartsWith("unifetcher://", StringComparison.OrdinalIgnoreCase) ? "https://" + a["unifetcher://".Length..] : a)
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

    private static void CloseOtherCopies()
    {
        var me = Environment.ProcessId;
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "UniFetcher")))
        {
            try
            {
                if (process.Id != me && process.SessionId == Process.GetCurrentProcess().SessionId)
                {
                    // Also ends the yt-dlp / ffmpeg it started
                    process.Kill(true);
                    process.WaitForExit(10000);
                }
            }
            catch
            {
                // already gone, or not ours
            }
            finally
            {
                process.Dispose();
            }
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
