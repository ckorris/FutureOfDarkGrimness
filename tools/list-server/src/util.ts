// Small helpers shared by the list-server Durable Objects (#271 registry, #226 reports,
// #412 analytics).

/** Mirrors the game's ASCII-only convention: anything outside printable ASCII becomes '?'. */
export function asciiFold(text: string): string {
  let out = "";
  for (const ch of text) {
    const code = ch.codePointAt(0) ?? 0;
    out += code >= 0x20 && code <= 0x7e ? ch : "?";
  }
  return out;
}

export function clientIp(request: Request): string | null {
  // CF-Connecting-IP is set by Cloudflare's edge and can't be forged by the caller.
  // wrangler dev doesn't always set it; fall back to loopback there so local smoke works.
  return request.headers.get("CF-Connecting-IP") ?? "127.0.0.1";
}

export function json(payload: object, status: number): Response {
  return new Response(JSON.stringify(payload) + "\n", {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

/** Null when the caller presents the admin token; the error response otherwise. No ADMIN_TOKEN
 *  secret configured means closed (503), never open. */
export function requireAdmin(request: Request, configured: string | undefined): Response | null {
  if (configured === undefined || configured.length === 0) {
    return json({ error: "admin token not configured" }, 503);
  }
  if (request.headers.get("X-Admin-Token") !== configured) {
    return json({ error: "forbidden" }, 403);
  }
  return null;
}
