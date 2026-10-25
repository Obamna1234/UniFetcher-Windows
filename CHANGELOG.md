# Changelog

## Unreleased
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
