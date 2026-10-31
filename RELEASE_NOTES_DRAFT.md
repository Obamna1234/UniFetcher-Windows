# UniFetcher for Windows — release notes draft (security hardening)

> Draft for the next release. No functional changes; hardening only. Review before publishing.

## Recommended version — security hardening

Small hardening fixes from the October 2026 security review. **Nothing about how the app works changes.**

**What changed**
- The background helper that the browser extension talks to now launches the app more safely (no shell).
- Removed unused `unifetcher://` link handling and tightened command-line links to http/https only.

**Severity:** low hardening. No critical or high-severity issues were found. The extension/native-host chain
was reviewed closely and is sound.

## Keeping a stable fallback

The previous release remains available as a known-good fallback.
