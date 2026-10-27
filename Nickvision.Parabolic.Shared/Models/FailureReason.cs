using Nickvision.Desktop.Globalization;
using System;
using System.Linq;

namespace Nickvision.Parabolic.Shared.Models;

/// <summary>
/// One plain-language reason for a failed download, worked out from yt-dlp's error text.
/// Same rules as the Android app (util/FailureReason.kt), so both apps say the same thing.
/// </summary>
public enum FailureReason
{
    None = 0,
    /// <summary>yt-dlp can't read this site; cookies won't help.</summary>
    SiteNotSupported,
    /// <summary>CAPTCHA, bot check or rate limit.</summary>
    BlockedBySecurity,
    /// <summary>Cookies were tried (and refreshed) and the site still refused.</summary>
    SessionRejected,
    NeedsLogin,
    /// <summary>Removed, private or region-locked.</summary>
    VideoUnavailable,
    Drm,
    Network,
    /// <summary>Not enough space (or a file too big for the drive).</summary>
    Storage,
    /// <summary>Skipped: already downloaded (neutral, not an error).</summary>
    AlreadyDownloaded,
    Other
}

public static class FailureReasons
{
    private static readonly string[] Drm = ["drm protected", "drm-protected", "is drm", "this video is drm"];

    private static readonly string[] Unavailable =
    [
        "private video", "video unavailable", "video is unavailable", "has been removed", "been terminated", "no longer available",
        "http error 404", "404: not found", "http error 451", "not available in your country", "not available in your region",
        "geo restrict", "geo-restrict", "geoblocked"
    ];

    private static readonly string[] Security =
    [
        "captcha", "not a bot", "are a robot", "verify you are human", "verify that you are human", "cloudflare", "just a moment",
        "bot check", "challenge page", "rate-limit", "too many requests", "http error 429",
        // UniFetcher (Windows) notes
        "check you're a person"
    ];

    private static readonly string[] Login =
    [
        "login required", "log in to", "sign in to view", "requires authentication", "account authentication", "use --username",
        "members-only", "members only", "only available for registered users", "only available to subscribers", "premium members",
        "needs you to be logged in"
    ];

    private static readonly string[] NotSupported =
    [
        "unsupported url", "is not a valid url", "no video could be found", "no media found", "no video formats found",
        "unable to extract", "please report this issue"
    ];

    private static readonly string[] Network =
    [
        "timed out", "timeout", "unable to download webpage", "urlopen error", "connection reset", "connection refused",
        "connection aborted", "network is unreachable", "temporary failure in name resolution", "name or service not known",
        "no address associated", "getaddrinfo", "remote end closed connection", "ssl:", "http error 5"
    ];

    private static readonly string[] Storage =
    [
        "no space left", "not enough space for temporary files", "disk full", "not enough space on the disk", "errno 28",
        "bigger than 4 gb"
    ];

    /// <summary>Errors that a fresh browser session (cookies) might fix: 401, 403, 410 and similar.</summary>
    private static readonly string[] SessionErrors =
    [
        "http error 401", "http error 403", "http error 410", "403: forbidden", "410: gone", "401: unauthorized",
        "use --cookies", "--cookies-from-browser"
    ];

    /// <summary>
    /// True if getting (or refreshing) the site's cookies is worth a try for this error.
    /// Bot checks and logins aren't: they need you.
    /// </summary>
    public static bool IsSessionError(string? message)
    {
        if (message is null)
        {
            return false;
        }
        var m = message.ToLowerInvariant();
        if (Has(m, Security) || Has(m, Login) || Has(m, Unavailable) || Has(m, Drm))
        {
            return false;
        }
        return Has(m, SessionErrors);
    }

    /// <param name="sessionTried">True if cookies were used (and refreshed) and it still failed, so a 401/403/410 means the site rejected the session.</param>
    public static FailureReason Classify(string? message, bool sessionTried = false)
    {
        if (message is null)
        {
            return FailureReason.Other;
        }
        var m = message.ToLowerInvariant();
        // UniFetcher's own note after a refreshed session still failed
        if (m.Contains("may be blocking your vpn"))
        {
            return FailureReason.SessionRejected;
        }
        if (Has(m, Storage))
        {
            return FailureReason.Storage;
        }
        if (Has(m, Drm))
        {
            return FailureReason.Drm;
        }
        if (Has(m, Unavailable))
        {
            return FailureReason.VideoUnavailable;
        }
        if (Has(m, Security))
        {
            return FailureReason.BlockedBySecurity;
        }
        if (Has(m, Login))
        {
            return FailureReason.NeedsLogin;
        }
        if (Has(m, SessionErrors))
        {
            return sessionTried ? FailureReason.SessionRejected : FailureReason.BlockedBySecurity;
        }
        if (Has(m, NotSupported))
        {
            return FailureReason.SiteNotSupported;
        }
        if (Has(m, Network))
        {
            return FailureReason.Network;
        }
        return FailureReason.Other;
    }

    /// <summary>Only the error lines, so words elsewhere in yt-dlp's long log don't confuse the result.</summary>
    public static FailureReason ClassifyLog(string log, bool sessionTried)
    {
        var lines = log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var important = lines.Where(l => l.Contains("ERROR", StringComparison.Ordinal) || l.StartsWith("UniFetcher:", StringComparison.Ordinal) || l.Contains("Errno", StringComparison.Ordinal) || l.Contains("Error:", StringComparison.Ordinal) || l.Contains(": error:", StringComparison.Ordinal) || l.StartsWith("Skipped:", StringComparison.Ordinal)).ToList();
        return Classify(important.Count > 0 ? string.Join('\n', important) : string.Join('\n', lines.TakeLast(5)), sessionTried);
    }

    /// <summary>Needs you (open the site, log in, or try another VPN server), not a plain retry.</summary>
    public static bool NeedsAction(this FailureReason reason) => reason is FailureReason.BlockedBySecurity or FailureReason.NeedsLogin or FailureReason.SessionRejected;

    public static string Label(this FailureReason reason, ITranslationService? t) => reason switch
    {
        FailureReason.SiteNotSupported => T(t, "Site not supported"),
        FailureReason.BlockedBySecurity => T(t, "Blocked by site security"),
        FailureReason.SessionRejected => T(t, "Session rejected"),
        FailureReason.NeedsLogin => T(t, "Needs login"),
        FailureReason.VideoUnavailable => T(t, "Video unavailable"),
        FailureReason.Drm => T(t, "DRM: not supported"),
        FailureReason.Network => T(t, "Network problem"),
        FailureReason.Storage => T(t, "Not enough space"),
        FailureReason.AlreadyDownloaded => T(t, "Already downloaded"),
        _ => T(t, "Download failed")
    };

    public static string Explanation(this FailureReason reason, ITranslationService? t) => reason switch
    {
        FailureReason.SiteNotSupported => T(t, "yt-dlp, the engine UniFetcher uses, can't read this site, so cookies or retrying won't help."),
        FailureReason.BlockedBySecurity => T(t, "The site wants to check you're a person, or is limiting requests. Open it in the UniFetcher browser, complete any check, then retry."),
        FailureReason.SessionRejected => T(t, "UniFetcher made and refreshed a browser session, but the site still refused. It may be blocking your VPN: try another VPN server, then retry."),
        FailureReason.NeedsLogin => T(t, "Log in to the site once in the UniFetcher browser, then retry."),
        FailureReason.VideoUnavailable => T(t, "The video was removed, is private, or isn't available in your country."),
        FailureReason.Drm => T(t, "This video is copy-protected (DRM). UniFetcher doesn't download DRM-protected videos."),
        FailureReason.Network => T(t, "The connection dropped or the site didn't answer. Retry later."),
        FailureReason.Storage => T(t, "The download drive is full, or the video is too big for it (FAT32 drives can't hold files over 4 GB)."),
        FailureReason.AlreadyDownloaded => T(t, "You already have this video."),
        _ => T(t, "Something else went wrong. The log has yt-dlp's message.")
    };

    private static string T(ITranslationService? t, string text) => t?._(text) ?? text;

    private static bool Has(string text, string[] needles) => needles.Any(text.Contains);
}
