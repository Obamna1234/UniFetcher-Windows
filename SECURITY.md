# Security notes

## October 2026 security review

A read-only security review of UniFetcher for Windows (the WinUI app, native-messaging host, browser
extension, and installer) was carried out. **No critical or high-severity issues were found.** The
extension ↔ native-host ↔ app chain — the main concern for this class of app — is well mitigated:
`CurrentUserOnly` named pipes, single-ID `allowed_origins`/`allowed_extensions`, http/https scheme
enforcement on every URL, and a user-confirmation dialog before any download. The tool updater is HTTPS,
GitHub-prefix-locked, and SHA-256 verified.

### Fixed in this pass (low-severity, behaviour-preserving)

1. **Native host launch (CWE-426/CWE-377).** `UniFetcher.NativeHost` now starts the app with
   `UseShellExecute = false`, launching the verified absolute path directly instead of via the OS shell.
   No browser-supplied data reaches the launch.
2. **Removed dead `unifetcher://` scheme handling (CWE-939).** The custom-scheme rewrite in
   `StartupGuard` and `MainWindowController.UrlFromArgs` was removed — the installer does not register that
   protocol, so it was unreachable dead code and a latent URI-handler surface. Command-line URL handling is
   now restricted to `http`/`https` in both places (the latter previously accepted any absolute URI).

### Deferred (needs a design decision — not changed, to avoid breaking updates)

- **Signature pinning on downloaded tools (Low).** `ToolUpdateService` verifies HTTPS + a GitHub prefix +
  SHA-256 before running `yt-dlp.exe`/`ffmpeg`. Adding Authenticode verification is desirable but **cannot
  be a blanket requirement**: upstream `yt-dlp.exe` is not Authenticode-signed, so a signature-required
  gate would break tool updates. A targeted approach (pin ffmpeg's publisher where signed, and/or add a
  second trust anchor) is recommended as a future change.

## Reporting a problem

Open an issue on GitHub (without personal data), or contact the repository owner.
