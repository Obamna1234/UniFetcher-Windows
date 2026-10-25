using Microsoft.Extensions.Logging;
using Nickvision.Desktop.Application;
using Nickvision.Desktop.Globalization;
using Nickvision.Desktop.Notifications;
using Nickvision.Parabolic.Shared.Helpers;
using Nickvision.Parabolic.Shared.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nickvision.Parabolic.Shared.Services;

public enum SessionProblem
{
    None,
    /// <summary>403 / 410 / "Gone": usually fixed with a real browser session (cookies).</summary>
    Forbidden,
    /// <summary>The site needs you to be logged in.</summary>
    NeedsLogin,
    /// <summary>CAPTCHA / "verify you are human" / bot check.</summary>
    HumanCheck,
    /// <summary>Cookies from a Chromium browser (Brave/Chrome/Edge) couldn't be read.</summary>
    CookiesUnreadable
}

public enum SessionDecision
{
    /// <summary>Not a session problem, or nothing more to try.</summary>
    GiveUp,
    /// <summary>A session was (re)made for the site: retry the download once with cookies.</summary>
    RetryWithCookies,
    /// <summary>Something only you can do (log in, CAPTCHA, change VPN server). The link is "Needs my action".</summary>
    NeedsAction
}

public record SiteSession(string Site, bool NeedsCookies, DateTime LastVisit);

public record SessionOutcome(SessionDecision Decision, string Message);

/// <summary>
/// Automatic per-site sessions (spec 3a / Android 4a): when a site refuses a download (403/410), open it once in the
/// UniFetcher Firefox profile, let it set cookies, and retry with that profile's cookies. Refresh once if they stop working; never loop.
/// Logins and CAPTCHAs are never automated: they become "Needs my action".
/// </summary>
public interface ISiteSessionService
{
    /// <summary>The folder yt-dlp should read cookies from for this link (UniFetcher profile), or null for no cookies.</summary>
    Task<string?> GetCookieProfileAsync(Uri url);

    Task<SessionOutcome> HandleFailureAsync(Uri url, string log, bool usedCookies, CancellationToken cancellationToken = default);

    Task MarkSuccessAsync(Uri url, bool usedCookies);

    Task<IReadOnlyList<SiteSession>> GetSessionsAsync();

    Task ForgetAsync(string site);

    Task ForgetAllAsync();

    Task<bool> OpenInProfileAsync(Uri url);
}

public class SiteSessionService : ISiteSessionService
{
    private const string TableName = "unifetcher_sessions";
    private static readonly TimeSpan PageSettleTime = TimeSpan.FromSeconds(12);
    private static readonly string[] TwoPartSuffixes = ["co.uk", "org.uk", "ac.uk", "gov.uk", "com.au", "net.au", "org.au", "co.nz", "co.jp", "ne.jp", "com.br", "com.mx", "co.in", "co.kr", "com.tr", "com.cn", "com.tw", "com.hk", "co.za"];

    private readonly ILogger<SiteSessionService> _logger;
    private readonly IConfigurationService _configurationService;
    private readonly IDatabaseService _databaseService;
    private readonly IFirefoxProfileService _profile;
    private readonly INotificationService _notificationService;
    private readonly ITranslationService _translationService;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _siteLocks;
    // Per run: sites refreshed once already since the last success (never loop), and sites opened for "needs my action"
    private readonly ConcurrentDictionary<string, bool> _refreshedOnce;
    private readonly ConcurrentDictionary<string, bool> _openedForAction;
    private bool _tableEnsured;
    private bool _warnedNoFirefox;

    public SiteSessionService(ILogger<SiteSessionService> logger, IConfigurationService configurationService, IDatabaseService databaseService, IFirefoxProfileService profile, INotificationService notificationService, ITranslationService translationService)
    {
        _logger = logger;
        _configurationService = configurationService;
        _databaseService = databaseService;
        _profile = profile;
        _notificationService = notificationService;
        _translationService = translationService;
        _siteLocks = new ConcurrentDictionary<string, SemaphoreSlim>();
        _refreshedOnce = new ConcurrentDictionary<string, bool>();
        _openedForAction = new ConcurrentDictionary<string, bool>();
    }

    /// <summary>"www.example.co.uk/a" -> "example.co.uk". Sessions are kept per site and never shared between sites.</summary>
    public static string SiteOf(Uri url)
    {
        var host = url.Host.ToLowerInvariant().TrimEnd('.');
        var labels = host.Split('.');
        if (labels.Length <= 2 || System.Net.IPAddress.TryParse(host, out _))
        {
            return host;
        }
        var lastTwo = $"{labels[^2]}.{labels[^1]}";
        return TwoPartSuffixes.Contains(lastTwo) && labels.Length >= 3 ? $"{labels[^3]}.{lastTwo}" : lastTwo;
    }

    public static SessionProblem Classify(string log)
    {
        if (string.IsNullOrEmpty(log))
        {
            return SessionProblem.None;
        }
        bool Has(params string[] parts) => parts.Any(p => log.Contains(p, StringComparison.OrdinalIgnoreCase));
        if (Has("Failed to decrypt with DPAPI", "Could not copy Chrome cookie database", "app-bound", "Failed to decrypt cookie", "cookie database is locked"))
        {
            return SessionProblem.CookiesUnreadable;
        }
        if (Has("captcha", "verify you are human", "not a bot", "are you a robot", "challenge page", "cf-challenge", "Cloudflare anti-bot"))
        {
            return SessionProblem.HumanCheck;
        }
        if (Has("login required", "Sign in to", "log in to", "logged-in", "requires authentication", "account is required", "only available for registered users", "members-only", "Use --cookies-from-browser or --cookies for the authentication"))
        {
            // A private video can't be fixed by logging in unless it's yours; still worth a look by you
            return SessionProblem.NeedsLogin;
        }
        if (Has("HTTP Error 403", "HTTP Error 410", "403: Forbidden", "410: Gone", "HTTP Error 401"))
        {
            return SessionProblem.Forbidden;
        }
        return SessionProblem.None;
    }

    public async Task<string?> GetCookieProfileAsync(Uri url)
    {
        // Only when the cookie source is the UniFetcher profile (the default); other sources are handled by the existing setting
        if (_configurationService.CookiesBrowser != Browser.None || !_profile.IsFirefoxInstalled)
        {
            return null;
        }
        var session = await GetAsync(SiteOf(url));
        if (session is null || !session.NeedsCookies)
        {
            return null;
        }
        return await _profile.GetCookieSnapshotAsync();
    }

    public async Task<SessionOutcome> HandleFailureAsync(Uri url, string log, bool usedCookies, CancellationToken cancellationToken = default)
    {
        var problem = Classify(log);
        var site = SiteOf(url);
        switch (problem)
        {
            case SessionProblem.None:
                return new SessionOutcome(SessionDecision.GiveUp, string.Empty);
            case SessionProblem.CookiesUnreadable:
                return new SessionOutcome(SessionDecision.NeedsAction, _translationService._("UniFetcher couldn't read cookies from the selected browser (newer Brave/Chrome/Edge versions lock them). In Settings → Downloader, set \"Cookies from Browser\" to the UniFetcher profile, then press Retry."));
            case SessionProblem.NeedsLogin:
            case SessionProblem.HumanCheck:
                return await NeedsActionAsync(url, site, problem);
        }
        // 403 / 410 / 401
        if (_configurationService.CookiesBrowser != Browser.None)
        {
            return new SessionOutcome(SessionDecision.GiveUp, string.Empty);
        }
        if (!_profile.IsFirefoxInstalled)
        {
            if (!_warnedNoFirefox)
            {
                _warnedNoFirefox = true;
                _notificationService.Send(new AppNotification(_translationService._("Some sites only work with a browser session. That needs Firefox (UniFetcher uses its own separate Firefox profile). Install Firefox to enable it; until then, downloads run without cookies."), NotificationSeverity.Warning));
            }
            return new SessionOutcome(SessionDecision.GiveUp, string.Empty);
        }
        var gate = _siteLocks.GetOrAdd(site, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var session = await GetAsync(site);
            var recentlyVisited = session is not null && DateTime.Now - session.LastVisit < TimeSpan.FromMinutes(2);
            if (!usedCookies)
            {
                // First time: one visit per site, shared by every link from that site
                if (!recentlyVisited)
                {
                    _logger.LogInformation($"{site} refused the download; getting a session in the UniFetcher profile.");
                    await VisitAsync(url, cancellationToken);
                }
                await SaveAsync(site, true);
                return new SessionOutcome(SessionDecision.RetryWithCookies, _translationService._("Getting a session for {0} and trying again...", site));
            }
            if (recentlyVisited)
            {
                // Another download from this site just refreshed it
                return new SessionOutcome(SessionDecision.RetryWithCookies, string.Empty);
            }
            if (_refreshedOnce.TryAdd(site, true))
            {
                // Cookies stopped working (e.g. VPN server changed): refresh once
                _logger.LogInformation($"{site} refused the saved session; refreshing it once.");
                await VisitAsync(url, cancellationToken);
                await SaveAsync(site, true);
                return new SessionOutcome(SessionDecision.RetryWithCookies, _translationService._("Refreshing the session for {0} and trying again...", site));
            }
            // Already refreshed: don't loop
            return new SessionOutcome(SessionDecision.NeedsAction, _translationService._("This site may be blocking your VPN. Try another VPN server, then press Retry."));
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task MarkSuccessAsync(Uri url, bool usedCookies)
    {
        if (usedCookies)
        {
            _refreshedOnce.TryRemove(SiteOf(url), out _);
        }
        await Task.CompletedTask;
    }

    public async Task<IReadOnlyList<SiteSession>> GetSessionsAsync()
    {
        await EnsureTableAsync();
        var list = new List<SiteSession>();
        using var command = await _databaseService.SelectAllFromTableAsync(TableName);
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new SiteSession(reader.GetString(0), reader.GetInt64(1) != 0, DateTime.Parse(reader.GetString(2))));
        }
        list.Sort((a, b) => string.CompareOrdinal(a.Site, b.Site));
        return list;
    }

    public async Task ForgetAsync(string site)
    {
        await EnsureTableAsync();
        await _databaseService.DeleteFromTableAsync(TableName, "site", site);
        _refreshedOnce.TryRemove(site, out _);
        _openedForAction.TryRemove(site, out _);
        await _profile.ForgetSiteAsync(site);
    }

    public async Task ForgetAllAsync()
    {
        foreach (var session in await GetSessionsAsync())
        {
            await ForgetAsync(session.Site);
        }
    }

    public Task<bool> OpenInProfileAsync(Uri url) => _profile.OpenAsync(url);

    private async Task<SessionOutcome> NeedsActionAsync(Uri url, string site, SessionProblem problem)
    {
        var message = problem == SessionProblem.HumanCheck
            ? _translationService._("{0} wants to check you're a person (or may be blocking your VPN). Complete the check in the UniFetcher Firefox window (or try another VPN server), then press Retry.", site)
            : _translationService._("{0} needs you to be logged in. Log in once in the UniFetcher Firefox window, then press Retry. Your login stays in that profile only.", site);
        if (_configurationService.CookiesBrowser == Browser.None && _profile.IsFirefoxInstalled)
        {
            // Remember that this site needs cookies, so the retry uses the session you create
            await SaveAsync(site, true);
            if (_openedForAction.TryAdd(site, true))
            {
                await _profile.OpenAsync(url);
            }
        }
        return new SessionOutcome(SessionDecision.NeedsAction, message);
    }

    private async Task VisitAsync(Uri url, CancellationToken cancellationToken)
    {
        // Open the site's page itself (not just the home page), so the site sets the cookies it wants for videos
        if (await _profile.OpenAsync(url))
        {
            await Task.Delay(PageSettleTime, cancellationToken);
        }
        _profile.InvalidateSnapshot();
    }

    private async Task<SiteSession?> GetAsync(string site)
    {
        await EnsureTableAsync();
        using var command = await _databaseService.SelectFromTableAsync(TableName, "site", site);
        using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? new SiteSession(reader.GetString(0), reader.GetInt64(1) != 0, DateTime.Parse(reader.GetString(2))) : null;
    }

    private async Task SaveAsync(string site, bool needsCookies)
    {
        await EnsureTableAsync();
        await _databaseService.ReplaceIntoTableAsync(TableName, new Dictionary<string, object>
        {
            { "site", site },
            { "needsCookies", needsCookies ? 1 : 0 },
            { "lastVisit", DateTime.Now.ToString("o") }
        });
    }

    private async Task EnsureTableAsync()
    {
        if (_tableEnsured)
        {
            return;
        }
        await _databaseService.EnsureTableExistsAsync(TableName, "site TEXT PRIMARY KEY, needsCookies INTEGER, lastVisit TEXT");
        _tableEnsured = true;
    }
}
