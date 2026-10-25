# Send to UniFetcher (browser extension)

One codebase for Firefox and Brave/Chrome/Edge. `src/` holds the shared files; `manifest.firefox.json` and `manifest.chrome.json` differ only where the browsers do. `scripts/build-extension.ps1` builds both into separate folders (the release workflow zips them and signs the Firefox one).

It talks only to UniFetcher on the same PC via native messaging (`com.unifetcher.host`), sends only links, and collects nothing. Based on the Parabolic extension by NickvisionApps & OwlRoyalty.

Install steps: see [HOW_TO.md](../HOW_TO.md#install-the-browser-extension).
