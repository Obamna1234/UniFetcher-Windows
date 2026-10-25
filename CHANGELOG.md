# Changelog

## Unreleased
- UniFetcher for Windows is born: a fork of [Parabolic](https://github.com/NickvisionApps/Parabolic) 2026.5.0 by Nickvision.
- New name, icon, app ID and data folder (`%APPDATA%\UniFetcher`), so it installs alongside Parabolic without sharing anything.
- Update checks point at UniFetcher-Windows releases; removed Parabolic's donation, chat and discussion links.
- Windows only: removed the Linux and macOS versions.
- ffmpeg now comes from yt-dlp/FFmpeg-Builds; aria2c is no longer bundled.
- Browser extension renamed; it now opens links with `unifetcher://`.
- New GitHub workflows: a test build on every push, and releases (installer, portable zip, extension zips) from version tags.
