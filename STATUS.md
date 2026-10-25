# UniFetcher for Windows — Status

Spec: `UNIFETCHER_WINDOWS_SPEC.md` (v3) in the UniFetcher Claude project. If work is interrupted, a new session reads this file and the spec and carries on.

Base project: **Parabolic** by Nickvision (MIT, C# / WinUI 3, .NET 10). Internal code names (`Nickvision.Parabolic.*`) are kept so upstream fixes stay easy to merge; everything users see says UniFetcher.

## Stages
| # | Stage | State |
|---|-------|-------|
| 1 | Fork setup, rename, credits, remove external links, workflows, `.gitignore`, `HOW_TO.md`, `STATUS.md` | Done (build passing) |
| 2 | yt-dlp and ffmpeg updating, auto-retry | Done (build passing) |
| 3 | Queue improvements, pause on connection loss, external drive, duplicate detection, quality preset, batch downloads | Done — waiting for green build |
| 4 | Dedicated "UniFetcher" Firefox profile sessions, VPN handling | Done (build passing) |
| 5 | Browser extension (native messaging, video detection, send all tabs, Firefox signing) | Done — waiting for green build |
| 6 | Bulk import (paste, file, drag-and-drop, send all tabs, end report) | Done — waiting for green build |
| 7 | Organising downloads, incognito, discreet notifications, clear history | Next |

## Decisions so far
- Bundled programs come only from official GitHub releases: yt-dlp (yt-dlp/yt-dlp), ffmpeg (yt-dlp/FFmpeg-Builds), deno (denoland/deno, needed by yt-dlp for YouTube).
- Deno is updated only with UniFetcher releases (the app no longer contacts denoland), to keep to the spec's network list.
- aria2c is not bundled (no official Windows build source); its setting is hidden.
- Installer files are named `UniFetcher-<version>-Setup-<arch>.exe` (not `UniFetcher-Setup-<version>.exe`) because the built-in updater recognises installers by the ending `setup-x64.exe`.
- yt-dlp/ffmpeg updates live in `ToolUpdateService` (Shared/Services). Installed copies go to `%LOCALAPPDATA%\UniFetcher\tools`; only the current and previous version are kept (for Roll back). ffmpeg is refreshed at most weekly because FFmpeg-Builds publishes daily (~100 MB each).
- Queue pausing (internet / drive missing / drive full) lives in `QueueGuardService`; interrupted downloads are put back in the queue as fresh items, keeping partial files so yt-dlp resumes.
- Duplicate detection uses UniFetcher's own `unifetcher_downloaded` table (`DownloadRegistryService`), filled when a download *succeeds* (Parabolic's history is filled when a download is *added*).
- Parabolic's "Overwrite existing files" setting is forced off and hidden; aria2c settings are hidden.
- Sessions: `FirefoxProfileService` (profile in `%APPDATA%\UniFetcher\FirefoxProfile`, registered as "UniFetcher" in profiles.ini) and `SiteSessionService` (table `unifetcher_sessions`). yt-dlp reads a private, checkpointed copy of the profile's cookies.sqlite via `--cookies-from-browser firefox:<copy>`, so the UniFetcher window doesn't need closing; it's closed only if the file can't be copied. Firefox session-only cookies aren't stored on disk, so they can't be used.
- Parabolic's "Cookies from Browser" setting: `None` now means "UniFetcher profile".
- Extension: one source in `extension/src` + two manifests; `scripts/build-extension.ps1` builds both. Chromium ID is fixed by the public `key` in `manifest.chrome.json` (`iofbbjgpimljhofeflakhkjknpelglob`); Firefox ID `unifetcher-extension@obamna1234.github.io`. Native host `com.unifetcher.host` = `UniFetcher.NativeHost` (AOT console app) → named pipe `UniFetcher.Inbox.<user>` → `InboxService`. Registration is per-user (HKCU) on every app start.
- Bulk Import: `LinkImportParser` (formats), `BulkImportService` (tables `unifetcher_bulk_jobs` / `unifetcher_bulk_items`, background worker that keeps the download queue topped up one fetch at a time), `BulkImportWindow` (built in code). Bulk downloads are tagged with `DownloadOptions.BulkItemId`, are not added to Parabolic's crash recovery (Bulk Import resumes them itself), and report their final result through `IDownloadService.DownloadFinished`.
- Test builds are versioned `0.0.0-dev.<run>`; releases take their version from the tag.

## Waiting on you
- Optional, for a signed Firefox extension: create a Mozilla API key and add GitHub secrets `AMO_JWT_ISSUER` and `AMO_JWT_SECRET` (steps in HOW_TO.md → "Setting up Firefox signing").
