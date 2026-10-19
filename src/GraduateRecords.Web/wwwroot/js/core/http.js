/**
 * Shared fetch helper. AJAX callers must send the antiforgery token header.
 * Never assign untrusted text via HTML-string DOM APIs; use textContent / createElement.
 */

/**
 * @param {string} url
 * @param {RequestInit & { antiforgeryToken?: string }} [options]
 * @returns {Promise<Response>}
 */
export async function request(url, options = {}) {
  const headers = new Headers(options.headers ?? undefined);

  if (options.antiforgeryToken) {
    headers.set("RequestVerificationToken", options.antiforgeryToken);
  }

  if (!headers.has("Accept")) {
    headers.set("Accept", "application/json");
  }

  return fetch(url, {
    ...options,
    headers,
    credentials: "same-origin",
  });
}
