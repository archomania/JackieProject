async function request(url, options = {}) {
  let response;
  try {
    response = await fetch(url, options);
  } catch (error) {
    if (error.name === "AbortError") throw error;
    throw new Error("Cannot connect to the server. Please try again.");
  }
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    throw new Error(problem.title || `The request failed (${response.status}).`);
  }
  return response.status === 204 ? undefined : response.json();
}

export function getListing({ path, q }, signal) {
  const parameters = new URLSearchParams({ path });
  if (q) parameters.set("q", q);
  return request(`${q ? "/api/files/search" : "/api/files"}?${parameters}`, {
    signal, headers: { Accept: "application/json" }
  });
}

export function uploadFile(path, file) {
  const body = new FormData();
  body.append("file", file);
  return request("/api/files/upload?" + new URLSearchParams({ path }), { method: "POST", body });
}

export function createFolder(path, name) {
  const body = new FormData();
  body.append("name", name);
  return request("/api/files/create-folder?" + new URLSearchParams({ path }), { method: "POST", body });
}

export function deleteEntry(path) {
  return request("/api/files?" + new URLSearchParams({ path }), { method: "DELETE" });
}
