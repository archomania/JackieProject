// The URL owns navigation state, so refresh, links, Back and Forward agree.
export function readState(search) {
  const parameters = new URLSearchParams(search);
  return { path: parameters.get("path") || "", q: parameters.get("q") || "" };
}

export function stateUrl({ path = "", q = "" }) {
  const parameters = new URLSearchParams();
  if (path) parameters.set("path", path);
  if (q) parameters.set("q", q);
  const query = parameters.toString();
  return query ? `/?${query}` : "/";
}

export function parentPath(path) {
  return path.split("/").filter(Boolean).slice(0, -1).join("/");
}
