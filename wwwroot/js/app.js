import { getListing, uploadFile } from "./api.js";
import { readState, stateUrl, parentPath } from "./url-state.js";
import { renderNavigation, renderListing, clearListing, showStatus, setBusy } from "./view.js";

let pendingRequest;

async function load() {
  pendingRequest?.abort();
  const request = new AbortController();
  pendingRequest = request;
  const state = readState(location.search);
  renderNavigation(state);
  clearListing();
  setBusy(true);
  showStatus(state.q ? "Searching…" : "Loading folder…");

  try {
    const listing = await getListing(state, request.signal);
    // A slower response must not overwrite the folder the user just opened.
    if (request.signal.aborted) return;
    renderListing(listing);
    showStatus(state.q ? `Results for “${state.q}”` : "Showing the current folder");
  } catch (error) {
    if (!request.signal.aborted) showStatus(error.message, true);
  } finally {
    if (pendingRequest === request) setBusy(false);
  }
}

function navigate(state) {
  const url = stateUrl(state);
  if (`${location.pathname}${location.search}` !== url) history.pushState(null, "", url);
  void load();
}

document.addEventListener("click", (event) => {
  const link = event.target.closest("a[data-path], a[data-home]");
  // Keep native Ctrl/Cmd-click and new-tab behavior for shareable links.
  if (!link || event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
  event.preventDefault();
  navigate({ path: link.dataset.path || "", q: "" });
});

document.getElementById("up").addEventListener("click", () => {
  navigate({ path: parentPath(readState(location.search).path), q: "" });
});

document.getElementById("search-form").addEventListener("submit", (event) => {
  event.preventDefault();
  navigate({ path: readState(location.search).path, q: document.getElementById("search").value.trim() });
});

document.getElementById("clear-search").addEventListener("click", () => {
  navigate({ path: readState(location.search).path, q: "" });
});

window.addEventListener("popstate", () => void load());
void load();

const uploadButton = document.getElementById("upload");
const picker = document.getElementById("picker");
uploadButton.addEventListener("click", () => picker.click());
picker.addEventListener("change", async () => {
  const file = picker.files[0];
  if (!file) return;
  const { path } = readState(location.search);
  uploadButton.disabled = true;
  try {
    showStatus("Uploading " + file.name + "…");
    await uploadFile(path, file);
    if (readState(location.search).path === path) navigate({ path, q: "" });
  } catch (error) {
    showStatus(error.message, true);
  } finally {
    picker.value = "";
    uploadButton.disabled = false;
  }
});
