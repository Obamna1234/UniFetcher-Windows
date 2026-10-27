using Nickvision.Desktop.Globalization;
using Nickvision.Parabolic.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Nickvision.Parabolic.Shared.Helpers;

/// <summary>
/// UniFetcher "Copy diagnostic": what went wrong and what UniFetcher tried, safe to paste into a bug report.
/// Cookie values, link tokens/query strings, passwords and your Windows user name are removed. The cookie file is never included.
/// </summary>
public static partial class Diagnostic
{
    public static string Build(string url, FailureReason reason, string cookieSource, IReadOnlyList<string> steps, string log, ITranslationService? t)
    {
        var builder = new StringBuilder();
        builder.AppendLine("UniFetcher diagnostic");
        builder.AppendLine($"Reason: {reason.Label(t)} — {reason.Explanation(t)}");
        builder.AppendLine($"Link: {Clean(url)}");
        builder.AppendLine($"Cookies used: {(string.IsNullOrEmpty(cookieSource) ? "no" : "yes, from " + cookieSource)}");
        builder.AppendLine("Steps UniFetcher tried:");
        if (steps.Count == 0)
        {
            builder.AppendLine("- none (failed on the first try)");
        }
        foreach (var step in steps)
        {
            builder.AppendLine($"- {step}");
        }
        builder.AppendLine();
        builder.AppendLine("yt-dlp said:");
        var lines = log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var important = lines.Where(l => l.StartsWith("ERROR", StringComparison.Ordinal) || l.StartsWith("WARNING", StringComparison.Ordinal) || l.StartsWith("UniFetcher:", StringComparison.Ordinal) || l.StartsWith("[debug] yt-dlp version", StringComparison.Ordinal) || l.StartsWith("Skipped:", StringComparison.Ordinal)).TakeLast(20).ToList();
        if (important.Count == 0)
        {
            important = lines.TakeLast(10).ToList();
        }
        foreach (var line in important)
        {
            builder.AppendLine(Clean(line));
        }
        return builder.ToString();
    }

    /// <summary>Removes anything private from a line of text.</summary>
    public static string Clean(string text)
    {
        // Cookie headers and values
        text = CookieRegex().Replace(text, "$1<removed>");
        // Passwords / tokens passed on the command line
        text = SecretArgRegex().Replace(text, "$1 <removed>");
        // Query strings and fragments in links (tokens, signatures, session ids)
        text = UrlQueryRegex().Replace(text, m => m.Groups[1].Value + (m.Groups[2].Success ? "?<removed>" : string.Empty));
        // Your Windows user name in file paths
        var user = Environment.UserName;
        if (!string.IsNullOrEmpty(user))
        {
            text = text.Replace($"\\Users\\{user}\\", "\\Users\\<you>\\", StringComparison.OrdinalIgnoreCase).Replace($"/Users/{user}/", "/Users/<you>/", StringComparison.OrdinalIgnoreCase);
        }
        return text;
    }

    [GeneratedRegex(@"((?:set-)?cookie[s]?\s*[:=]\s*).*", RegexOptions.IgnoreCase)]
    private static partial Regex CookieRegex();

    [GeneratedRegex(@"(--(?:password|video-password|username|add-header|cookies)|-p|-u)\s+(?:""[^""]*""|'[^']*'|\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex SecretArgRegex();

    [GeneratedRegex(@"(https?://[^\s?#""'<>]+)(\?[^\s#""'<>]*)?(#[^\s""'<>]*)?", RegexOptions.IgnoreCase)]
    private static partial Regex UrlQueryRegex();
}
