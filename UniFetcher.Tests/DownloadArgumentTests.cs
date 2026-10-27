using Nickvision.Parabolic.Shared.Helpers;
using Nickvision.Parabolic.Shared.Models;
using Nickvision.Parabolic.Shared.Services;
using System.Text.RegularExpressions;
using Xunit;

namespace UniFetcher.Tests;

public class DownloadArgumentTests
{
    /// <summary>
    /// yt-dlp format filters can only compare a field with a value ([height<=1080]), never two fields ([width>=height]).
    /// A filter like that makes yt-dlp stop with "Invalid filter specification" and every download fails.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("+bestaudio")]
    [InlineData("+(bestaudio[acodec!=opus]/bestaudio)")]
    public void SmartQualityUsesOnlyValidFilters(string audio)
    {
        var format = YtdlpExecutableService.SmartFormat(audio);
        foreach (Match filter in Regex.Matches(format, @"\[([^\]]+)\]"))
        {
            Assert.Matches(@"^[a-z_]+\s*(!?[<>=]=?|[\^$*~]=)\s*[A-Za-z0-9._-]+\??$", filter.Groups[1].Value);
            Assert.DoesNotMatch(@"(width|height)\s*[<>=!]+\s*(width|height)$", filter.Groups[1].Value);
        }
        Assert.EndsWith("best", format);
    }

    [Fact]
    public void DiagnosticKeepsTracebackErrors()
    {
        var log = string.Join('\n',
            "[debug] yt-dlp version nightly@2026.10.26.220202",
            "Traceback (most recent call last):",
            "  File \"YoutubeDL.py\", line 2259, in _build_format_filter",
            "SyntaxError: Invalid filter specification 'width>=height'");
        var text = Diagnostic.Build("https://www.youtube.com/watch?v=abc", FailureReason.Other, "no", [], log, null);
        Assert.Contains("SyntaxError: Invalid filter specification", text);
        Assert.Contains("Traceback", text);
    }
}
