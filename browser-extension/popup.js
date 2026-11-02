const checkbox = document.getElementById("autoCaptureToggle");

chrome.storage.local.get("autoCaptureEnabled").then(({ autoCaptureEnabled }) => {
  checkbox.checked = autoCaptureEnabled !== false; // default ON
});

checkbox.addEventListener("change", () => {
  chrome.storage.local.set({ autoCaptureEnabled: checkbox.checked });
});
