// Send to UniFetcher — background script (shared by Firefox and Brave/Chrome).
// Privacy: this extension collects nothing and sends nothing anywhere except to the UniFetcher app on this PC,
// through the browser's "native messaging" (no internet involved). Only links are sent: no cookies, no history.

const api = globalThis.browser ?? globalThis.chrome;
const HOST = "com.unifetcher.host";
const MEDIA_PATTERN = /\.(mp4|m4v|webm|mkv|mov|m3u8|mpd)(\?|#|$)/i;
const MEDIA_TYPES = /^(video\/|application\/(x-mpegurl|vnd\.apple\.mpegurl|dash\+xml))/i;
const MAX_PER_TAB = 50;

// Detected media per tab, kept only in memory (cleared when the tab navigates or closes)
const detected = new Map();

function isWebLink(url) {
  return typeof url === "string" && /^https?:\/\//i.test(url);
}

function kindOf(url, contentType) {
  const lower = (url + " " + (contentType || "")).toLowerCase();
  if (lower.includes(".m3u8") || lower.includes("mpegurl")) return "HLS stream";
  if (lower.includes(".mpd") || lower.includes("dash+xml")) return "DASH stream";
  const ext = lower.match(/\.(mp4|m4v|webm|mkv|mov)/);
  if (ext) return ext[1].toUpperCase();
  return "Video";
}

async function send(message) {
  try {
    const reply = await api.runtime.sendNativeMessage(HOST, message);
    return reply && reply.ok ? { ok: true } : { ok: false, error: (reply && reply.error) || "UniFetcher didn't answer" };
  } catch (e) {
    return { ok: false, error: "UniFetcher isn't installed on this PC, or its connection to the browser isn't set up yet. Open UniFetcher once, then try again." };
  }
}

async function sendLinks(urls, mode, title) {
  const links = [...new Set(urls.filter(isWebLink))];
  if (links.length === 0) {
    return { ok: false, error: "There's no web link to send." };
  }
  return send({ type: "links", mode, urls: links, title: title || "" });
}

async function updateBadge(tabId) {
  const items = detected.get(tabId) || [];
  try {
    await api.action.setBadgeText({ tabId, text: items.length ? String(items.length) : "" });
    await api.action.setBadgeBackgroundColor({ tabId, color: "#127a74" });
  } catch {
    // tab is gone
  }
}

// ---- Video detection: watch the page's network requests for video files and streams ----
api.webRequest.onResponseStarted.addListener(
  (details) => {
    if (details.tabId < 0 || !isWebLink(details.url)) return;
    const header = (details.responseHeaders || []).find((h) => h.name.toLowerCase() === "content-type");
    const contentType = header ? header.value || "" : "";
    if (!MEDIA_PATTERN.test(details.url) && !MEDIA_TYPES.test(contentType)) return;
    // Skip the many small pieces (segments) of a stream; the playlist (.m3u8/.mpd) is what matters
    if (/\.(ts|m4s|aac)(\?|$)/i.test(details.url)) return;
    const list = detected.get(details.tabId) || [];
    if (list.some((x) => x.url === details.url) || list.length >= MAX_PER_TAB) return;
    const lengthHeader = (details.responseHeaders || []).find((h) => h.name.toLowerCase() === "content-length");
    list.push({ url: details.url, kind: kindOf(details.url, contentType), size: lengthHeader ? Number(lengthHeader.value) : 0 });
    detected.set(details.tabId, list);
    updateBadge(details.tabId);
  },
  { urls: ["<all_urls>"], types: ["media", "xmlhttprequest", "other", "object"] },
  ["responseHeaders"]
);

api.tabs.onUpdated.addListener((tabId, change) => {
  if (change.status === "loading" && change.url) {
    detected.delete(tabId);
    updateBadge(tabId);
  }
});

api.tabs.onRemoved.addListener((tabId) => detected.delete(tabId));

// ---- Right-click menu ----
function createMenus() {
  api.contextMenus.removeAll(() => {
    api.contextMenus.create({ id: "unifetcher-link", title: "Download with UniFetcher", contexts: ["link"] });
    api.contextMenus.create({ id: "unifetcher-page", title: "Send this page to UniFetcher", contexts: ["page"] });
    api.contextMenus.create({ id: "unifetcher-video", title: "Download this video with UniFetcher", contexts: ["video"] });
  });
}

api.runtime.onInstalled.addListener(createMenus);
api.runtime.onStartup?.addListener(createMenus);

api.contextMenus.onClicked.addListener(async (info, tab) => {
  let url = null;
  if (info.menuItemId === "unifetcher-link") url = info.linkUrl;
  else if (info.menuItemId === "unifetcher-video") url = isWebLink(info.srcUrl) ? info.srcUrl : info.pageUrl;
  else url = info.pageUrl || (tab && tab.url);
  const result = await sendLinks([url], "single", tab ? tab.title : "");
  if (!result.ok && tab) showError(tab.id, result.error);
});

// ---- Keyboard shortcut ----
api.commands?.onCommand.addListener(async (command) => {
  if (command !== "send-page") return;
  const [tab] = await api.tabs.query({ active: true, currentWindow: true });
  if (tab) {
    const result = await sendLinks([tab.url], "single", tab.title);
    if (!result.ok) showError(tab.id, result.error);
  }
});

function showError(tabId, message) {
  api.action.setBadgeText({ tabId, text: "!" }).catch(() => {});
  api.action.setTitle({ tabId, title: "UniFetcher: " + message }).catch(() => {});
}

// ---- Messages from the popup ----
api.runtime.onMessage.addListener((message, sender, sendResponse) => {
  (async () => {
    if (message.type === "get-detected") {
      sendResponse({ items: detected.get(message.tabId) || [] });
    } else if (message.type === "send-page") {
      sendResponse(await sendLinks([message.url], "single", message.title));
    } else if (message.type === "send-media") {
      sendResponse(await sendLinks([message.url], "single", message.title));
    } else if (message.type === "send-tabs") {
      const query = message.allWindows ? {} : { currentWindow: true };
      const tabs = await api.tabs.query(query);
      const urls = tabs.map((t) => t.url).filter(isWebLink);
      const result = await sendLinks(urls, "bulk", "");
      sendResponse({ ...result, count: [...new Set(urls)].length });
    } else {
      sendResponse({ ok: false, error: "Unknown request" });
    }
  })();
  return true; // answer asynchronously
});
