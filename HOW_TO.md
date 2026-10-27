# UniFetcher for Windows — How To

A plain-language guide. Sections marked *(coming soon)* describe features still being built; [STATUS.md](STATUS.md) shows what's done.

## Contents
1. [Install UniFetcher](#install-unifetcher)
2. [The "Windows protected your PC" warning](#the-windows-protected-your-pc-warning)
3. [Where your downloads are saved](#where-your-downloads-are-saved)
4. [Update UniFetcher](#update-unifetcher)
   - [yt-dlp and ffmpeg updates](#yt-dlp-and-ffmpeg-updates)
5. [The UniFetcher Firefox profile](#the-unifetcher-firefox-profile)
6. [Install the browser extension](#install-the-browser-extension)
7. [Bulk Import (150+ links)](#bulk-import-150-links)
8. [Using a VPN (PIA)](#using-a-vpn-pia)
9. [Downloading to an external hard drive](#downloading-to-an-external-hard-drive)
   - [Your downloads, privacy and notifications](#your-downloads-privacy-and-notifications)
10. [Copy an error log and report a bug](#copy-an-error-log-and-report-a-bug)
11. [Release a new version](#release-a-new-version)
12. [Test a build before releasing it](#test-a-build-before-releasing-it)

---

## Install UniFetcher

1. Go to https://github.com/Obamna1234/UniFetcher-Windows/releases
2. Under the newest release, open **Assets** and download **`UniFetcher-<version>-Setup-x64.exe`**.
   - Only pick `-arm64` if your PC has an ARM processor (rare; e.g. Surface Pro X, Snapdragon laptops).
3. Open the downloaded file. If you see a blue "Windows protected your PC" box, see the next section.
4. Click through the installer. UniFetcher appears in the Start menu. It installs just for your Windows account (in `%LOCALAPPDATA%\Programs\UniFetcher`), so it doesn't ask for administrator rights, and you never need "Run as administrator".

**Installed an early test build that asked for administrator rights?** Remove it first: **Start → Settings → Apps → Installed apps**, find **UniFetcher**, **⋯ → Uninstall**. Your settings and history are kept.

**If UniFetcher doesn't open:**
- Starting it while it's already open just brings its window to the front.
- If an earlier copy got stuck in the background, UniFetcher asks whether to close it. Click **Yes**.
- If it still won't open: press **Ctrl+Shift+Esc** (Task Manager), look for **UniFetcher** under **Processes** (or on the **Details** tab as `UniFetcher.exe`), select it, click **End task**, then start UniFetcher again.
- If something stops it from starting, a message tells you, and the details are saved in `%LOCALAPPDATA%\UniFetcher\crash.log` (type that into the File Explorer address bar). Send that file's text with your bug report.

**Portable version (no install):** download `UniFetcher-<version>-Portable-x64.zip`, right-click → **Extract All**, then double-click `UniFetcher.bat`. Its settings are kept in the same folder.

## The "Windows protected your PC" warning

UniFetcher isn't code-signed (that needs a paid certificate), so Windows SmartScreen warns about it. For **your own builds from your own repository**, it's safe to continue:

1. Check the file came from `github.com/Obamna1234/UniFetcher-Windows/releases` (your repository), not anywhere else.
2. In the blue box, click **More info**.
3. Click **Run anyway**.

If the download itself is blocked in your browser, use the browser's download menu and choose **Keep** / **Keep anyway**.

Never do this for a UniFetcher file you got from somewhere other than your own repository.

## If your antivirus blocks UniFetcher

UniFetcher isn't code-signed (that needs a paid certificate), and every test build is brand new, so antivirus programs like Malwarebytes sometimes flag it on behaviour alone ("Malware.AI…" style names are guesses by machine learning). Before allowing it:
1. Check the file came from your own repository (`github.com/Obamna1234/UniFetcher-Windows`).
2. Upload the flagged file to https://www.virustotal.com. If only one or two of ~70 scanners flag it with "AI"/"heuristic"/"generic" names, it's almost certainly a false alarm.
3. Then in Malwarebytes: **Detections → select it → Restore**, and add the UniFetcher folder (`%LOCALAPPDATA%\Programs\UniFetcher`) under **Settings → Allow List**.
4. You can also report the false positive to Malwarebytes (forums.malwarebytes.com → False Positives).

**"Setup: Unable to execute file in the temporary directory … Error 5: Access is denied"** while installing means your antivirus was still scanning (or blocked) the installer. Wait for the scan to finish, then run the installer again. If it keeps happening, allow the installer in your antivirus first, or use the portable zip.

What UniFetcher does, so you know what's normal: it downloads yt-dlp/ffmpeg updates from their official GitHub pages and runs them, opens its own Firefox profile when a site needs a session, and saves settings in `%APPDATA%\UniFetcher`. It doesn't register itself with your browsers unless you switch on **Settings → Downloader → Browser extension connection**.

## Where your downloads are saved

- New downloads go to **Downloads\UniFetcher** unless you pick another folder.
- To change it: paste a link, click **Next**, and on the next page click the folder button next to **Save folder**. UniFetcher remembers that folder for the next downloads.
- **File → Show Downloads Folder** opens that folder.
- **File → Show Last Download** (Ctrl+L) opens File Explorer with your newest finished file selected, like OBS's "Show Recordings".
- **View → History** lists everything you've downloaded, with buttons to open each file or its folder.

**"Use Authentication"** in the Add Download window is optional. It's only for sites where you have an account and yt-dlp can sign in with a username and password. Leave it off for YouTube and most sites. Cookies are separate and happen automatically: if a site refuses a download, UniFetcher tries again with your UniFetcher browser profile, and lists the site under **Action needed** if you have to sign in yourself.

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

Some sites only give working video links to a real browser (you'd see errors like "HTTP Error 403" or "410: Gone" while the video plays fine in a browser). UniFetcher fixes this by itself, using **its own separate Firefox profile called "UniFetcher"**. Your everyday Firefox or Brave is never touched: nothing UniFetcher does shows up in its history, cookies or logins.

How it works:
- The first time a site refuses a download, UniFetcher opens that site in a **UniFetcher** Firefox window, waits a few seconds for the site to set its cookies, and retries the download with those cookies. One visit per site, not per link.
- If the cookies stop working later (for example after your VPN changed server), it refreshes them once and retries. It never loops.
- While a download uses a saved session, its status shows 🍪 **Using a saved browser session (cookies)**.
- **Logins and "verify you are human" checks are never automated.** The download is marked as needing your action, and a bar at the top of UniFetcher has an **Open in browser** button. Do the login or check in the UniFetcher window, then press **Retry** on the download. The login stays in that profile only.

Needs Firefox installed (download it from mozilla.org). Without Firefox, downloads simply run without cookies.

**Open the UniFetcher profile yourself** (e.g. to log in to a site in advance):
- Settings → **Downloader** → **UniFetcher browser profile** → **Open UniFetcher browser**, or
- In Firefox, type `about:profiles` in the address bar and click **Launch profile in new browser** under **UniFetcher**.

The window it opens is the UniFetcher profile even though it looks like normal Firefox. Use your usual Firefox for everything else.

**Forget a site / clear everything**: in the same Settings section, the list shows sites with saved sessions. The bin button forgets one site; **Forget all** forgets every site; **Clear UniFetcher browser data** wipes the profile's history, cookies, logins and cache (your everyday browser is unaffected).

**Cookies from Browser** setting (same page): *UniFetcher profile (recommended)* is the default. The other choices (your main Firefox, Brave, Chrome, Edge) attach your normal browsing and logins to **every** download. Newer Brave/Chrome/Edge versions lock their cookies so yt-dlp often can't read them; if UniFetcher says it couldn't read them, switch back to the UniFetcher profile.

## Install the browser extension

The **Send to UniFetcher** extension adds:
- a toolbar button: **Send this page to UniFetcher**, **Videos found on this page** (the button shows a number when it spots video files or streams), and **All tabs → This window / All windows** (sends every tab's link to Bulk Import);
- right-click **Download with UniFetcher** on links and videos;
- the shortcut **Alt+Shift+U** to send the current page.

It sends only links — no cookies, no history — and only to UniFetcher on your own PC (through a small helper, `UniFetcher.NativeHost.exe`, that the browser starts; no internet involved). **Open UniFetcher once after installing or updating it**, so it can introduce the helper to your browsers.

### In the UniFetcher Firefox profile (default)
Nothing to do once Firefox signing is set up (below): each UniFetcher release includes the signed extension and puts it into the UniFetcher profile by itself. Close and reopen the UniFetcher browser window to see the button.

### In your main Firefox (optional)
Download `UniFetcher-Extension-<version>-Firefox.xpi` from the release, then in Firefox open **☰ → Add-ons and themes → ⚙ (gear) → Install Add-on From File…** and pick the file. Sending a link from your main Firefox shares nothing else with UniFetcher.

### In Brave, Chrome or Edge (optional)
1. Download `UniFetcher-Extension-<version>-Chrome-Brave-Edge.zip` and extract it to a folder you'll keep (e.g. `Documents\UniFetcher Extension`). The browser loads it from there, so don't delete the folder.
2. Go to `brave://extensions` (or `chrome://extensions`, `edge://extensions`).
3. Turn on **Developer mode**.
4. Click **Load unpacked** and choose the extracted folder.
5. To update later: replace the folder's contents with the new zip and click the **⟳** reload button on the extension.

Brave may show a "developer mode extensions" notice; that's expected for extensions you load yourself.

### Setting up Firefox signing (one time)
Firefox only keeps extensions signed by Mozilla. Signing is free and the extension stays private ("unlisted": not published on the add-ons site). You need to do this once:
1. Go to https://addons.mozilla.org and **Log in** (create a free Mozilla account if you don't have one).
2. Open https://addons.mozilla.org/developers/addon/api/key/ , accept the developer agreement if asked, and click **Generate new credentials**.
3. You'll see a **JWT issuer** (looks like `user:12345:678`) and a **JWT secret** (a long code). Keep this page open.
4. In another tab, open https://github.com/Obamna1234/UniFetcher-Windows/settings/secrets/actions and click **New repository secret**:
   - Name `AMO_JWT_ISSUER`, value = the JWT issuer → **Add secret**.
   - Again: name `AMO_JWT_SECRET`, value = the JWT secret → **Add secret**.
5. That's it. The next release (see "Release a new version") signs the Firefox extension automatically. Never paste these values anywhere else.

Without these secrets, releases still work; the Firefox extension just isn't signed (you can only try it temporarily via `about:debugging` → **This Firefox** → **Load Temporary Add-on…**).

## Bulk Import (150+ links)

Hand UniFetcher a long list once and let it work through it.

**Getting the list in** (any of these):
- In the browser: the extension's **All tabs → This window / All windows**.
- In UniFetcher: **File → Bulk Import…** (Ctrl+B), then paste the links, or click **Import from file…**.
- Drag an exported file onto the UniFetcher window.

Formats it understands by itself: links one per line or mixed into text (e.g. copied from your phone), bookmarks HTML exports, Firefox bookmark backups (`.json`), and tab-manager exports (JSON, CSV, OneTab).

**Documents work too**, so you can keep your links in a file instead of copying and pasting between devices: **PDF**, **Word** (`.docx`, and older `.doc`), **OpenDocument** (`.odt`), **RTF**, **e-mails** (`.eml`), saved web pages (`.mht`), **EPUB**, Excel/PowerPoint files, web shortcuts (`.url`) and **Samsung Notes**. UniFetcher finds every web link in the file, both clickable links and links just typed as text. Numbered lists such as `1. https://…`, `2. https://…` (what you get when you share all tabs into Samsung Notes) work as they are, and long links that a PDF wraps onto the next line are joined back together.
- **Samsung Notes**: best is to export the note as **PDF** or **Microsoft Word** (in the note: **⋮ → Save as file**), or share it as **Text**. A `.sdocx` file also works most of the time, but Samsung doesn't publish that format, so UniFetcher reads it on a best-effort basis.
- A PDF that's just a scan or photo of a page has no text in it, so links in it can't be read.
- Getting the file onto your PC: email it to yourself, or save it in a cloud folder you can reach from both (e.g. OneDrive, Google Drive), or copy it over a USB cable.

Exporting tabs without the extension:
- **Firefox**: right-click any tab → **Select All Tabs**, right-click again → **Bookmark Tabs…** → save into a new folder. Then **Bookmarks → Manage Bookmarks** (Ctrl+Shift+O) → **Import and Backup → Export Bookmarks to HTML…**.
- **Brave / Chrome / Edge**: **Ctrl+Shift+D** (bookmark all tabs) → new folder. Then open the bookmark manager (Ctrl+Shift+O) → **⋮** → **Export bookmarks**.
- **From your Android phone**: in Firefox for Android, open the tabs view → **⋮ → Share all tabs**, send it to yourself (e.g. email), and paste the text into Bulk Import.

**Before starting** you see a summary: total links, how many are new, duplicates inside the list and videos already downloaded (both skipped automatically), links that aren't web pages, free space on the download drive, and every site with a tick box. Untick a site or single links if you like. Playlists, channels and profiles are listed separately and unticked; tick one to download all its videos. Then press **Start**.

**While it runs**, you can close the Bulk Import window (or UniFetcher, or restart the PC): progress is saved and it carries on next time. It uses the normal download queue, so the 2-at-a-time limit, polite delays, pausing when the internet or drive disappears, and site sessions all apply. Importing the same list again later only picks up what's left.

Each link ends up as one of:
- **Downloaded**
- **Skipped (duplicate)** — already downloaded, or the same video as another link
- **Failed – will retry** — a temporary problem (connection, site busy); retried up to 3 times, waiting longer each time
- **Unavailable** — removed, private, region-locked, DRM-protected, or over 4 GB on a FAT32 drive; not retried
- **No video found** — e.g. an article or search page
- **Needs my action** — a login or "verify you are human" check; open the site in the UniFetcher browser, then press **Retry failed**
- **Failed** — still failing after the automatic retries

**At the end** you get a report with counts per result and buttons **Retry failed**, **Export list of failed links** (a text file you can import again later) and **Copy report**. Earlier imports are listed on the Bulk Import start page.

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

**Where unfinished downloads are kept (temporary files):** by default, right next to the finished video, in the same folder on the same drive. So with the download folder on your external drive, the internal drive isn't used for videos at all (UniFetcher's own settings and history stay on the PC, which is tiny). If the download drive is short of space you can pick another place: Settings → **Downloads** → **Temporary files folder** → **Choose…** (and **Use default** to go back). UniFetcher puts a `UniFetcher temp` folder inside the one you choose. Moving the finished video from a different drive takes longer, and if that drive is unplugged or full the queue pauses rather than using another drive.

What UniFetcher does with an external drive:
- If the drive is unplugged or asleep, the queue **pauses** with "Download drive not connected" and carries on by itself when the drive is back. It never quietly switches to your PC's own drive.
- It recognises the drive even if Windows gives it a different letter next time (it remembers the drive itself, not just "E:").
- Unfinished pieces of downloads are kept on the same drive as the finished videos. If the drive is pulled out mid-download, that download goes back in the queue and continues from where it stopped.
- If the drive gets full, the queue pauses; free up space and it carries on. Before a big batch, it warns you if the videos might not fit.
- On a FAT32 drive it warns you once, and skips (and lists as failed) any video over 4 GB instead of failing again and again.
- "Already downloaded" checks still work while the drive is unplugged (UniFetcher keeps that list on your PC).

### Your downloads, privacy and notifications

- **View → Downloads Library** lists everything you've downloaded, as a list or with thumbnails. Search by title, site or link; sort by newest, oldest, name, size, duration or site. Each video has **Open file**, **Show in folder**, **Copy link**, **Download again** and **Remove from list**. Removing only takes it off the list; the file is deleted only if you tick **Also delete file**.
- Settings → **Downloads**:
  - **Subfolder per site**: saves each site's videos in its own folder (e.g. `youtube.com`).
  - **Incognito mode**: while on, finished downloads aren't added to the history or the "already downloaded" list. (Bulk Import still keeps its own list so it can resume.)
  - **Windows notifications**: *Show video titles*, *Discreet* (just "Download finished", no titles or pictures), or *Off*. They only appear while UniFetcher isn't the window you're using. Bulk Import sends one notification at the end, not one per video.
  - **Clear history and cache**: forgets the history, the "already downloaded" list, finished Bulk Imports and cached files. Tick **Also clear UniFetcher browser data** to wipe the UniFetcher Firefox profile as well. Your videos are never deleted by this.
- UniFetcher never overwrites a file: if a name is taken, the new file gets " (1)", " (2)" and so on.

## When a download fails

Every failed download shows **one plain reason** instead of just "Error". Hover over it for an explanation:

| Reason | What it means |
|---|---|
| Site not supported | yt-dlp, the engine UniFetcher uses, can't read this site. Cookies or retrying won't help. |
| Blocked by site security | A "verify you are human" check, or the site is limiting requests. |
| Session rejected | UniFetcher made and refreshed a browser session, but the site still refused. It may be blocking your VPN: try another server. |
| Needs login | Log in once in the UniFetcher browser. |
| Video unavailable | Removed, private, or not available in your country. |
| DRM: not supported | Copy-protected; UniFetcher doesn't download these. |
| Network problem | The connection dropped or the site didn't answer. |
| Not enough space | The drive is full, or the file is too big for a FAT32 drive. |

A 🍪 next to the reason means a browser session (cookies) was used.

**Colours** (Help → **What the Colours Mean**): **orange** border = needs your action, **red** = failed, **grey** = stopped by you, no colour = queued, downloading or finished.

**Action needed**: downloads that need you (a login, a person check, or a VPN block) are collected under **Action needed** in the Downloads page, grouped by site, and an orange bar says how many are waiting. For each site:
1. Click **Open**: the site opens in the UniFetcher browser (not your everyday browser).
2. Log in or complete the check there.
3. Click **Done**: UniFetcher retries every waiting video from that site with a fresh session.

Nothing there expires, and the list is still there after you close UniFetcher. You get one message for all of them, not one per video.

**Downloads menu**: **Retry All Failed** (skips the ones that need you), **Resume Stopped**, **Clear Failed and Stopped**.

**Settings → Downloads**: **Skip videos already downloaded** (on by default) and **Keep free space** (2 GB by default; downloads pause before the drive gets fuller than this).

If moving a finished video into your download folder fails (for example the drive was unplugged at that moment), the video is kept in the temporary folder and the download says where.

## Copy an error log and report a bug

1. For a failed download: click **Copy diagnostic** on it. That copies the reason, whether cookies were used, what UniFetcher tried and yt-dlp's error, with cookies, link tokens and your Windows user name removed. (Or click **Log** to see everything.)
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
