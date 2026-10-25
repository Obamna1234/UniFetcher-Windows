# Changelog

## Unreleased
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
