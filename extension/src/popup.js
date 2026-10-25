const api = globalThis.browser ?? globalThis.chrome;
const status = document.getElementById("status");

function show(result, okText) {
  status.className = result.ok ? "ok" : "bad";
  status.textContent = result.ok ? okText : result.error;
}

function formatSize(bytes) {
  if (!bytes) return "";
  const mb = bytes / (1024 * 1024);
  return mb >= 1024 ? (mb / 1024).toFixed(1) + " GB" : mb.toFixed(mb < 10 ? 1 : 0) + " MB";
}

async function init() {
  const [tab] = await api.tabs.query({ active: true, currentWindow: true });

  document.getElementById("send-page").addEventListener("click", async () => {
    show(await api.runtime.sendMessage({ type: "send-page", url: tab.url, title: tab.title }), "Sent to UniFetcher.");
  });

  for (const [id, allWindows] of [["send-window", false], ["send-all", true]]) {
    document.getElementById(id).addEventListener("click", async () => {
      const result = await api.runtime.sendMessage({ type: "send-tabs", allWindows });
      show(result, `Sent ${result.count} links to UniFetcher's Bulk Import.`);
    });
  }

  const { items } = await api.runtime.sendMessage({ type: "get-detected", tabId: tab.id });
  if (items.length) {
    document.getElementById("media-section").hidden = false;
    const list = document.getElementById("media-list");
    for (const item of items) {
      const li = document.createElement("li");
      const button = document.createElement("button");
      const kind = document.createElement("span");
      kind.className = "kind";
      kind.textContent = item.kind;
      const detail = document.createElement("span");
      detail.className = "detail";
      detail.textContent = formatSize(item.size) || new URL(item.url).pathname.split("/").pop();
      button.title = item.url;
      button.append(kind, detail);
      button.addEventListener("click", async () => {
        show(await api.runtime.sendMessage({ type: "send-media", url: item.url, title: tab.title }), "Sent to UniFetcher.");
      });
      li.append(button);
      list.append(li);
    }
  }
}

init();
