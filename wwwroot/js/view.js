import { stateUrl } from "./url-state.js";

const byId = (id) => document.getElementById(id);
const dateFormatter = new Intl.DateTimeFormat(undefined, { dateStyle: "medium" });

export function formatBytes(bytes) {
  if (bytes === null || bytes === undefined) return "—";
  if (bytes === 0) return "0 B";
  const units = ["B", "KB", "MB", "GB", "TB"];
  const index = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), units.length - 1);
  return `${new Intl.NumberFormat(undefined, { maximumFractionDigits: 1 }).format(bytes / 1024 ** index)} ${units[index]}`;
}

export function renderNavigation({ path, q }) {
  const container = byId("breadcrumbs");
  container.replaceChildren();
  const segments = path.split("/").filter(Boolean);
  const crumbs = [{ name: "Home", path: "" }];
  segments.forEach((name, index) => crumbs.push({ name, path: segments.slice(0, index + 1).join("/") }));

  crumbs.forEach((crumb, index) => {
    const link = document.createElement("a");
    link.textContent = crumb.name;
    link.href = stateUrl({ path: crumb.path });
    link.dataset.path = crumb.path;
    if (index === crumbs.length - 1) link.setAttribute("aria-current", "page");
    container.append(link);
  });

  byId("folder-title").textContent = segments.at(-1) || "Home";
  byId("search").value = q;
  byId("clear-search").hidden = !q;
  byId("up").disabled = !path;
}

export function showStatus(message, error = false) {
  const status = byId("status");
  status.textContent = message;
  status.classList.toggle("error", error);
}

export function clearListing() {
  byId("entries").replaceChildren();
  byId("empty").hidden = true;
  byId("item-count").textContent = "0 items";
  for (const id of ["folder-count", "file-count", "total-size"]) byId(id).textContent = "—";
}

export function setBusy(busy) {
  byId("files").setAttribute("aria-busy", String(busy));
}

export function renderListing(listing) {
  const fragment = document.createDocumentFragment();
  for (const entry of listing.entries) {
    const row = document.createElement("tr");
    const nameCell = document.createElement("td");
    const name = document.createElement(entry.kind === "folder" ? "a" : "span");
    name.textContent = entry.name;
    name.className = `entry-name ${entry.kind}`;
    if (entry.kind === "folder") {
      name.href = stateUrl({ path: entry.path });
      name.dataset.path = entry.path;
    }
    nameCell.append(name);
    row.append(nameCell);

    const values = [entry.kind === "folder" ? "Folder" : "File", formatBytes(entry.sizeBytes), dateFormatter.format(new Date(entry.lastModifiedUtc))];
    for (const value of values) {
      const cell = document.createElement("td");
      cell.textContent = value;
      row.append(cell);
    }

    const actions = document.createElement("td");
    if (entry.kind === "file") {
      const download = document.createElement("a");
      download.href = "/api/files/download?" + new URLSearchParams({ path: entry.path });
      download.className = "download-button";
      download.textContent = "Download";
      download.download = entry.name;
      download.title = "Download " + entry.name;
      actions.append(download);
    }
    row.append(actions);
    fragment.append(row);
  }

  byId("entries").replaceChildren(fragment);
  byId("folder-count").textContent = listing.summary.folderCount;
  byId("file-count").textContent = listing.summary.fileCount;
  byId("total-size").textContent = formatBytes(listing.summary.totalFileBytes);
  byId("item-count").textContent = `${listing.entries.length} ${listing.entries.length === 1 ? "item" : "items"}`;
  byId("empty").textContent = byId("search").value ? "No matching items." : "This folder is empty.";
  byId("empty").hidden = listing.entries.length !== 0;
}

// All file names use textContent, so a file name cannot inject HTML into the page.
