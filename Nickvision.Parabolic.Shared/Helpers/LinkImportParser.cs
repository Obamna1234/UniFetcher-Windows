using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nickvision.Parabolic.Shared.Helpers;

public record ImportedLink(Uri Url, string Title);

public record ParsedLinks(string Format, IReadOnlyList<ImportedLink> Links, IReadOnlyList<string> NotWebPages);

/// <summary>
/// Reads lists of links in many formats, detecting the format by itself (spec section 5 / Android 4a):
/// plain text (one per line or mixed into text, e.g. Firefox "Share all tabs"), bookmarks HTML (Firefox, Brave, Chrome,
/// Edge, Samsung Internet), Firefox bookmark backup JSON, tab-manager exports (JSON, CSV, OneTab-style "url | title").
/// </summary>
public static partial class LinkImportParser
{
    public static ParsedLinks Parse(string text, string? fileName = null)
    {
        text ??= string.Empty;
        // Numbered lists ("1. https://...", "2) https://...", as when sharing all tabs into Samsung Notes) need nothing special:
        // links are found anywhere in a line. Links typed with backslashes ("https:\\example.com\...") are fixed to normal slashes.
        text = BackslashLinkRegex().Replace(text, m => m.Groups[1].Value + "://" + m.Groups[2].Value.Replace('\\', '/'));
        var trimmed = text.TrimStart('﻿', ' ', '\t', '\r', '\n');
        var extension = System.IO.Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        var links = new List<ImportedLink>();
        var notWeb = new List<string>();
        string format;
        if ((trimmed.StartsWith('{') || trimmed.StartsWith('[')) && TryParseJson(trimmed, links, notWeb, out format))
        {
            // done
        }
        else if (extension is ".html" or ".htm" || trimmed.StartsWith("<!DOCTYPE NETSCAPE-Bookmark-file", StringComparison.OrdinalIgnoreCase) || AnchorRegex().IsMatch(trimmed))
        {
            format = "Bookmarks HTML";
            ParseHtml(trimmed, links, notWeb);
        }
        else if (extension == ".csv" || LooksLikeCsv(trimmed))
        {
            format = "CSV";
            ParseText(trimmed, links, notWeb);
        }
        else if (OneTabRegex().IsMatch(trimmed))
        {
            format = "OneTab-style list";
            ParseText(trimmed, links, notWeb);
        }
        else
        {
            format = "Text";
            ParseText(trimmed, links, notWeb);
        }
        return new ParsedLinks(format, links, notWeb.Distinct().ToList());
    }

    private static bool TryParseJson(string text, List<ImportedLink> links, List<string> notWeb, out string format)
    {
        format = "JSON";
        try
        {
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("children", out _) && (root.TryGetProperty("guid", out _) || root.TryGetProperty("root", out _)))
            {
                format = "Firefox bookmark backup";
            }
            else
            {
                format = "Tab-manager export (JSON)";
            }
            Walk(root, string.Empty, links, notWeb, 0);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Collects every "url" / "uri" / "href" / "link" value anywhere in the JSON, with a nearby "title" / "name".</summary>
    private static void Walk(JsonElement element, string title, List<ImportedLink> links, List<string> notWeb, int depth)
    {
        if (depth > 64)
        {
            return;
        }
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var localTitle = title;
                foreach (var name in new[] { "title", "name", "label" })
                {
                    if (element.TryGetProperty(name, out var t) && t.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(t.GetString()))
                    {
                        localTitle = t.GetString()!;
                        break;
                    }
                }
                foreach (var property in element.EnumerateObject())
                {
                    var key = property.Name.ToLowerInvariant();
                    if (property.Value.ValueKind == JsonValueKind.String && key is "url" or "uri" or "href" or "link" or "taburl" or "pageurl")
                    {
                        Add(property.Value.GetString()!, localTitle, links, notWeb);
                    }
                    else if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        Walk(property.Value, localTitle, links, notWeb, depth + 1);
                    }
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        Add(item.GetString()!, title, links, notWeb);
                    }
                    else
                    {
                        Walk(item, title, links, notWeb, depth + 1);
                    }
                }
                break;
        }
    }

    private static void ParseHtml(string html, List<ImportedLink> links, List<string> notWeb)
    {
        foreach (Match match in AnchorRegex().Matches(html))
        {
            var url = WebUtility.HtmlDecode(match.Groups["url"].Value);
            var title = WebUtility.HtmlDecode(TagRegex().Replace(match.Groups["title"].Value, string.Empty)).Trim();
            Add(url, title, links, notWeb);
        }
    }

    private static void ParseText(string text, List<ImportedLink> links, List<string> notWeb)
    {
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }
            var found = false;
            foreach (Match match in UrlRegex().Matches(line))
            {
                found = true;
                var url = match.Value.TrimEnd('.', ',', ';', ')', ']', '}', '"', '\'', '>');
                // OneTab: "url | title"; CSV: other columns
                var rest = line[(match.Index + match.Length)..].Trim().TrimStart('|', ',', ';', '\t').Trim().Trim('"');
                var title = rest.Length > 0 && !rest.Contains("://") ? rest : string.Empty;
                Add(url, title, links, notWeb);
            }
            if (!found)
            {
                foreach (Match match in OtherSchemeRegex().Matches(line))
                {
                    notWeb.Add(match.Value);
                }
            }
        }
    }

    private static void Add(string value, string title, List<ImportedLink> links, List<string> notWeb)
    {
        value = value.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return;
        }
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && !string.IsNullOrEmpty(uri.Host) && (uri.Host.Contains('.') || uri.Host == "localhost"))
        {
            links.Add(new ImportedLink(uri, title));
        }
        else if (value.Contains(':'))
        {
            // about:, file:, chrome:, moz-extension:, javascript:, place: ... clearly not web pages
            notWeb.Add(value.Length > 200 ? value[..200] : value);
        }
    }

    private static bool LooksLikeCsv(string text)
    {
        var lines = text.Split('\n', 6, StringSplitOptions.RemoveEmptyEntries);
        return lines.Length >= 2 && lines.Take(5).All(l => l.Contains(',')) && lines[0].Contains("url", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"<a\s[^>]*?href\s*=\s*[""'](?<url>[^""']+)[""'][^>]*>(?<title>.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AnchorRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();

    // "https:\\host\path" -> group 1 = scheme, group 2 = the rest of the link
    [GeneratedRegex(@"\b(https?):\\\\([^\s<>""'|]+)", RegexOptions.IgnoreCase)]
    private static partial Regex BackslashLinkRegex();

    [GeneratedRegex(@"https?://[^\s<>""'|]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"^https?://\S+\s+\|\s+.+$", RegexOptions.Multiline)]
    private static partial Regex OneTabRegex();

    [GeneratedRegex(@"\b(?:about|file|chrome|edge|brave|moz-extension|chrome-extension|javascript|data|place|view-source|resource):[^\s|,]*", RegexOptions.IgnoreCase)]
    private static partial Regex OtherSchemeRegex();
}
