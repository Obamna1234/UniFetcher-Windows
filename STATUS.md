# UniFetcher for Windows — Status

Spec: `UNIFETCHER_WINDOWS_SPEC.md` (v3) in the UniFetcher Claude project. If work is interrupted, a new session reads this file and the spec and carries on.

Base project: **Parabolic** by Nickvision (MIT, C# / WinUI 3, .NET 10). Internal code names (`Nickvision.Parabolic.*`) are kept so upstream fixes stay easy to merge; everything users see says UniFetcher.

## Stages
| # | Stage | State |
|---|-------|-------|
| 1 | Fork setup, rename, credits, remove external links, workflows, `.gitignore`, `HOW_TO.md`, `STATUS.md` | Done (build passing) |
| 2 | yt-dlp and ffmpeg updating, auto-retry | Done (build passing) |
| 3 | Queue improvements, pause on connection loss, external drive, duplicate detection, quality preset, batch downloads | Done (build passing) |
| 4 | Dedicated "UniFetcher" Firefox profile sessions, VPN handling | Done (build passing) |
| 5 | Browser extension (native messaging, video detection, send all tabs, Firefox signing) | Done (build passing) |
| 6 | Bulk import (paste, file, drag-and-drop, send all tabs, end report) | Done (build passing) |
| 7 | Organising downloads, incognito, discreet notifications, clear history | Done (build passing) |

## Decisions so far
- GitHub Actions in `.github/workflows` are pinned to exact commit SHAs (tag in a comment), so a moved or hijacked tag can't change what the build or release runs. To update one, look up the new tag's commit and replace the SHA. (October 2026 security review from the Sevbify project; nothing else needed changing: tool updates are checksum-verified, the extension is off by default, and the native host only accepts http(s) links from UniFetcher's own extension over a current-user-only pipe.)
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
- Downloads Library is `LibraryWindow` (built in code) over Parabolic's history table; it replaces Parabolic's History dialog in the View menu. Windows notifications use the Windows App SDK `AppNotificationManager` (`WindowsNotifier`).
- Temporary files: `--paths temp:` is the download folder unless `TempFolder` is set (then `<TempFolder>\UniFetcher temp`). `QueueGuardService` pauses with `TempMissing` / `DriveFull` for it. Fetching info (no video data) still uses Windows' temp folder.
- Documents for Bulk Import: `DocumentTextExtractor` (PDF via PdfPig 0.1.9 + raw /URI scan incl. compressed streams; zip-based Office/ODF/EPUB/Samsung Notes; RTF; EML/MHT with quoted-printable and base64; UTF-8/UTF-16 scan of other binaries) → `LinkImportParser`.
- Test builds are versioned `0.0.0-dev.<run>`; releases take their version from the tag.

## Fixed after the first real-PC test (27 Oct)
- Second start crashed (settings database opened from two places at once at start-up) → database is opened once in `Program.Main` before anything else. This was v5-changes bug 1 ("won't open again after closing").
- Installer no longer needs admin (per-user install, HKCU link handler). Single-instance guard with stuck-copy recovery, crash message + `crash.log`, process always exits after the window closes.
- Build now runs a start-up test (install, open/close ×3) and prints `crash.log` / `app.log` errors / Windows crash events as run annotations.

## Antivirus (27 Oct)
- Malwarebytes quarantined a test build. Code audit found nothing malicious (only GitHub + the sites you download from; bundled tools from official releases). Removed behaviour that looks suspicious: reading other processes' command lines, ending processes, unifetcher:// handler; browser-extension registry entries are opt-in (`BrowserExtensionEnabled`, default off). Waiting on the user for the detection name/file to know exactly what was flagged.

## Next
- `claude/ANDROID_TO_WINDOWS_CHANGES.md` items 1–10: **done** (failure reasons, Action needed view, colours + key, Copy diagnostic, Resume stopped / Clear failed and stopped, keep-free-space, skip already downloaded, safe move, start-up test, update check). Logic in `FailureReason.cs`, `ActionNeededService`, `Diagnostic.cs`; tests in `UniFetcher.Tests`.
- Still to do from `claude/UNIFETCHER_WINDOWS_V5_CHANGES.md`: bugs 2 (keyring Back button) and 3 (keyring explanation), then features A–G (stage 10). The spec is now v5 (section 12).
- All 7 stages are built. Next: test on your PC, then make the first release by pushing a version tag (HOW_TO.md → "Release a new version").

## Waiting on you
- Nothing. (Firefox extension signing was skipped: you said the browser extension isn't needed. It still builds unsigned; nothing else depends on it.)
