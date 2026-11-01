export async function getListing({ path, q }, signal) {
  const parameters = new URLSearchParams({ path });
  let endpoint = "/api/files";

  if (q) {
    endpoint += "/search";
    parameters.set("q", q);
  }

  let response;
  try {
    response = await fetch(`${endpoint}?${parameters}`, {
      signal,
      headers: { Accept: "application/json" },
    });
  } catch (error) {
    if (error.name === "AbortError") throw error;
    throw new Error("Cannot connect to the server. Please try again.");
  }

  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    throw new Error(problem.title || `The request failed (${response.status}).`);
  }

  return response.json();
}

export async function uploadFile(path, file) {
  const body = new FormData();
  body.append("file", file);
  const response = await fetch("/api/files/upload?" + new URLSearchParams({ path }), {
    method: "POST", body
  });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    throw new Error(problem.title || `Upload failed (${response.status}).`);
  }
}
