using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace UniFetcher.NativeHost;

/// <summary>
/// Browser "native messaging" host for the Send to UniFetcher extension.
/// The browser starts this program and talks to it through standard input/output (4-byte length + JSON).
/// It forwards each message to the UniFetcher app through a local pipe that only your Windows user can open,
/// starting UniFetcher if it isn't running. Nothing goes over the internet.
/// </summary>
public static class Program
{
    private static readonly string PipeName = "UniFetcher.Inbox." + Environment.UserName;

    public static int Main()
    {
        using var input = Console.OpenStandardInput();
        using var output = Console.OpenStandardOutput();
        while (true)
        {
            var message = ReadMessage(input);
            if (message is null)
            {
                return 0; // browser closed the connection
            }
            string reply;
            try
            {
                reply = Forward(message) ? "{\"ok\":true}" : "{\"ok\":false,\"error\":\"Couldn't reach UniFetcher. Is it installed?\"}";
            }
            catch (Exception e)
            {
                reply = "{\"ok\":false,\"error\":" + JsonEncode(e.Message) + "}";
            }
            WriteMessage(output, reply);
        }
    }

    private static string? ReadMessage(Stream input)
    {
        var header = new byte[4];
        if (!ReadExactly(input, header))
        {
            return null;
        }
        var length = BitConverter.ToInt32(header, 0);
        if (length <= 0 || length > 64 * 1024 * 1024)
        {
            return null;
        }
        var body = new byte[length];
        return ReadExactly(input, body) ? Encoding.UTF8.GetString(body) : null;
    }

    private static bool ReadExactly(Stream stream, byte[] buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = stream.Read(buffer, read, buffer.Length - read);
            if (n == 0)
            {
                return false;
            }
            read += n;
        }
        return true;
    }

    private static void WriteMessage(Stream output, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        output.Write(BitConverter.GetBytes(bytes.Length), 0, 4);
        output.Write(bytes, 0, bytes.Length);
        output.Flush();
    }

    private static bool Forward(string message)
    {
        // Check it's the kind of message we expect before passing it on
        using (var doc = JsonDocument.Parse(message))
        {
            if (!doc.RootElement.TryGetProperty("type", out var type) || type.GetString() != "links")
            {
                throw new InvalidOperationException("Unknown request");
            }
        }
        if (TrySend(message, 1500))
        {
            return true;
        }
        // UniFetcher isn't running: start it (it sits next to this program) and wait for it
        var app = Path.Combine(AppContext.BaseDirectory, "UniFetcher.exe");
        if (!File.Exists(app))
        {
            return false;
        }
        // UseShellExecute=false launches the verified absolute path directly, without routing
        // through the OS shell / ShellExecute resolution. The path is fixed and File.Exists-checked
        // above; no browser-supplied data reaches it.
        Process.Start(new ProcessStartInfo(app) { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory });
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            if (TrySend(message, 2000))
            {
                return true;
            }
            Thread.Sleep(1000);
        }
        return false;
    }

    private static bool TrySend(string message, int timeoutMs)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            pipe.Connect(timeoutMs);
            var bytes = Encoding.UTF8.GetBytes(message.Replace('\n', ' ') + "\n");
            pipe.Write(bytes, 0, bytes.Length);
            pipe.Flush();
            var answer = new byte[1];
            return pipe.Read(answer, 0, 1) == 1 && answer[0] == (byte)'1';
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string JsonEncode(string text)
    {
        var builder = new StringBuilder("\"");
        foreach (var c in text)
        {
            builder.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                _ when c < ' ' => "\\u" + ((int)c).ToString("x4"),
                _ => c.ToString()
            });
        }
        return builder.Append('"').ToString();
    }
}
