import { createFolder, deleteEntry, getListing, uploadFile } from "./api.js?v=folder-dialog-2";
import { readState, stateUrl, parentPath } from "./url-state.js?v=folder-dialog-2";
import { renderNavigation, renderListing, clearListing, showStatus, setBusy } from "./view.js?v=folder-dialog-2";

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

function confirmDelete(name) {
  try {
    return Promise.resolve(window.confirm(`Delete "${name}"?`));
  } catch (error) {
    if (!(error instanceof Error) || !error.message.includes("confirm() is not supported")) throw error;
    deleteMessage.textContent = `Delete "${name}"?`;
    deleteDialog.showModal();
    return new Promise((resolve) => {
      deleteForm.addEventListener("submit", (event) => {
        resolve(event.submitter?.value === "delete");
      }, { once: true });
      deleteDialog.addEventListener("cancel", () => resolve(false), { once: true });
    });
  }
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
  if (!path || !name || !(await confirmDelete(name))) return;

  button.disabled = true;
  try {
    showStatus("Deleting " + name + "…");
    await deleteEntry(path);
    const state = readState(location.search);
    navigate({ path: state.path, q: state.q });
  } catch (error) {
    button.disabled = false;
    showStatus(error.message, true);
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
void load();

const uploadButton = document.getElementById("upload");
const picker = document.getElementById("picker");
const createFolderButton = document.getElementById("create-folder");
const folderDialog = document.getElementById("folder-dialog");
const folderForm = document.getElementById("folder-form");
const folderName = document.getElementById("folder-name");
const deleteDialog = document.getElementById("delete-dialog");
const deleteForm = document.getElementById("delete-form");
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
  try {
    const name = await requestFolderName();
    if (name === null) return;

    const { path } = readState(location.search);
    createFolderButton.disabled = true;
    showStatus("Creating folder…");
    await createFolder(path, name.trim());
    if (readState(location.search).path === path) navigate({ path, q: "" });
  } catch (error) {
    showStatus(error.message, true);
  } finally {
    createFolderButton.disabled = false;
  }
});
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
