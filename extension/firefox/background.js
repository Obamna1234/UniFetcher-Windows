// Function to format the URL and open the scheme link
function openUniFetcherUrl(url) {
  browser.storage.sync.get(['trimPlaylist'], (result) => {
    if (browser.runtime.lastError) {
      console.error('Storage error:', browser.runtime.lastError);
    }
    let processedUrl = url;
    if (result && result.trimPlaylist) {
      processedUrl = trimPlaylistFromUrl(url);
    }
    // Remove "https://" or "http://" from the URL
    let formattedUrl = processedUrl.replace(/^https?:\/\//, '');
    // Construct the final scheme URL
    let schemeUrl = `unifetcher://${formattedUrl}`;

    // Use the browser.tabs API to open the URL scheme
    browser.tabs.update({ url: schemeUrl });
  });
}

// Function to create the context menu item
function createContextMenu() {
  browser.contextMenus.create({
    id: "openUniFetcherLink",
    title: "Open link in UniFetcher",
    contexts: ["link", "page"]
  });
}

// Function to remove the context menu item
function removeContextMenu() {
  browser.contextMenus.remove("openUniFetcherLink");
}

// Initialize context menu based on stored settings
browser.runtime.onInstalled.addListener(() => {
  browser.storage.sync.get('showContextMenu', (result) => {
    if (result.showContextMenu !== false) {
      createContextMenu();
    }
  });
});

// Listen for changes in storage to update the context menu
browser.storage.onChanged.addListener((changes, namespace) => {
  if (namespace === 'sync' && changes.showContextMenu) {
    if (changes.showContextMenu.newValue) {
      createContextMenu();
    } else {
      removeContextMenu();
    }
  }
});

// Listener for context menu: Open link in UniFetcher clicks
browser.contextMenus.onClicked.addListener((info, tab) => {
  if (info.menuItemId === "openUniFetcherLink") {
    // If the right click was on a link, use the link's URL.
    // Otherwise, use the current tab's URL.
    const urlToOpen = info.linkUrl || info.pageUrl;
    openUniFetcherUrl(urlToOpen);
  }
});

// Listener for action (Extension icon) clicks
browser.action.onClicked.addListener((tab) => {
  // Use the current tab's URL for the action button
  openUniFetcherUrl(tab.url);
});

// Listener for Keyboard shorcut command(Alt+P, Alt+O)
browser.commands.onCommand.addListener((command) => {
  if (command === "open-unifetcher-current-tab") {
    browser.tabs.query({ active: true, currentWindow: true }, (tabs) => {
      if (tabs.length > 0) {
        openUniFetcherUrl(tabs[0].url);
      }
    });
  };
  if (command === "open-unifetcher-options") {
    browser.runtime.openOptionsPage();
  }
});
