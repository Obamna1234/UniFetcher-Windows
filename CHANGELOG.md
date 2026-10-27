# Changelog

## Unreleased
### Fixed: downloads failing with "Something else went wrong"
- The Smart quality rule used a format filter yt-dlp doesn't accept, so every Smart-quality download stopped before starting (YouTube included). The rule now uses yt-dlp's own resolution sorting (best up to 1080p, portrait videos too).
- Copy diagnostic now includes the real error line (tracebacks and "error:" lines) and always the last lines of the log.
- New: File → Show Downloads Folder, and File → Show Last Download (Ctrl+L).
- "Use Authentication" now has a tooltip explaining it's optional and separate from cookies.

### Fewer antivirus false alarms
- No longer reads other programs' details to find its Firefox windows; it only closes windows it opened itself.
- No longer ends a stuck copy of itself; it explains how to do that in Task Manager.
- No unifetcher:// link handler is registered any more.
- The browser-extension helper is only registered with your browsers if you switch on Settings → Downloader → Browser extension connection (off by default); entries from earlier test builds are removed.
- The program now carries full version details; test builds are numbered 0.0.N-dev.

### Clearer failures and Action needed (from the Android app)
- One plain-language reason on every failed download and failed lookup ("Site not supported", "Needs login", …), with a 🍪 when cookies were used.
- Copy diagnostic on failed downloads (private details removed).
- Colour borders (orange / red / grey) and Help → What the Colours Mean.
- Action needed view grouped by site with Open and Done, an orange banner, one grouped notification; kept after closing UniFetcher.
- Downloads menu: Resume Stopped, Clear Failed and Stopped; Retry All Failed skips the ones that need you. New filters: Action needed, Stopped.
- Settings: Skip videos already downloaded (on), Keep free space (2 GB / 1 GB / 500 MB / Off).
- If moving a finished video fails, it's kept in the temporary folder and you're told where.
- Unit tests for the failure reasons and link import run in every build.

### Fixes from first test on a real PC
- Installer no longer asks for administrator rights: it installs just for you.
- Starting UniFetcher while it's open brings the window forward; a copy stuck in the background can be closed with one click; start-up errors show a message and are saved to crash.log; closing the window always ends the program.
- Sharp logo inside the app (title bar, home page, About).
- "Select Batch File" is now "Import links" (opens Bulk Import) with an ⓘ button listing the supported files.
- Settings regrouped: yt-dlp and ffmpeg updates moved to Downloader, Prevent Suspend to Downloads, and a new Privacy page (history length, incognito, clear history).
- Every build now installs and starts UniFetcher on GitHub's test PC, closes it and opens it again.

### Documents and temporary files
- Bulk Import reads links from documents: PDF, Word (.docx/.doc), OpenDocument (.odt), RTF, e-mails (.eml), saved web pages (.mht), EPUB, Excel/PowerPoint, .url shortcuts and Samsung Notes (.sdocx, best effort). Works with Import from file and drag-and-drop.
- Numbered link lists ("1. https://…"), links typed with backslashes, and long links wrapped onto the next line in PDFs are all read correctly.
- New setting "Temporary files folder" (default: next to the download). The queue pauses if that folder's drive is missing or full.

### Organising and privacy (stage 7)
- Downloads Library (View menu): list or thumbnail grid, search, sort by date/size/duration/name/site, source site and original link, Open file, Show in folder, Copy link, Download again, Remove (file kept unless "Also delete file" is ticked).
- Optional subfolder per site.
- Incognito mode: no download history kept while it's on.
- Windows notifications: titles, discreet (generic text), or off; one notification per Bulk Import.
- Clear history and cache (optionally also the UniFetcher browser profile).

### Bulk Import (stage 6)
- File → Bulk Import (Ctrl+B): paste, import a file, drag a file onto the window, or "Send all tabs" from the extension.
- Reads plain/mixed text, bookmarks HTML, Firefox bookmark backup JSON, tab-manager JSON/CSV and OneTab lists.
- Summary before starting: totals, per-site tick boxes, duplicates in the list and already-downloaded videos (skipped), non-web links, free space; playlists/channels listed separately to expand on request (with a warning).
- Uses the "Smart: best up to 1080p" quality rule and the normal queue. Progress is saved and resumes after closing or restarting; re-importing only picks up what's left.
- Errors are sorted: temporary ones retried 3 times with growing waits, extractor errors update yt-dlp and retry once, permanent ones marked Unavailable, pages without video marked No video found, logins/CAPTCHAs marked Needs my action.
- End report with Retry failed, Export list of failed links and Copy report.

### Browser extension (stage 5)
- One extension for Firefox and Brave/Chrome/Edge: send this page, right-click "Download with UniFetcher", Alt+Shift+U, "Send all tabs" (this window / all windows) to Bulk Import.
- Video detection: the toolbar button shows how many video files/streams (MP4, WebM, HLS, DASH) the page loaded; pick one to send.
- Talks to UniFetcher through native messaging (a small helper next to UniFetcher.exe, registered per user). No internet, no data collection; starts UniFetcher if it's closed.
- Releases sign the Firefox version for self-distribution when the Mozilla API key is set as GitHub secrets, and add it to the UniFetcher Firefox profile automatically.

### Browser sessions and VPN (stage 4)
- New separate "UniFetcher" Firefox profile (created automatically, listed in about:profiles). Your everyday browser is never touched.
- Downloads and fetches refused with 403/410 get a session from that profile (one visit per site) and retry; cookies are refreshed once if they stop working; never loops.
- Logins and CAPTCHAs are never automated: "needs your action" with an Open in browser button, then Retry.
- Repeated refusals after a refresh suggest the site may be blocking your VPN.
- Settings: cookie source (UniFetcher profile by default; main Firefox / Brave / Chrome / Edge with warnings), open the profile, saved sites with Forget / Forget all, Clear UniFetcher browser data.
- 🍪 indicator when a download uses a saved session. Cookies are copied privately for yt-dlp and never logged or exported.

### Queue, drives and quality (stage 3)
- New quality choice "Smart: best up to 1080p" (default): highest up to 1080p, prefers 720p or better, takes the lowest version above 1080p if that's all there is, merges into MP4 (MKV if MP4 can't hold the streams), never re-encodes.
- 2 downloads at once by default (maximum 5); 2–5 second polite delays between requests and items.
- The queue pauses instead of failing when the internet drops (e.g. VPN reconnecting) or the download drive is unplugged or full, and resumes by itself.
- The download drive is recognised by its serial number, so a new drive letter doesn't matter. UniFetcher never silently switches to another drive.
- Free-space warning before big batches; FAT32 warning and skipping of videos over 4 GB.
- Duplicate detection by link and video ID: already-downloaded videos are flagged, and unticked in playlists. Works with the drive unplugged.
- Never overwrites files: a taken name gets " (1)", " (2)"...
- Warning for batches over 50 items.

### yt-dlp and ffmpeg always up to date (stage 2)
- yt-dlp is checked on start, every 12 hours, and before a fetch if the last check is over 6 hours old.
- Settings: Update automatically / Ask me first / Off; channel Nightly (default) / Stable / Master; Check now; versions and last-checked time; Roll back to previous version.
- Fetches and downloads that fail with an extractor / "unsupported" error update yt-dlp and retry once.
- ffmpeg is refreshed weekly from yt-dlp/FFmpeg-Builds. Every download is checksum-verified.
- Builds bundle the newest stable yt-dlp.

### Fork setup (stage 1)
- UniFetcher for Windows is born: a fork of [Parabolic](https://github.com/NickvisionApps/Parabolic) 2026.5.0 by Nickvision.
- New name, icon, app ID and data folder (`%APPDATA%\UniFetcher`), so it installs alongside Parabolic without sharing anything.
- Update checks point at UniFetcher-Windows releases; removed Parabolic's donation, chat and discussion links.
- Windows only: removed the Linux and macOS versions.
- ffmpeg now comes from yt-dlp/FFmpeg-Builds; aria2c is no longer bundled.
- Browser extension renamed; it now opens links with `unifetcher://`.
- New GitHub workflows: a test build on every push, and releases (installer, portable zip, extension zips) from version tags.
