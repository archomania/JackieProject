import { createFolder, deleteEntry, getListing, uploadFile } from "./api.js?v=folder-dialog-2";
import { readState, stateUrl, parentPath } from "./url-state.js?v=folder-dialog-2";
import { renderNavigation, renderListing, clearListing, showStatus, setBusy } from "./view.js?v=folder-dialog-2";

let pendingRequest;
let folderAvailable = false;
let uploadInProgress = false;
let folderInProgress = false;
const locationKey = () => location.pathname + location.search;

function refreshActionButtons() {
  uploadButton.disabled = !folderAvailable || uploadInProgress;
  createFolderButton.disabled = !folderAvailable || folderInProgress;
}

async function load() {
  pendingRequest?.abort();
  const request = new AbortController();
  pendingRequest = request;
  const state = readState(location.search);
  folderAvailable = false;
  refreshActionButtons();
  renderNavigation(state);
  clearListing();
  setBusy(true);
  showStatus(state.q ? "Searching…" : "Loading folder…");

  try {
    const listing = await getListing(state, request.signal);
    // A slower response must not overwrite the folder the user just opened.
    if (request.signal.aborted) return;
    renderListing(listing);
    folderAvailable = true;
    refreshActionButtons();
    showStatus(listing.listingIncomplete
      ? "Some items changed while loading. Refresh to try again."
      : state.q ? `Results for “${state.q}”` : "Showing the current folder");
  } catch (error) {
    if (!request.signal.aborted) showStatus(error.message, true);
  } finally {
    if (pendingRequest === request) setBusy(false);
  }
}

function confirmDelete(name) {
  deleteMessage.textContent = `Delete “${name}”? Folders and all their contents will be permanently removed.`;
  deleteDialog.returnValue = "cancel";
  return new Promise((resolve) => {
    deleteDialog.addEventListener("close", () => resolve(deleteDialog.returnValue === "delete"), { once: true });
    deleteDialog.showModal();
  });
}

function navigate(state) {
  const url = stateUrl(state);
  if (`${location.pathname}${location.search}` !== url) history.pushState(null, "", url);
  void load();
}

document.addEventListener("click", (event) => {
  const remove = event.target.closest("button[data-delete-path]");
  if (remove) {
    void removeEntry(remove);
    return;
  }
  const link = event.target.closest("a[data-path], a[data-home]");
  // Keep native Ctrl/Cmd-click and new-tab behavior for shareable links.
  if (!link || event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
  event.preventDefault();
  navigate({ path: link.dataset.path || "", q: "" });
});

async function removeEntry(button) {
  const path = button.dataset.deletePath;
  const name = button.dataset.deleteName;
  if (!path || !name) return;
  const startedAt = locationKey();
  button.disabled = true;
  try {
    if (!(await confirmDelete(name))) return;
    showStatus("Deleting " + name + "…");
    await deleteEntry(path);
    if (locationKey() === startedAt) await load();
  } catch (error) {
    if (locationKey() === startedAt) showStatus(error.message, true);
  } finally {
    button.disabled = false;
  }
}

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

const uploadButton = document.getElementById("upload");
const picker = document.getElementById("picker");
const createFolderButton = document.getElementById("create-folder");
const folderDialog = document.getElementById("folder-dialog");
const folderForm = document.getElementById("folder-form");
const folderName = document.getElementById("folder-name");
const deleteDialog = document.getElementById("delete-dialog");
const deleteMessage = document.getElementById("delete-message");

function requestFolderName() {
  folderForm.reset();
  folderDialog.returnValue = "cancel";
  return new Promise((resolve) => {
    folderDialog.addEventListener("close", () => {
      resolve(folderDialog.returnValue === "create" ? folderName.value : null);
    }, { once: true });
    folderDialog.showModal();
    folderName.focus();
  });
}

createFolderButton.addEventListener("click", async () => {
  const startedAt = locationKey();
  const { path } = readState(location.search);
  folderInProgress = true;
  refreshActionButtons();
  try {
    const name = await requestFolderName();
    if (name === null) return;

    showStatus("Creating folder…");
    await createFolder(path, name.trim());
    if (locationKey() === startedAt) await load();
  } catch (error) {
    if (locationKey() === startedAt) showStatus(error.message, true);
  } finally {
    folderInProgress = false;
    refreshActionButtons();
  }
});
uploadButton.addEventListener("click", () => picker.click());
picker.addEventListener("change", async () => {
  const file = picker.files[0];
  if (!file) return;
  const startedAt = locationKey();
  const { path } = readState(location.search);
  uploadInProgress = true;
  refreshActionButtons();
  try {
    if (file.size > 10 * 1024 * 1024) throw new Error("Choose a file of 10 MiB or less.");
    showStatus("Uploading " + file.name + "…");
    await uploadFile(path, file);
    if (locationKey() === startedAt) await load();
  } catch (error) {
    if (locationKey() === startedAt) showStatus(error.message, true);
  } finally {
    picker.value = "";
    uploadInProgress = false;
    refreshActionButtons();
  }
});

void load();
