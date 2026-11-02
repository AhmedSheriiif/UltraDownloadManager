const HOST_NAME = "com.ultradownloadmanager.host";

chrome.runtime.onInstalled.addListener(() => {
  chrome.contextMenus.create({
    id: "udm-download-link",
    title: "Download with Ultra Download Manager",
    contexts: ["link"]
  });
  chrome.contextMenus.create({
    id: "udm-download-media",
    title: "Download with Ultra Download Manager",
    contexts: ["video", "audio", "image"]
  });
});

chrome.contextMenus.onClicked.addListener(async (info, tab) => {
  const url = info.menuItemId === "udm-download-link" ? info.linkUrl : info.srcUrl;
  if (!url) return;

  const result = await sendToNativeHost(url, tab?.url ?? "");
  if (result.success) {
    notify("Added to Ultra Download Manager", url);
  } else {
    notify("Ultra Download Manager", result.message || "Failed to add download.");
  }
});

// Auto-capture: intercept the browser's own downloads and hand them to Ultra Download
// Manager instead, the way IDM/FDM do. onDeterminingFilename fires after the download is
// queued but before any bytes are written, so cancelling here means nothing ever lands in
// the browser's own Downloads folder/shelf.
chrome.downloads.onDeterminingFilename.addListener((downloadItem, suggest) => {
  handleAutoCapture(downloadItem, suggest);
  return true; // we may call suggest() asynchronously (or not at all, if we cancel)
});

async function handleAutoCapture(downloadItem, suggest) {
  const url = downloadItem.finalUrl || downloadItem.url;

  if (!(await isAutoCaptureEnabled()) || !url || !/^https?:\/\//i.test(url)) {
    suggest();
    return;
  }

  const result = await sendToNativeHost(url, downloadItem.referrer || "");
  if (!result.success) {
    // Native host/app unreachable - let the browser finish the download rather than lose it.
    notify("Ultra Download Manager", result.message || "Falling back to the browser download.");
    suggest();
    return;
  }

  chrome.downloads.cancel(downloadItem.id, () => {
    chrome.downloads.erase({ id: downloadItem.id });
  });
  notify("Added to Ultra Download Manager", url);
}

async function sendToNativeHost(url, referrerUrl) {
  let cookieHeader = "";
  try {
    const cookies = await chrome.cookies.getAll({ url });
    if (cookies.length > 0) {
      cookieHeader = cookies.map((c) => `${c.name}=${c.value}`).join("; ");
    }
  } catch (e) {
    // best effort - some URLs (e.g. file://) can't be queried for cookies
  }

  const message = {
    action: "addDownload",
    url,
    referrer: referrerUrl,
    cookie: cookieHeader
  };

  return new Promise((resolve) => {
    chrome.runtime.sendNativeMessage(HOST_NAME, message, (response) => {
      if (chrome.runtime.lastError) {
        resolve({ success: false, message: chrome.runtime.lastError.message ?? "Is Ultra Download Manager installed?" });
        return;
      }
      resolve(response ?? { success: false, message: "No response from Ultra Download Manager." });
    });
  });
}

async function isAutoCaptureEnabled() {
  const { autoCaptureEnabled } = await chrome.storage.local.get("autoCaptureEnabled");
  return autoCaptureEnabled !== false; // default ON
}

function notify(title, message) {
  chrome.notifications.create({
    type: "basic",
    iconUrl: "icons/icon128.png",
    title,
    message
  });
}
