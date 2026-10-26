using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Nickvision.Parabolic.Shared.Helpers;

/// <summary>
/// UniFetcher: pulls readable text (and the links in it) out of documents, so a list of links saved in a file
/// can go straight into Bulk Import: PDF, Word (.docx/.doc), OpenDocument (.odt), RTF, e-mails (.eml/.mht),
/// EPUB, Excel/PowerPoint, Samsung Notes (.sdocx, best effort), web shortcuts and any text-based file.
/// </summary>
public static partial class DocumentTextExtractor
{
    private const long MaxFileBytes = 300L * 1024 * 1024;
    private const long MaxEntryBytes = 64L * 1024 * 1024;

    private static readonly HashSet<string> PlainTextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".text", ".md", ".markdown", ".csv", ".tsv", ".json", ".html", ".htm", ".xml", ".url", ".webloc", ".log", ".ini", ".list"
    };

    /// <returns>The text to search for links, a friendly name for the format, and whether links could only be found by a best-effort scan.</returns>
    public static async Task<(string Text, string Format, bool BestEffort)> ExtractAsync(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("The file doesn't exist any more.", path);
        }
        if (info.Length > MaxFileBytes)
        {
            throw new InvalidDataException("That file is too big to read for links (over 300 MB).");
        }
        var extension = info.Extension.ToLowerInvariant();
        var bytes = await File.ReadAllBytesAsync(path);
        if (StartsWith(bytes, "%PDF"))
        {
            return (ExtractPdf(path, bytes), "PDF document", false);
        }
        if (bytes.Length > 4 && bytes[0] == 'P' && bytes[1] == 'K' && bytes[2] == 3 && bytes[3] == 4)
        {
            var format = extension switch
            {
                ".docx" or ".docm" or ".dotx" => "Word document",
                ".odt" or ".ott" => "OpenDocument text",
                ".xlsx" or ".xlsm" or ".ods" => "Spreadsheet",
                ".pptx" or ".odp" => "Presentation",
                ".epub" => "EPUB e-book",
                ".sdocx" or ".sdoc" or ".snb" => "Samsung Notes file",
                ".zip" => "Zip archive",
                _ => "Document"
            };
            return (ExtractZip(bytes), format, extension is ".sdocx" or ".sdoc" or ".snb");
        }
        if (StartsWith(bytes, "{\\rtf"))
        {
            return (ExtractRtf(DecodeText(bytes)), "Rich text (RTF)", false);
        }
        if (extension is ".eml" or ".mht" or ".mhtml")
        {
            return (ExtractEmail(DecodeText(bytes)), extension == ".eml" ? "E-mail" : "Saved web page", false);
        }
        if (PlainTextExtensions.Contains(extension) || LooksLikeText(bytes))
        {
            // Plain/structured text is handled by LinkImportParser, which also detects bookmarks HTML, JSON and CSV
            return (DecodeText(bytes), string.Empty, false);
        }
        // Old Word (.doc), Outlook (.msg) and other binary files: find text runs that look like links
        return (ScanBinary(bytes), extension switch
        {
            ".doc" => "Word 97–2003 document",
            ".msg" => "Outlook message",
            _ => "File"
        }, true);
    }

    // ---------------- PDF ----------------

    private static string ExtractPdf(string path, byte[] bytes)
    {
        var builder = new StringBuilder();
        try
        {
            // The words on each page (handles the fonts PDFs use, so typed links are found)
            using var document = UglyToad.PdfPig.PdfDocument.Open(path);
            foreach (var page in document.GetPages())
            {
                var words = page.GetWords().ToList();
                if (words.Count == 0)
                {
                    continue;
                }
                // Group words into lines by their height on the page
                var lines = new List<List<UglyToad.PdfPig.Content.Word>>();
                foreach (var word in words)
                {
                    var last = lines.Count > 0 ? lines[^1] : null;
                    var lineHeight = Math.Max(1.0, word.BoundingBox.Height);
                    if (last is null || Math.Abs(last[^1].BoundingBox.Bottom - word.BoundingBox.Bottom) > lineHeight * 0.5)
                    {
                        lines.Add(new List<UglyToad.PdfPig.Content.Word> { word });
                    }
                    else
                    {
                        last.Add(word);
                    }
                }
                var rightEdge = words.Max(w => w.BoundingBox.Right);
                var leftEdge = words.Min(w => w.BoundingBox.Left);
                var tolerance = Math.Max(6.0, (rightEdge - leftEdge) * 0.04);
                for (var i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    for (var j = 0; j < line.Count; j++)
                    {
                        builder.Append(line[j].Text);
                        var isLastWord = j == line.Count - 1;
                        if (!isLastWord)
                        {
                            builder.Append(' ');
                            continue;
                        }
                        // A long link that reaches the right edge of the page was wrapped onto the next line by the
                        // exporter (e.g. Samsung Notes → PDF): join the pieces back together. A new numbered item
                        // ("2.", "3)") or a new link on the next line means it wasn't wrapped.
                        var next = i + 1 < lines.Count ? lines[i + 1][0].Text : null;
                        var wrapped = next is not null
                            && LinkRegex().IsMatch(line[j].Text)
                            && line[j].BoundingBox.Right >= rightEdge - tolerance
                            && !NumberedItemRegex().IsMatch(next)
                            && !next.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                            && LinkContinuationRegex().IsMatch(next);
                        builder.Append(wrapped ? string.Empty : "\n");
                    }
                }
                builder.AppendLine();
            }
        }
        catch (Exception)
        {
            // Damaged or unusual PDF: the scan below still finds clickable links
        }
        // Clickable links (/URI annotations), also inside compressed parts of the file
        builder.AppendLine(ScanPdfRaw(bytes));
        return builder.ToString();
    }

    private static string ScanPdfRaw(byte[] bytes)
    {
        var builder = new StringBuilder();
        var latin = Encoding.Latin1.GetString(bytes);
        AppendPdfUris(latin, builder);
        foreach (Match match in PdfStreamRegex().Matches(latin))
        {
            var start = match.Index + match.Length;
            var end = latin.IndexOf("endstream", start, StringComparison.Ordinal);
            if (end <= start || end - start > MaxEntryBytes)
            {
                continue;
            }
            try
            {
                using var input = new MemoryStream(bytes, start, end - start);
                using var zlib = new ZLibStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                zlib.CopyTo(output);
                AppendPdfUris(Encoding.Latin1.GetString(output.ToArray()), builder);
            }
            catch
            {
                // not a compressed stream (or not one we can read)
            }
        }
        return builder.ToString();
    }

    private static void AppendPdfUris(string text, StringBuilder builder)
    {
        foreach (Match match in PdfUriRegex().Matches(text))
        {
            builder.AppendLine(match.Groups[1].Value.Replace("\\(", "(").Replace("\\)", ")").Replace("\\\\", "\\"));
        }
    }

    // ---------------- Zip-based documents ----------------

    private static string ExtractZip(byte[] bytes)
    {
        var builder = new StringBuilder();
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            if (entry.Length == 0 || entry.Length > MaxEntryBytes || entry.FullName.EndsWith('/'))
            {
                continue;
            }
            var name = entry.FullName.ToLowerInvariant();
            // Pictures and fonts inside documents never hold the link text
            if (name.EndsWith(".png") || name.EndsWith(".jpg") || name.EndsWith(".jpeg") || name.EndsWith(".gif") || name.EndsWith(".webp") || name.EndsWith(".ttf") || name.EndsWith(".otf") || name.EndsWith(".wmf") || name.EndsWith(".emf"))
            {
                continue;
            }
            byte[] data;
            try
            {
                using var entryStream = entry.Open();
                using var memory = new MemoryStream();
                entryStream.CopyTo(memory);
                data = memory.ToArray();
            }
            catch
            {
                continue; // encrypted or damaged part
            }
            if (name.EndsWith(".xml") || name.EndsWith(".rels") || name.EndsWith(".xhtml") || name.EndsWith(".html") || name.EndsWith(".htm") || name.EndsWith(".opf") || name.EndsWith(".ncx") || name.EndsWith(".json") || name.EndsWith(".txt"))
            {
                var text = DecodeText(data);
                // Link targets in attributes (e.g. Word's hyperlink list, href="..."). Text in between tags is NOT scanned raw,
                // because Word splits typed links into pieces; XmlToText below joins them back up.
                foreach (Match attribute in LinkAttributeRegex().Matches(text))
                {
                    builder.AppendLine(WebUtility.HtmlDecode(attribute.Groups[1].Value));
                }
                // The visible text, with paragraphs kept apart and the pieces of one line joined back together
                builder.AppendLine(XmlToText(text));
            }
            else if (name.EndsWith(".zip") || name.EndsWith(".sdocx") || name.EndsWith(".page"))
            {
                try
                {
                    builder.AppendLine(ExtractZip(data));
                }
                catch
                {
                    builder.AppendLine(ScanBinary(data));
                }
            }
            else
            {
                // Unknown inner files (e.g. Samsung Notes' own note data): look for link-like text
                builder.AppendLine(ScanBinary(data));
            }
        }
        return builder.ToString();
    }

    private static string XmlToText(string xml)
    {
        var text = ParagraphEndRegex().Replace(xml, "\n");
        text = TagRegex().Replace(text, string.Empty);
        return WebUtility.HtmlDecode(text);
    }

    // ---------------- RTF ----------------

    private static string ExtractRtf(string rtf)
    {
        var builder = new StringBuilder();
        // HYPERLINK "https://..." fields keep the real link even when the visible text differs
        builder.AppendLine(rtf);
        var text = rtf.Replace("\\par", "\n").Replace("\\line", "\n");
        text = RtfControlRegex().Replace(text, string.Empty);
        text = text.Replace("{", string.Empty).Replace("}", string.Empty);
        builder.AppendLine(text);
        return builder.ToString();
    }

    // ---------------- E-mail / saved web page ----------------

    private static string ExtractEmail(string message)
    {
        var builder = new StringBuilder();
        // Quoted-printable: undo the "=" line breaks that cut long links in two, and "=3D"
        var unfolded = message.Replace("=\r\n", string.Empty).Replace("=\n", string.Empty).Replace("=3D", "=").Replace("=3d", "=");
        builder.AppendLine(unfolded);
        // Base64 parts (many mail apps send the text this way)
        foreach (Match match in Base64PartRegex().Matches(message))
        {
            try
            {
                var decoded = Convert.FromBase64String(Regex.Replace(match.Groups[1].Value, @"\s+", string.Empty));
                builder.AppendLine(DecodeText(decoded));
            }
            catch
            {
                // attachment or picture
            }
        }
        return builder.ToString();
    }

    // ---------------- Binary scan ----------------

    /// <summary>Finds web links stored as plain text inside any file, in both common text encodings (UTF-8 and UTF-16).</summary>
    private static string ScanBinary(byte[] bytes)
    {
        var builder = new StringBuilder();
        foreach (var encoding in new Encoding[] { Encoding.UTF8, Encoding.Unicode, Encoding.BigEndianUnicode })
        {
            var text = encoding.GetString(bytes);
            foreach (Match match in LinkRegex().Matches(text))
            {
                builder.AppendLine(match.Value);
            }
        }
        return builder.ToString();
    }

    // ---------------- Helpers ----------------

    private static string DecodeText(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        // UTF-16 without a byte order mark (common in Windows text files): every other byte is zero
        if (bytes.Length > 8 && bytes.Take(200).Where((_, i) => i % 2 == 1).Count(b => b == 0) > 40)
        {
            text = Encoding.Unicode.GetString(bytes);
        }
        return text;
    }

    private static bool LooksLikeText(byte[] bytes)
    {
        var sample = bytes.Take(4096).ToArray();
        if (sample.Length == 0)
        {
            return true;
        }
        var zeros = sample.Count(b => b == 0);
        var control = sample.Count(b => b < 9 || (b > 13 && b < 32));
        // UTF-16 text has many zeros but in a regular pattern
        var utf16 = sample.Where((_, i) => i % 2 == 1).Count(b => b == 0) > sample.Length / 3;
        return utf16 || (zeros == 0 && control < sample.Length / 20);
    }

    private static bool StartsWith(byte[] bytes, string prefix)
    {
        // Allow a little junk before the header (some PDFs have it)
        var head = Encoding.Latin1.GetString(bytes, 0, Math.Min(bytes.Length, 1024));
        var index = head.IndexOf(prefix, StringComparison.Ordinal);
        return index >= 0 && (prefix == "%PDF" ? true : index == 0);
    }

    [GeneratedRegex(@"stream\r?\n")]
    private static partial Regex PdfStreamRegex();

    [GeneratedRegex(@"/URI\s*\((https?://(?:\\\)|\\\(|[^)])*)\)", RegexOptions.IgnoreCase)]
    private static partial Regex PdfUriRegex();

    [GeneratedRegex(@"</(?:w:p|a:p|text:p|text:h|p|div|li|tr|h[1-6]|para|paragraph)>|<(?:br|w:br|w:cr|text:line-break|text:tab|w:tab)\b[^>]*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex ParagraphEndRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();

    // "2." / "2)" / "2" at the start of a line: a new list item, as in tabs shared into Samsung Notes
    [GeneratedRegex(@"^\d{1,5}[.)]")]
    private static partial Regex NumberedItemRegex();

    // Only characters that can appear inside a link
    [GeneratedRegex(@"^[A-Za-z0-9\-._~:/?#\[\]@!$&'()*+,;=%]+$")]
    private static partial Regex LinkContinuationRegex();

    [GeneratedRegex(@"(?:href|Target|xlink:href|url|src)\s*=\s*[""'](https?://[^""']+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex LinkAttributeRegex();

    [GeneratedRegex(@"\\[a-z]+-?\d* ?|\\'[0-9a-f]{2}", RegexOptions.IgnoreCase)]
    private static partial Regex RtfControlRegex();

    [GeneratedRegex(@"Content-Transfer-Encoding:\s*base64\s*\r?\n(?:[^\r\n]+\r?\n)*?\r?\n([A-Za-z0-9+/=\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Base64PartRegex();

    [GeneratedRegex(@"https?://[A-Za-z0-9\-._~:/?#\[\]@!$&'()*+,;=%]{4,2000}", RegexOptions.IgnoreCase)]
    private static partial Regex LinkRegex();
}
