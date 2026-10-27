using Nickvision.Parabolic.Shared.Helpers;
using Nickvision.Parabolic.Shared.Models;
using Xunit;

namespace UniFetcher.Tests;

/// <summary>Same cases as the Android app's FailureReasonTest, so both apps give the same reasons.</summary>
public class FailureReasonTests
{
    [Fact]
    public void SessionErrorsTriggerAnAutomaticSession()
    {
        Assert.True(FailureReasons.IsSessionError("ERROR: unable to download video data: HTTP Error 403: Forbidden"));
        Assert.True(FailureReasons.IsSessionError("WARNING: [site] abc: failed to download m3u8 information: HTTP Error 410: Gone"));
        Assert.True(FailureReasons.IsSessionError("ERROR: This video requires cookies. Use --cookies-from-browser or --cookies"));
    }

    [Fact]
    public void BotChecksLoginsAndRemovedVideosDoNotTriggerASession()
    {
        Assert.False(FailureReasons.IsSessionError("ERROR: Sign in to confirm you're not a bot. HTTP Error 403"));
        Assert.False(FailureReasons.IsSessionError("ERROR: Login required to view this video. HTTP Error 403"));
        Assert.False(FailureReasons.IsSessionError("ERROR: Video unavailable. This video has been removed"));
        Assert.False(FailureReasons.IsSessionError("ERROR: Unable to download webpage: timed out"));
        Assert.False(FailureReasons.IsSessionError(null));
    }

    [Fact]
    public void ClassifiesCommonErrors()
    {
        Assert.Equal(FailureReason.SiteNotSupported, FailureReasons.Classify("ERROR: Unsupported URL: https://example.com/x"));
        Assert.Equal(FailureReason.BlockedBySecurity, FailureReasons.Classify("ERROR: Got a CAPTCHA page"));
        Assert.Equal(FailureReason.NeedsLogin, FailureReasons.Classify("ERROR: This video is only available for registered users"));
        Assert.Equal(FailureReason.VideoUnavailable, FailureReasons.Classify("ERROR: HTTP Error 404: Not Found"));
        Assert.Equal(FailureReason.VideoUnavailable, FailureReasons.Classify("ERROR: Private video. Sign in if you've been granted access"));
        Assert.Equal(FailureReason.Drm, FailureReasons.Classify("ERROR: This video is DRM protected"));
        Assert.Equal(FailureReason.Network, FailureReasons.Classify("ERROR: Unable to download webpage: <urlopen error timed out>"));
        Assert.Equal(FailureReason.Storage, FailureReasons.Classify("OSError: [Errno 28] No space left on device"));
        Assert.Equal(FailureReason.Other, FailureReasons.Classify("something unexpected"));
    }

    [Fact]
    public void ForbiddenAfterTryingASessionMeansSessionRejected()
    {
        const string error = "ERROR: unable to download video data: HTTP Error 403: Forbidden";
        Assert.Equal(FailureReason.SessionRejected, FailureReasons.Classify(error, sessionTried: true));
        Assert.Equal(FailureReason.BlockedBySecurity, FailureReasons.Classify(error, sessionTried: false));
    }

    [Fact]
    public void WindowsNotesAreUnderstood()
    {
        Assert.Equal(FailureReason.SessionRejected, FailureReasons.Classify("UniFetcher: This site may be blocking your VPN. Try another VPN server, then press Retry."));
        Assert.Equal(FailureReason.Storage, FailureReasons.Classify("Skipped: this video is bigger than 4 GB, and the download drive (FAT32) can't store files that big."));
        Assert.True(FailureReason.NeedsLogin.NeedsAction());
        Assert.False(FailureReason.SiteNotSupported.NeedsAction());
    }

    [Fact]
    public void LogClassificationLooksAtErrorLinesOnly()
    {
        // "timeout" appears in a debug line, but the real error is an unsupported site
        const string log = "[debug] socket timeout 20\n[generic] Extracting URL\nERROR: Unsupported URL: https://example.com/series-details/";
        Assert.Equal(FailureReason.SiteNotSupported, FailureReasons.ClassifyLog(log, false));
    }

    [Fact]
    public void DiagnosticRemovesPrivateDetails()
    {
        var text = Diagnostic.Clean("ERROR: https://cdn.example.com/v.mp4?token=SECRET123&sig=abc Cookie: session=XYZ --password hunter2");
        Assert.DoesNotContain("SECRET123", text);
        Assert.DoesNotContain("XYZ", text);
        Assert.DoesNotContain("hunter2", text);
        Assert.Contains("https://cdn.example.com/v.mp4", text);
    }
}
