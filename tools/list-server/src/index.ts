// FDG list server (#264) — the master-server registry behind the in-game server browser.
//
// Three endpoints, all JSON over HTTPS:
//   POST   /servers        register (no id/token) or heartbeat/update (id + token)
//   GET    /servers        list live entries (never includes tokens)
//   DELETE /servers/:id    polite removal (X-Token header); TTL expiry is the real cleanup
//
// Security posture (see WorkItems/264-server-browser.md):
//   - The advertised host address is ALWAYS the observed source IP of the registrant
//     (CF-Connecting-IP). A client never gets to claim an address, so the list can't be
//     poisoned with a victim's IP to aim other players' connections at it.
//   - The reachability probe only ever dials that same observed IP — the Worker can't be
//     used as an SSRF proxy to probe third parties (and private/loopback ranges are
//     rejected outright as a second layer).
//   - Registrations are token-guarded (no listing hijack), size-clamped, ASCII-folded,
//     rate-limited per IP, and capped per IP and globally.
//   - Passwords never reach this server; only a hasPassword flag is listed.

export interface Env {
  REGISTRY: DurableObjectNamespace;
}

// ---- Tunables ---------------------------------------------------------------------------

const TTL_SECONDS = 90; // 3 missed 30s heartbeats and the entry vanishes
const MAX_ENTRIES_TOTAL = 200;
const MAX_ENTRIES_PER_IP = 4;
const MIN_POST_INTERVAL_MS = 3_000; // per IP; the client heartbeats every ~30s
const MAX_BODY_BYTES = 4_096;
const MAX_NAME_CHARS = 40;
const PROBE_TIMEOUT_MS = 3_000;

// ---- Worker entry: route everything under /servers to the single Registry DO ------------

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);

    if (url.pathname === "/" && request.method === "GET") {
      return new Response("fdg-list-server up\n", { status: 200 });
    }

    if (url.pathname === "/servers" || url.pathname.startsWith("/servers/")) {
      // One global instance: the whole registry is a few KB, and a single DO gives
      // strongly consistent register/expire semantics with zero extra storage setup.
      const id = env.REGISTRY.idFromName("global");
      return env.REGISTRY.get(id).fetch(request);
    }

    return new Response("not found\n", { status: 404 });
  },
};

// ---- Registry Durable Object -------------------------------------------------------------

interface ServerEntry {
  serverId: string;
  token: string; // never serialized into listings
  name: string;
  host: string; // observed public IP of the registrant
  port: number;
  protocolVersion: number;
  typeMapHash: string;
  hasPassword: boolean;
  playerCount: number;
  maxPlayers: number;
  state: "lobby" | "in-game";
  reachable: boolean | null; // null = not probed yet / probe unsupported
  lastSeenMs: number;
}

export class Registry {
  private readonly state: DurableObjectState;
  // Last accepted POST per IP, for rate limiting. In-memory only: losing it on DO eviction
  // just means one early heartbeat gets through, which is harmless.
  private readonly lastPostByIp = new Map<string, number>();

  constructor(state: DurableObjectState) {
    this.state = state;
  }

  async fetch(request: Request): Promise<Response> {
    const url = new URL(request.url);
    try {
      if (url.pathname === "/servers" && request.method === "POST") {
        return await this.handleRegisterOrHeartbeat(request);
      }
      if (url.pathname === "/servers" && request.method === "GET") {
        return await this.handleList(url);
      }
      const deleteMatch = url.pathname.match(/^\/servers\/([A-Za-z0-9-]{1,64})$/);
      if (deleteMatch && request.method === "DELETE") {
        return await this.handleDelete(deleteMatch[1], request);
      }
      return json({ error: "not found" }, 404);
    } catch (err) {
      // Never leak internals; the client only needs to know it wasn't their fault.
      console.error("registry error:", err);
      return json({ error: "internal error" }, 500);
    }
  }

  // ---- POST /servers -----------------------------------------------------------------

  private async handleRegisterOrHeartbeat(request: Request): Promise<Response> {
    const observedIp = clientIp(request);
    if (observedIp === null) return json({ error: "could not determine client address" }, 400);

    const now = Date.now();

    // Per-IP rate limit. Applies to both register and heartbeat; the real client posts
    // every ~30s, so anything under 3s apart is a bug or abuse.
    const last = this.lastPostByIp.get(observedIp) ?? 0;
    if (now - last < MIN_POST_INTERVAL_MS) {
      return json({ error: "rate limited" }, 429);
    }
    // Opportunistically trim the rate map so it can't grow unbounded.
    if (this.lastPostByIp.size > 1_000) {
      for (const [ip, t] of this.lastPostByIp) {
        if (now - t > 60_000) this.lastPostByIp.delete(ip);
      }
    }

    const contentLength = Number(request.headers.get("Content-Length") ?? "0");
    if (!Number.isFinite(contentLength) || contentLength > MAX_BODY_BYTES) {
      return json({ error: "body too large" }, 413);
    }

    let body: unknown;
    try {
      body = await request.json();
    } catch {
      return json({ error: "invalid JSON" }, 400);
    }
    const parsed = parseRegistration(body);
    if (typeof parsed === "string") return json({ error: parsed }, 400);

    const entries = await this.loadLive(now);

    // Heartbeat path: id + token supplied and matching an existing entry.
    if (parsed.serverId !== null) {
      const existing = entries.get(parsed.serverId);
      if (existing === undefined) {
        // Expired or never existed — tell the host to re-register cleanly.
        return json({ error: "unknown serverId (re-register)" }, 404);
      }
      if (existing.token !== parsed.token || existing.host !== observedIp) {
        return json({ error: "forbidden" }, 403);
      }
      this.lastPostByIp.set(observedIp, now);

      existing.name = parsed.name;
      existing.hasPassword = parsed.hasPassword;
      existing.playerCount = parsed.playerCount;
      existing.maxPlayers = parsed.maxPlayers;
      existing.state = parsed.state;
      existing.protocolVersion = parsed.protocolVersion;
      existing.typeMapHash = parsed.typeMapHash;
      if (existing.port !== parsed.port) {
        existing.port = parsed.port;
        existing.reachable = await probe(observedIp, parsed.port); // port changed: re-probe
      }
      existing.lastSeenMs = now;
      await this.state.storage.put(existing.serverId, existing);
      return json(registrationReply(existing), 200);
    }

    // Register path.
    if (entries.size >= MAX_ENTRIES_TOTAL) return json({ error: "registry full" }, 503);
    let perIp = 0;
    for (const entry of entries.values()) {
      if (entry.host === observedIp) perIp++;
    }
    if (perIp >= MAX_ENTRIES_PER_IP) return json({ error: "too many servers from this address" }, 429);

    this.lastPostByIp.set(observedIp, now);

    const entry: ServerEntry = {
      serverId: crypto.randomUUID(),
      token: randomToken(),
      name: parsed.name,
      host: observedIp,
      port: parsed.port,
      protocolVersion: parsed.protocolVersion,
      typeMapHash: parsed.typeMapHash,
      hasPassword: parsed.hasPassword,
      playerCount: parsed.playerCount,
      maxPlayers: parsed.maxPlayers,
      state: parsed.state,
      reachable: await probe(observedIp, parsed.port),
      lastSeenMs: now,
    };
    await this.state.storage.put(entry.serverId, entry);
    return json(registrationReply(entry), 201);
  }

  // ---- GET /servers ------------------------------------------------------------------

  private async handleList(url: URL): Promise<Response> {
    const now = Date.now();
    const entries = await this.loadLive(now);

    const versionFilter = url.searchParams.get("protocolVersion");
    const listing = [...entries.values()]
      .filter(e => versionFilter === null || String(e.protocolVersion) === versionFilter)
      .sort((a, b) => a.name.localeCompare(b.name))
      .map(e => ({
        serverId: e.serverId,
        name: e.name,
        host: e.host,
        port: e.port,
        protocolVersion: e.protocolVersion,
        typeMapHash: e.typeMapHash,
        hasPassword: e.hasPassword,
        playerCount: e.playerCount,
        maxPlayers: e.maxPlayers,
        state: e.state,
        reachable: e.reachable,
        ageSeconds: Math.round((now - e.lastSeenMs) / 1000),
      }));

    return json({ servers: listing }, 200);
  }

  // ---- DELETE /servers/:id -----------------------------------------------------------

  private async handleDelete(serverId: string, request: Request): Promise<Response> {
    const entry = (await this.state.storage.get(serverId)) as ServerEntry | undefined;
    if (entry === undefined) return json({ ok: true }, 200); // already gone: fine
    if (request.headers.get("X-Token") !== entry.token) return json({ error: "forbidden" }, 403);
    await this.state.storage.delete(serverId);
    return json({ ok: true }, 200);
  }

  // ---- Storage helpers ----------------------------------------------------------------

  /** Loads all entries, deleting expired ones as a lazy sweep (no alarms needed). */
  private async loadLive(now: number): Promise<Map<string, ServerEntry>> {
    const all = (await this.state.storage.list()) as Map<string, ServerEntry>;
    const live = new Map<string, ServerEntry>();
    const expired: string[] = [];
    for (const [key, entry] of all) {
      if (now - entry.lastSeenMs > TTL_SECONDS * 1000) expired.push(key);
      else live.set(key, entry);
    }
    if (expired.length > 0) await this.state.storage.delete(expired);
    return live;
  }
}

// ---- Validation ---------------------------------------------------------------------------

interface ParsedRegistration {
  serverId: string | null; // null = fresh registration
  token: string | null;
  name: string;
  port: number;
  protocolVersion: number;
  typeMapHash: string;
  hasPassword: boolean;
  playerCount: number;
  maxPlayers: number;
  state: "lobby" | "in-game";
}

/** Returns the parsed registration, or an error string. Every field is clamped/validated —
 *  this is the trust boundary for everything a host self-reports. */
function parseRegistration(body: unknown): ParsedRegistration | string {
  if (typeof body !== "object" || body === null) return "body must be an object";
  const b = body as Record<string, unknown>;

  const name = asciiFold(String(b.name ?? "")).slice(0, MAX_NAME_CHARS).trim();
  if (name.length === 0) return "name is required";

  const port = Number(b.port);
  if (!Number.isInteger(port) || port < 1024 || port > 65535) return "port must be 1024-65535";

  const protocolVersion = Number(b.protocolVersion);
  if (!Number.isInteger(protocolVersion) || protocolVersion < 0 || protocolVersion > 100_000) {
    return "protocolVersion must be a non-negative integer";
  }

  const typeMapHash = String(b.typeMapHash ?? "");
  if (!/^[A-Fa-f0-9]{0,64}$/.test(typeMapHash)) return "typeMapHash must be hex, <= 64 chars";

  const playerCount = Number(b.playerCount);
  const maxPlayers = Number(b.maxPlayers);
  if (!Number.isInteger(playerCount) || playerCount < 0 || playerCount > 64) return "bad playerCount";
  if (!Number.isInteger(maxPlayers) || maxPlayers < 0 || maxPlayers > 64) return "bad maxPlayers";

  const state = b.state === "in-game" ? "in-game" : b.state === "lobby" ? "lobby" : null;
  if (state === null) return "state must be 'lobby' or 'in-game'";

  const hasId = typeof b.serverId === "string" && b.serverId.length > 0;
  const hasToken = typeof b.token === "string" && (b.token as string).length > 0;
  if (hasId !== hasToken) return "serverId and token must be supplied together";

  return {
    serverId: hasId ? (b.serverId as string).slice(0, 64) : null,
    token: hasToken ? (b.token as string).slice(0, 128) : null,
    name,
    port,
    protocolVersion,
    typeMapHash,
    hasPassword: b.hasPassword === true,
    playerCount,
    maxPlayers,
    state,
  };
}

/** Mirrors the game's ASCII-only convention: anything outside printable ASCII becomes '?'. */
function asciiFold(text: string): string {
  let out = "";
  for (const ch of text) {
    const code = ch.codePointAt(0) ?? 0;
    out += code >= 0x20 && code <= 0x7e ? ch : "?";
  }
  return out;
}

// ---- Reachability probe -------------------------------------------------------------------

/** TCP-dials ip:port (the OBSERVED ip only — never anything client-supplied) and reports
 *  whether a connection opened. null when probing isn't possible in this runtime. */
async function probe(ip: string, port: number): Promise<boolean | null> {
  if (isPrivateOrLocal(ip)) {
    // Local dev (wrangler dev sees 127.0.0.1) or something weird: don't dial into
    // private ranges from Cloudflare's network. Listed as "unknown".
    return null;
  }
  try {
    const { connect } = await import("cloudflare:sockets");
    const socket = connect({ hostname: ip, port });
    const opened = socket.opened.then(() => true as const);
    const timeout = new Promise<false>(resolve => setTimeout(() => resolve(false), PROBE_TIMEOUT_MS));
    const result = await Promise.race([opened, timeout]);
    await socket.close().catch(() => {});
    return result;
  } catch {
    return false;
  }
}

function isPrivateOrLocal(ip: string): boolean {
  if (ip.includes(":")) {
    // IPv6: loopback, link-local, unique-local.
    const lower = ip.toLowerCase();
    return lower === "::1" || lower.startsWith("fe80:") || lower.startsWith("fc") || lower.startsWith("fd");
  }
  const parts = ip.split(".").map(Number);
  if (parts.length !== 4 || parts.some(n => !Number.isInteger(n) || n < 0 || n > 255)) return true;
  const [a, b] = parts;
  return (
    a === 10 ||
    a === 127 ||
    a === 0 ||
    (a === 172 && b >= 16 && b <= 31) ||
    (a === 192 && b === 168) ||
    (a === 169 && b === 254) ||
    (a === 100 && b >= 64 && b <= 127) // CGNAT
  );
}

// ---- Small helpers ------------------------------------------------------------------------

function clientIp(request: Request): string | null {
  // CF-Connecting-IP is set by Cloudflare's edge and can't be forged by the caller.
  // wrangler dev doesn't always set it; fall back to loopback there so local smoke works.
  return request.headers.get("CF-Connecting-IP") ?? "127.0.0.1";
}

function randomToken(): string {
  const bytes = new Uint8Array(24);
  crypto.getRandomValues(bytes);
  return [...bytes].map(b => b.toString(16).padStart(2, "0")).join("");
}

function registrationReply(entry: ServerEntry): object {
  return {
    serverId: entry.serverId,
    token: entry.token,
    publicIp: entry.host,
    reachable: entry.reachable,
    ttlSeconds: TTL_SECONDS,
  };
}

function json(payload: object, status: number): Response {
  return new Response(JSON.stringify(payload) + "\n", {
    status,
    headers: { "Content-Type": "application/json" },
  });
}
