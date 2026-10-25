# UniFetcher for Windows — How To

A plain-language guide. Sections marked *(coming soon)* describe features still being built; [STATUS.md](STATUS.md) shows what's done.

## Contents
1. [Install UniFetcher](#install-unifetcher)
2. [The "Windows protected your PC" warning](#the-windows-protected-your-pc-warning)
3. [Update UniFetcher](#update-unifetcher)
   - [yt-dlp and ffmpeg updates](#yt-dlp-and-ffmpeg-updates)
4. [The UniFetcher Firefox profile](#the-unifetcher-firefox-profile) *(coming soon)*
5. [Install the browser extension](#install-the-browser-extension)
6. [Using a VPN (PIA)](#using-a-vpn-pia)
7. [Downloading to an external hard drive](#downloading-to-an-external-hard-drive)
8. [Copy an error log and report a bug](#copy-an-error-log-and-report-a-bug)
9. [Release a new version](#release-a-new-version)
10. [Test a build before releasing it](#test-a-build-before-releasing-it)

---

## Install UniFetcher

1. Go to https://github.com/Obamna1234/UniFetcher-Windows/releases
2. Under the newest release, open **Assets** and download **`UniFetcher-<version>-Setup-x64.exe`**.
   - Only pick `-arm64` if your PC has an ARM processor (rare; e.g. Surface Pro X, Snapdragon laptops).
3. Open the downloaded file. If you see a blue "Windows protected your PC" box, see the next section.
4. Click through the installer. UniFetcher appears in the Start menu.

**Portable version (no install):** download `UniFetcher-<version>-Portable-x64.zip`, right-click → **Extract All**, then double-click `UniFetcher.bat`. Its settings are kept in the same folder.

## The "Windows protected your PC" warning

UniFetcher isn't code-signed (that needs a paid certificate), so Windows SmartScreen warns about it. For **your own builds from your own repository**, it's safe to continue:

1. Check the file came from `github.com/Obamna1234/UniFetcher-Windows/releases` (your repository), not anywhere else.
2. In the blue box, click **More info**.
3. Click **Run anyway**.

If the download itself is blocked in your browser, use the browser's download menu and choose **Keep** / **Keep anyway**.

Never do this for a UniFetcher file you got from somewhere other than your own repository.

## Update UniFetcher

- UniFetcher checks your Releases page on start. When there's a new version, a bar appears at the top of the window: click **Update**. It downloads the new installer and runs it.
- Or: **Help → Check for Updates**.
- Or: download the newest `Setup` file from Releases and run it. Installing over the old version **keeps your settings, history and queue** (they live in `%APPDATA%\UniFetcher`, not in the program folder).

### yt-dlp and ffmpeg updates

Websites change often, and an old yt-dlp is the most common reason a download fails. UniFetcher handles this on its own:

- It checks for a new yt-dlp when it starts, every 12 hours while open, and before fetching a link if the last check was over 6 hours ago.
- If a download fails because a site "isn't supported" or "can't be read", it updates yt-dlp (if there's a newer one) and tries that download once more.
- ffmpeg is refreshed about once a week.
- Everything comes only from the official yt-dlp pages on GitHub, and each file's checksum is verified before it's used.

Settings → **User Interface** → **yt-dlp and ffmpeg**:
- **Updates**: *Update automatically* (recommended), *Ask me first* (a bar with an **Update** button appears at the top of the window), or *Off*.
- **yt-dlp channel**: *Nightly* (recommended, gets site fixes within a day), *Stable* (fewer, bigger releases), or *Master* (every single change; for testing).
- **Check now**, plus the versions in use and when they were last checked.
- **Roll back to previous version**: if downloads started failing right after an update, press this to go back. UniFetcher won't reinstall that same version; it waits for the next one.

## The UniFetcher Firefox profile

*(coming soon — stage 4)* UniFetcher will use its own separate Firefox profile called "UniFetcher" for website sessions, so nothing touches your everyday Firefox or Brave. This section will explain how to open it.

## Install the browser extension

The extension adds a **"Send this page to UniFetcher"** button and a right-click **"Download with UniFetcher"** option. It sends only the link — no cookies, history or anything else — and only to UniFetcher on your own PC.

The extension zips are attached to each release: `UniFetcher-Extension-<version>-Firefox.zip` and `UniFetcher-Extension-<version>-Chrome-Brave.zip`.

**Brave / Chrome (developer mode):**
1. Download the `Chrome-Brave` zip and extract it to a folder you'll keep (e.g. `Documents\UniFetcher Extension`). Don't delete that folder later — the browser loads the extension from it.
2. In the address bar, go to `brave://extensions` (or `chrome://extensions`).
3. Turn on **Developer mode** (top right).
4. Click **Load unpacked** and choose the extracted folder.

**Firefox:** a permanently installed Firefox extension must be signed by Mozilla. Signing is set up in stage 5; until then you can try it temporarily:
1. Go to `about:debugging#/runtime/this-firefox`.
2. Click **Load Temporary Add-on…** and choose `manifest.json` inside the extracted Firefox zip.
3. It stays until you close Firefox.

Installing the extension in your main browser is optional. Sending a link from your main browser shares nothing else with UniFetcher.

## Using a VPN (PIA)

- Use the **Private Internet Access desktop app**. It covers the whole PC, so UniFetcher's downloads go through the VPN with no extra setup.
- A browser-only VPN (a browser extension, or a VPN built into one browser) does **not** cover UniFetcher's downloads.
- **Split tunnelling** (PIA → Settings → Split Tunnel):
  - If you have a list of apps that **bypass** the VPN, make sure none of these are in it: `UniFetcher.exe`, `yt-dlp.exe`, `ffmpeg.exe`, `firefox.exe`.
  - If it's set so **only listed apps use the VPN**, add all four. `UniFetcher.exe`, `yt-dlp.exe` and `ffmpeg.exe` are in `C:\Program Files\UniFetcher\Release\`; Firefox is usually `C:\Program Files\Mozilla Firefox\firefox.exe`.
- If the VPN reconnects or PIA's kill switch blocks the internet for a moment, UniFetcher pauses the queue and carries on when the connection is back. Those downloads aren't counted as failed.
- If a site keeps refusing you or showing "verify you are human" checks, it may be blocking VPN traffic. Switch PIA to another server and press Retry.

## Downloading to an external hard drive

1. In UniFetcher, open **Settings** and set the download folder to a folder on your external drive.
2. Check the drive's format: open **File Explorer → This PC**, right-click the drive → **Properties**, and look at **File system**.
   - **NTFS** or **exFAT**: good.
   - **FAT32**: it can't store files over 4 GB, so long high-quality videos will fail. Reformatting to exFAT fixes this, but **reformatting erases everything on the drive** — copy your files off first.

What UniFetcher does with an external drive:
- If the drive is unplugged or asleep, the queue **pauses** with "Download drive not connected" and carries on by itself when the drive is back. It never quietly switches to your PC's own drive.
- It recognises the drive even if Windows gives it a different letter next time (it remembers the drive itself, not just "E:").
- Unfinished pieces of downloads are kept on the same drive as the finished videos. If the drive is pulled out mid-download, that download goes back in the queue and continues from where it stopped.
- If the drive gets full, the queue pauses; free up space and it carries on. Before a big batch, it warns you if the videos might not fit.
- On a FAT32 drive it warns you once, and skips (and lists as failed) any video over 4 GB instead of failing again and again.
- "Already downloaded" checks still work while the drive is unplugged (UniFetcher keeps that list on your PC).

## Copy an error log and report a bug

1. For a failed download: click the **Log** button on that download, then select the text that appears and copy it (Ctrl+A, Ctrl+C).
2. For a general problem: **Help → About** → **Debugging** → **Copy Debug Information**.
3. **Before sharing, look through the text** and remove anything private (links you don't want shared, your Windows user name in file paths).
4. Go to https://github.com/Obamna1234/UniFetcher-Windows/issues/new (or **Help → Report a Bug** in the app), describe what happened, paste the text, and click **Submit new issue**.

## Release a new version

Releases are made by pushing a **tag** (a label on a version of the code). The tag must look like `v1.2.3`.

Easiest way, on the GitHub website:
1. Open https://github.com/Obamna1234/UniFetcher-Windows/releases and click **Draft a new release**.
2. Click **Choose a tag**, type a new version such as `v1.0.0`, and click **Create new tag: v1.0.0 on publish**.
3. Leave the title and description empty and click **Publish release**.
4. Open the **Actions** tab. A **Release** run starts and takes roughly 20–30 minutes. When it finishes, your release gets the installers, portable zips and extension zips added to it, plus automatic release notes.

Version numbers: raise the last number for small fixes (`v1.0.1`), the middle number for new features (`v1.1.0`). Each version must be higher than the last, or the in-app updater won't offer it. A tag like `v1.1.0-beta1` makes a "pre-release".

Using the command line instead (optional):
```
git tag v1.0.0
git push origin v1.0.0
```

## Test a build before releasing it

Every change pushed to GitHub is built automatically:
1. Open the **Actions** tab and click the newest **Build** run (green tick = passed).
2. Scroll to **Artifacts** and download `UniFetcher-0.0.0-dev.<number>-x64`.
3. Extract it and run the `Setup` file (or the portable zip inside).

Test builds have version `0.0.0-dev.<number>` and a different-coloured icon, so you can tell them apart.
