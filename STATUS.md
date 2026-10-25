# UniFetcher for Windows — Status

Spec: `UNIFETCHER_WINDOWS_SPEC.md` (v3) in the UniFetcher Claude project. If work is interrupted, a new session reads this file and the spec and carries on.

Base project: **Parabolic** by Nickvision (MIT, C# / WinUI 3, .NET 10). Internal code names (`Nickvision.Parabolic.*`) are kept so upstream fixes stay easy to merge; everything users see says UniFetcher.

## Stages
| # | Stage | State |
|---|-------|-------|
| 1 | Fork setup, rename, credits, remove external links, workflows, `.gitignore`, `HOW_TO.md`, `STATUS.md` | Done (build passing) |
| 2 | yt-dlp and ffmpeg updating, auto-retry | Done — waiting for green build |
| 3 | Queue improvements, pause on connection loss, external drive, duplicate detection, quality preset, batch downloads | Next |
| 4 | Dedicated "UniFetcher" Firefox profile sessions, VPN handling | To do |
| 5 | Browser extension (native messaging, video detection, send all tabs, Firefox signing) | To do |
| 6 | Bulk import (paste, file, drag-and-drop, send all tabs, end report) | To do |
| 7 | Organising downloads, incognito, discreet notifications, clear history | To do |

## Decisions so far
- Bundled programs come only from official GitHub releases: yt-dlp (yt-dlp/yt-dlp), ffmpeg (yt-dlp/FFmpeg-Builds), deno (denoland/deno, needed by yt-dlp for YouTube).
- Deno is updated only with UniFetcher releases (the app no longer contacts denoland), to keep to the spec's network list.
- aria2c is not bundled (no official Windows build source); its setting is hidden.
- Installer files are named `UniFetcher-<version>-Setup-<arch>.exe` (not `UniFetcher-Setup-<version>.exe`) because the built-in updater recognises installers by the ending `setup-x64.exe`.
- yt-dlp/ffmpeg updates live in `ToolUpdateService` (Shared/Services). Installed copies go to `%LOCALAPPDATA%\UniFetcher\tools`; only the current and previous version are kept (for Roll back). ffmpeg is refreshed at most weekly because FFmpeg-Builds publishes daily (~100 MB each).
- Test builds are versioned `0.0.0-dev.<run>`; releases take their version from the tag.

## Waiting on you
- Nothing right now.
