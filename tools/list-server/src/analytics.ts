// Anonymous usage analytics (#412), a third Durable Object on the list-server Worker.
//
//   POST /events          a JSON array of 1..20 events (no auth; allowlisted, capped, rate-limited)
//   GET  /stats?days=N     canned JSON summary                       (X-Admin-Token)
//   GET  /stats/export?after=<id>&limit=<n>&format=csv|json[&newest=1]  raw rows, paged by id (X-Admin-Token)
//   DELETE /stats/smoke    remove smoke.sh's own rows (fixed SMOKE_INSTALL id) (X-Admin-Token)
//   DELETE /stats?confirm=wipe-all-analytics  remove every event, install and daily count (X-Admin-Token)
//
// Privacy posture (see WorkItems/412-usage-analytics.md):
//   - The game sends a random install UUID (generated locally, resettable, never derived from
//     hardware), a per-launch session UUID, the app version, OS, CPU arch, and an allowlisted
//     set of per-event properties. No names, chat, army contents, paths, or locale.
//   - The observed IP is used ONLY for the per-IP rate limit, in a separate table whose rows are
//     pruned after RATE_RETENTION_MS. It never touches the events or installs tables, so the
//     stored data never links an install ID to an address.
//   - Every event name, property key, type, enum value and length is checked against SCHEMA;
//     anything unknown rejects the whole batch. A tampered client can still post plausible fake
//     counts - the numbers are indicative, not audited.
//   - Storage is capped by row count and REJECTS when full rather than evicting.
//
// Free-plan budget: SQLite-backed DOs bill rows written/read. One event is one events row plus
// its (name, t) index entry; app_launch also upserts one installs row. The summary reads only
// the installs table and index-bounded slices of events, so it stays cheap as the table grows.

import { asciiFold, clientIp, json, requireAdmin } from "./util";
import type { Env } from "./index";

// ---- Tunables ---------------------------------------------------------------------------

const MAX_BODY_BYTES = 8_192;
const MAX_EVENTS_PER_BATCH = 20;
const MIN_POST_INTERVAL_MS = 2_000; // per IP; the client flushes every ~30s
const RATE_RETENTION_MS = 5 * 60_000; // rate records older than this are deleted on every write
const MAX_ROWS = 2_000_000; // ~1GB at worst; well inside the free-plan DO storage allowance
/** Events accepted per UTC day. The free plan's rows-written allowance (about 100k/day) is per account
 *  and shared with the server browser and bug-report box on this Worker; an event costs ~2-3 row
 *  writes, so this keeps analytics well under half of it even on a surge. Past the cap the Worker
 *  answers 503, which clients treat as "retry later" and keep in their local spool. */
const DAILY_EVENT_CAP = 10_000;
const CLOCK_SKEW_SECONDS = 7 * 86_400; // client t outside +-7 days of receipt is clamped
const MAX_EXPORT_ROWS = 5_000;
const DEFAULT_STATS_DAYS = 30;
const MAX_STATS_DAYS = 3_650;

const WIPE_CONFIRM = "wipe-all-analytics";

/** The fixed install ID smoke.sh posts under. DELETE /stats/smoke removes exactly these rows, so
 *  smoking the deployed Worker leaves the real numbers untouched. Not a secret: anyone posting
 *  under it only gets their own fake rows cleaned up. */
export const SMOKE_INSTALL = "0b1c2d3e-4f50-4a6b-8c7d-9e0f1a2b3c4d";


// ---- Schema allowlist -------------------------------------------------------------------

type PropSpec =
  | { type: "bool" }
  | { type: "int"; min: number; max: number }
  | { type: "str"; maxLen: number }
  | { type: "enum"; values: readonly string[] }
  /** An engine enum's member name (e.g. "BoltAction"). Shape-checked rather than value-listed so
   *  a new engine enum member doesn't need a Worker redeploy to be recorded. */
  | { type: "ident" }
  | { type: "strArr"; maxItems: number; maxLen: number }
  | { type: "identArr"; maxItems: number }
  | { type: "intArr"; maxItems: number; min: number; max: number };

const SYSTEMS = ["grimdark-future", "age-of-fantasy", "mixed"] as const;
const COUNT = { type: "int", min: 0, max: 64 } as const;
const SECONDS = { type: "int", min: 0, max: 30 * 86_400 } as const;
const POINTS = { type: "int", min: 0, max: 100_000 } as const;
const FACTION_LEN = 60;
const MAX_SLOTS = 8;

export const SCHEMA: Record<string, Record<string, PropSpec>> = {
  app_launch: {
    first_launch: { type: "bool" },
    prev_session_seconds: SECONDS,
    prev_session_clean: { type: "bool" },
  },
  game_start: {
    points: POINTS,
    system: { type: "enum", values: SYSTEMS },
    players: COUNT,
    humans: COUNT,
    ais: COUNT,
    remote: COUNT,
    teams: COUNT,
    ai_profiles: { type: "identArr", maxItems: MAX_SLOTS },
    factions: { type: "strArr", maxItems: MAX_SLOTS, maxLen: FACTION_LEN },
    systems: { type: "identArr", maxItems: MAX_SLOTS },
    slot_points: { type: "intArr", maxItems: MAX_SLOTS, min: 0, max: 100_000 },
    listed: { type: "bool" },
    resume: { type: "bool" },
    terrain_mode: { type: "ident" },
    terrain_count: { type: "int", min: 0, max: 1_000 },
    objective_mode: { type: "ident" },
    randomness: { type: "ident" },
    turn_style: { type: "ident" },
    shooting_mode: { type: "ident" },
    cover_exceptions: { type: "bool" },
    see_through_friendlies: { type: "bool" },
    unlimited_split_fire: { type: "bool" },
  },
  game_join: {
    faction: { type: "str", maxLen: FACTION_LEN },
    system: { type: "enum", values: SYSTEMS },
    points: POINTS,
  },
  game_end: {
    outcome: { type: "enum", values: ["Win", "Tie", "Fault", "Disconnect"] },
    rounds: { type: "int", min: 0, max: 1_000 },
    seconds: SECONDS,
    winner: { type: "enum", values: ["human", "ai", "none"] },
  },
  game_abandon: {
    seconds: SECONDS,
    reason: { type: "enum", values: ["menu", "quit", "close"] },
  },
  // Sent only when the Combat Calculator has stayed open for a minute (menu clicks are not recorded).
  feature_open: {
    feature: { type: "enum", values: ["combat_calculator"] },
  },
};

/** Props whose histograms should read true/false (SQLite's json_extract yields 0/1). */
const BOOL_PROPS = new Set(
  Object.values(SCHEMA).flatMap(spec => Object.entries(spec).filter(([, p]) => p.type === "bool").map(([k]) => k)),
);

const OS_VALUES = ["windows", "linux", "macos", "other"];
const ARCH_VALUES = ["x64", "arm64", "other"];
const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const IDENT_RE = /^[A-Za-z0-9_-]{1,40}$/;

interface ValidEvent {
  name: string;
  t: number;
  install: string;
  session: string;
  game: string | null;
  ver: string;
  os: string;
  arch: string;
  props: Record<string, unknown>;
}

/** Validates one event against SCHEMA. Returns the normalized event or a reason string. */
export function validateEvent(raw: unknown, receivedSeconds: number): ValidEvent | string {
  if (typeof raw !== "object" || raw === null || Array.isArray(raw)) return "event must be an object";
  const e = raw as Record<string, unknown>;

  for (const key of Object.keys(e)) {
    if (!["name", "t", "install", "session", "game", "ver", "os", "arch", "props"].includes(key)) {
      return `unknown field '${key}'`;
    }
  }

  const name = e.name;
  if (typeof name !== "string" || !(name in SCHEMA)) return "unknown event name";

  let t = e.t;
  if (typeof t !== "number" || !Number.isInteger(t)) return "t must be an integer";
  // Real machines have wrong clocks; clamp rather than reject so the event still counts.
  if (Math.abs((t as number) - receivedSeconds) > CLOCK_SKEW_SECONDS) t = receivedSeconds;

  for (const key of ["install", "session"] as const) {
    if (typeof e[key] !== "string" || !UUID_RE.test(e[key] as string)) return `${key} must be a lowercase UUID`;
  }
  let game: string | null = null;
  if (e.game !== undefined && e.game !== null) {
    if (typeof e.game !== "string" || !UUID_RE.test(e.game)) return "game must be a lowercase UUID";
    game = e.game;
  }

  if (typeof e.ver !== "string" || e.ver.length === 0 || e.ver.length > 64) return "ver must be 1-64 chars";
  if (typeof e.os !== "string" || !OS_VALUES.includes(e.os)) return "bad os";
  if (typeof e.arch !== "string" || !ARCH_VALUES.includes(e.arch)) return "bad arch";

  const propsIn = e.props ?? {};
  if (typeof propsIn !== "object" || propsIn === null || Array.isArray(propsIn)) return "props must be an object";
  const spec = SCHEMA[name];
  const props: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(propsIn as Record<string, unknown>)) {
    const s = spec[key];
    if (s === undefined) return `unknown prop '${name}.${key}'`;
    if (value === null) continue; // an absent value, e.g. no previous session yet
    const checked = checkProp(s, value);
    if (typeof checked === "object" && checked !== null && "error" in checked) return `${name}.${key}: ${checked.error}`;
    props[key] = checked;
  }

  return {
    name,
    t: t as number,
    install: e.install as string,
    session: e.session as string,
    game,
    ver: asciiFold(e.ver),
    os: e.os,
    arch: e.arch,
    props,
  };
}

function checkProp(spec: PropSpec, value: unknown): unknown {
  const isInt = (v: unknown, min: number, max: number) =>
    typeof v === "number" && Number.isInteger(v) && v >= min && v <= max;
  switch (spec.type) {
    case "bool":
      return typeof value === "boolean" ? value : { error: "must be a boolean" };
    case "int":
      return isInt(value, spec.min, spec.max) ? value : { error: `must be an integer ${spec.min}-${spec.max}` };
    case "str":
      return typeof value === "string" && value.length <= spec.maxLen
        ? asciiFold(value)
        : { error: `must be a string <= ${spec.maxLen} chars` };
    case "enum":
      return typeof value === "string" && spec.values.includes(value) ? value : { error: "value not allowed" };
    case "ident":
      return typeof value === "string" && IDENT_RE.test(value) ? value : { error: "must be an identifier" };
    case "strArr":
      if (!Array.isArray(value) || value.length > spec.maxItems) return { error: `must be an array <= ${spec.maxItems}` };
      if (!value.every(v => typeof v === "string" && v.length <= spec.maxLen)) return { error: "bad array item" };
      return value.map(v => asciiFold(v as string));
    case "identArr":
      if (!Array.isArray(value) || value.length > spec.maxItems) return { error: `must be an array <= ${spec.maxItems}` };
      if (!value.every(v => typeof v === "string" && IDENT_RE.test(v))) return { error: "bad array item" };
      return value;
    case "intArr":
      if (!Array.isArray(value) || value.length > spec.maxItems) return { error: `must be an array <= ${spec.maxItems}` };
      if (!value.every(v => isInt(v, spec.min, spec.max))) return { error: "bad array item" };
      return value;
  }
}

// ---- Durable Object -----------------------------------------------------------------------

export class Analytics {
  private readonly state: DurableObjectState;
  private readonly env: Env;
  private readonly sql: SqlStorage;

  constructor(state: DurableObjectState, env: Env) {
    this.state = state;
    this.env = env;
    this.sql = state.storage.sql;
    // Idempotent schema setup; runs once per DO wake-up and costs nothing when it exists.
    this.sql.exec(`CREATE TABLE IF NOT EXISTS events(
      id INTEGER PRIMARY KEY, received INTEGER NOT NULL, t INTEGER NOT NULL, name TEXT NOT NULL,
      install TEXT NOT NULL, session TEXT NOT NULL, game TEXT, ver TEXT NOT NULL,
      os TEXT NOT NULL, arch TEXT NOT NULL, props TEXT NOT NULL)`);
    this.sql.exec(`CREATE INDEX IF NOT EXISTS events_name_t ON events(name, t)`);
    // One row per install, touched only by app_launch: makes lifetime/d7/d30 uniques and
    // new-installs-per-day a read of this small table instead of a scan of every event.
    this.sql.exec(`CREATE TABLE IF NOT EXISTS installs(
      install TEXT PRIMARY KEY, first_seen INTEGER NOT NULL, last_seen INTEGER NOT NULL)`);
    // Rate-limit records: the only place an IP is ever written, pruned within minutes.
    this.sql.exec(`CREATE TABLE IF NOT EXISTS rate(ip TEXT PRIMARY KEY, last INTEGER NOT NULL)`);
    // Events accepted per UTC day, for DAILY_EVENT_CAP. One small row per day.
    this.sql.exec(`CREATE TABLE IF NOT EXISTS daily(day TEXT PRIMARY KEY, events INTEGER NOT NULL)`);
  }

  async fetch(request: Request): Promise<Response> {
    const url = new URL(request.url);
    try {
      if (url.pathname === "/events" && request.method === "POST") return await this.handleIngest(request);
      if (url.pathname === "/stats" && request.method === "GET") {
        return requireAdmin(request, this.env.ADMIN_TOKEN) ?? this.handleStats(url);
      }
      if (url.pathname === "/stats/export" && request.method === "GET") {
        return requireAdmin(request, this.env.ADMIN_TOKEN) ?? this.handleExport(url);
      }
      if (url.pathname === "/stats" && request.method === "DELETE") {
        return requireAdmin(request, this.env.ADMIN_TOKEN) ?? this.handleWipe(url);
      }
      if (url.pathname === "/stats/smoke" && request.method === "DELETE") {
        return requireAdmin(request, this.env.ADMIN_TOKEN) ?? this.handleDeleteSmoke();
      }
      return json({ error: "not found" }, 404);
    } catch (err) {
      console.error("analytics error:", err);
      return json({ error: "internal error" }, 500);
    }
  }

  // ---- POST /events ---------------------------------------------------------------------

  private async handleIngest(request: Request): Promise<Response> {
    const observedIp = clientIp(request);
    if (observedIp === null) return json({ error: "could not determine client address" }, 400);
    const nowMs = Date.now();

    // Prune first, so a stale record never outlives RATE_RETENTION_MS whatever happens next.
    this.sql.exec(`DELETE FROM rate WHERE last < ?`, nowMs - RATE_RETENTION_MS);
    const last = this.sql.exec<{ last: number }>(`SELECT last FROM rate WHERE ip = ?`, observedIp).toArray()[0]?.last ?? 0;
    if (nowMs - last < MIN_POST_INTERVAL_MS) return json({ error: "rate limited" }, 429);

    // Recorded before any validation, so a flood of malformed batches is throttled too.
    this.sql.exec(`INSERT INTO rate(ip, last) VALUES(?, ?) ON CONFLICT(ip) DO UPDATE SET last = excluded.last`,
      observedIp, nowMs);

    const bytes = new Uint8Array(await request.arrayBuffer());
    if (bytes.byteLength === 0) return json({ error: "empty body" }, 400);
    if (bytes.byteLength > MAX_BODY_BYTES) return json({ error: "body too large" }, 413);

    let body: unknown;
    try {
      body = JSON.parse(new TextDecoder().decode(bytes));
    } catch {
      return json({ error: "invalid JSON" }, 400);
    }
    if (!Array.isArray(body) || body.length === 0) return json({ error: "body must be a non-empty array" }, 400);
    if (body.length > MAX_EVENTS_PER_BATCH) return json({ error: `at most ${MAX_EVENTS_PER_BATCH} events` }, 413);

    const receivedSeconds = Math.floor(nowMs / 1000);
    const events: ValidEvent[] = [];
    for (let i = 0; i < body.length; i++) {
      const result = validateEvent(body[i], receivedSeconds);
      if (typeof result === "string") return json({ error: `event ${i}: ${result}` }, 400);
      events.push(result);
    }

    // Reject rather than evict: a flood must never delete real data. MAX(id) is an index
    // lookup; it equals the row count except for smoke rows cleaned up later, so it can only
    // overcount (the conservative direction for a cap).
    const rows = this.sql.exec<{ n: number | null }>(`SELECT MAX(id) AS n FROM events`).toArray()[0]?.n ?? 0;
    if (rows + events.length > MAX_ROWS) return json({ error: "analytics storage full" }, 503);

    const today = new Date(nowMs).toISOString().slice(0, 10);
    const acceptedToday =
      this.sql.exec<{ events: number }>(`SELECT events FROM daily WHERE day = ?`, today).toArray()[0]?.events ?? 0;
    if (acceptedToday + events.length > DAILY_EVENT_CAP) {
      return json({ error: "daily analytics budget used; retry tomorrow" }, 503);
    }

    this.state.storage.transactionSync(() => {
      this.sql.exec(
        `INSERT INTO daily(day, events) VALUES(?, ?) ON CONFLICT(day) DO UPDATE SET events = events + excluded.events`,
        today, events.length);
      for (const ev of events) {
        this.sql.exec(
          `INSERT INTO events(received, t, name, install, session, game, ver, os, arch, props)
           VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
          receivedSeconds, ev.t, ev.name, ev.install, ev.session, ev.game, ev.ver, ev.os, ev.arch,
          JSON.stringify(ev.props));
        if (ev.name === "app_launch") {
          this.sql.exec(
            `INSERT INTO installs(install, first_seen, last_seen) VALUES(?, ?, ?)
             ON CONFLICT(install) DO UPDATE SET
               first_seen = MIN(first_seen, excluded.first_seen),
               last_seen = MAX(last_seen, excluded.last_seen)`,
            ev.install, ev.t, ev.t);
        }
      }
    });

    return json({ accepted: events.length }, 202);
  }

  // ---- GET /stats -----------------------------------------------------------------------

  private handleStats(url: URL): Response {
    const daysParam = Number(url.searchParams.get("days") ?? DEFAULT_STATS_DAYS);
    const days = Number.isInteger(daysParam) && daysParam >= 1 && daysParam <= MAX_STATS_DAYS
      ? daysParam : DEFAULT_STATS_DAYS;
    const now = Math.floor(Date.now() / 1000);
    const since = now - days * 86_400;

    const one = <T extends Record<string, SqlStorageValue>>(q: string, ...b: unknown[]) =>
      this.sql.exec<T>(q, ...b).toArray()[0];
    const all = <T extends Record<string, SqlStorageValue>>(q: string, ...b: unknown[]) =>
      this.sql.exec<T>(q, ...b).toArray();

    const day = `date(t, 'unixepoch')`;
    const launchesPerDay = all(
      `SELECT ${day} AS day, COUNT(*) AS launches, COUNT(DISTINCT install) AS installs
       FROM events WHERE name = 'app_launch' AND t >= ? GROUP BY day ORDER BY day`, since);
    const newInstallsPerDay = all(
      `SELECT date(first_seen, 'unixepoch') AS day, COUNT(*) AS installs
       FROM installs WHERE first_seen >= ? GROUP BY day ORDER BY day`, since);

    const activeSince = (secondsAgo: number) =>
      (one<{ n: number }>(`SELECT COUNT(*) AS n FROM installs WHERE last_seen >= ?`, now - secondsAgo)?.n ?? 0);
    const uniqueInstalls = {
      d1: activeSince(86_400),
      d7: activeSince(7 * 86_400),
      d30: activeSince(30 * 86_400),
      lifetime: one<{ n: number }>(`SELECT COUNT(*) AS n FROM installs`)?.n ?? 0,
    };

    const sessions = one(
      `SELECT COUNT(*) AS launches,
              AVG(json_extract(props, '$.prev_session_seconds')) AS avg_prev_session_seconds,
              SUM(CASE WHEN json_extract(props, '$.prev_session_clean') = 0 THEN 1 ELSE 0 END) AS unclean,
              SUM(CASE WHEN json_extract(props, '$.prev_session_clean') IS NOT NULL THEN 1 ELSE 0 END) AS reported
       FROM events WHERE name = 'app_launch' AND t >= ?`, since);

    const count = (name: string) =>
      one<{ n: number }>(`SELECT COUNT(*) AS n FROM events WHERE name = ? AND t >= ?`, name, since)?.n ?? 0;
    const byProp = (name: string, prop: string) => {
      const out: Record<string, number> = {};
      for (const r of all<{ k: SqlStorageValue; n: number }>(
        `SELECT json_extract(props, ?) AS k, COUNT(*) AS n FROM events
         WHERE name = ? AND t >= ? GROUP BY k ORDER BY n DESC`, `$.${prop}`, name, since)) {
        const key = r.k === null ? "(none)" : BOOL_PROPS.has(prop) ? String(r.k === 1) : String(r.k);
        out[key] = r.n;
      }
      return out;
    };
    // Histogram over the elements of an array prop (factions, AI profiles, per-slot systems).
    const byArrayItem = (name: string, prop: string) => {
      const out: Record<string, number> = {};
      for (const r of all<{ k: SqlStorageValue; n: number }>(
        `SELECT j.value AS k, COUNT(*) AS n FROM events, json_each(events.props, ?) AS j
         WHERE events.name = ? AND events.t >= ? GROUP BY k ORDER BY n DESC`, `$.${prop}`, name, since)) {
        out[String(r.k)] = r.n;
      }
      return out;
    };
    const byColumn = (column: "ver" | "os" | "arch") => {
      const out: Record<string, number> = {};
      for (const r of all<{ k: string; n: number }>(
        `SELECT ${column} AS k, COUNT(DISTINCT install) AS n FROM events
         WHERE name = 'app_launch' AND t >= ? GROUP BY k ORDER BY n DESC`, since)) {
        out[r.k] = r.n;
      }
      return out;
    };

    const gameAverages = one(
      `SELECT AVG(json_extract(props, '$.rounds')) AS avg_rounds,
              AVG(json_extract(props, '$.seconds')) AS avg_seconds
       FROM events WHERE name = 'game_end' AND t >= ?`, since);
    const mode = all<{ k: string; n: number }>(
      `SELECT CASE
                WHEN json_extract(props, '$.remote') > 0 AND json_extract(props, '$.ais') > 0 THEN 'network+ai'
                WHEN json_extract(props, '$.remote') > 0 THEN 'network'
                WHEN json_extract(props, '$.ais') > 0 THEN 'vs-ai'
                ELSE 'local-only'
              END AS k, COUNT(*) AS n
       FROM events WHERE name = 'game_start' AND t >= ? GROUP BY k ORDER BY n DESC`, since);

    return json({
      window: { days, since: new Date(since * 1000).toISOString() },
      launches_per_day: launchesPerDay,
      new_installs_per_day: newInstallsPerDay,
      unique_installs: uniqueInstalls,
      sessions,
      games: {
        started: count("game_start"),
        joined: count("game_join"),
        finished_by_outcome: byProp("game_end", "outcome"),
        winner: byProp("game_end", "winner"),
        abandoned_by_reason: byProp("game_abandon", "reason"),
        ...gameAverages,
      },
      mode: Object.fromEntries(mode.map(r => [r.k, r.n])),
      points: byProp("game_start", "points"),
      system: byProp("game_start", "system"),
      players: byProp("game_start", "players"),
      factions: byArrayItem("game_start", "factions"),
      join_factions: byProp("game_join", "faction"),
      ai_profiles: byArrayItem("game_start", "ai_profiles"),
      terrain_mode: byProp("game_start", "terrain_mode"),
      objective_mode: byProp("game_start", "objective_mode"),
      randomness: byProp("game_start", "randomness"),
      turn_style: byProp("game_start", "turn_style"),
      shooting_mode: byProp("game_start", "shooting_mode"),
      listed: byProp("game_start", "listed"),
      resume: byProp("game_start", "resume"),
      cover_exceptions: byProp("game_start", "cover_exceptions"),
      see_through_friendlies: byProp("game_start", "see_through_friendlies"),
      unlimited_split_fire: byProp("game_start", "unlimited_split_fire"),
      features: byProp("feature_open", "feature"),
      versions: byColumn("ver"),
      os: byColumn("os"),
      arch: byColumn("arch"),
    }, 200);
  }

  // ---- DELETE /stats/smoke -------------------------------------------------------------

  /** Removes smoke.sh's own rows (SMOKE_INSTALL) and nothing else. */
  private handleDeleteSmoke(): Response {
    let events = 0;
    this.state.storage.transactionSync(() => {
      events = this.sql.exec(`DELETE FROM events WHERE install = ?`, SMOKE_INSTALL).rowsWritten;
      this.sql.exec(`DELETE FROM installs WHERE install = ?`, SMOKE_INSTALL);
    });
    return json({ ok: true, deleted: events }, 200);
  }

  // ---- DELETE /stats --------------------------------------------------------------------

  /** Starts the stats over: every event, install and daily count (e.g. dev-test rows before a
   *  release). The confirm value guards against a stray DELETE on the summary URL. */
  private handleWipe(url: URL): Response {
    if (url.searchParams.get("confirm") !== WIPE_CONFIRM) {
      return json({ error: `add ?confirm=${WIPE_CONFIRM} to delete all analytics` }, 400);
    }
    let events = 0;
    this.state.storage.transactionSync(() => {
      events = this.sql.exec(`DELETE FROM events`).rowsWritten;
      this.sql.exec(`DELETE FROM installs`);
      this.sql.exec(`DELETE FROM daily`);
    });
    return json({ ok: true, deleted: events }, 200);
  }

  // ---- GET /stats/export ----------------------------------------------------------------

  private handleExport(url: URL): Response {
    const after = Math.max(0, Number.parseInt(url.searchParams.get("after") ?? "0", 10) || 0);
    const limitParam = Number.parseInt(url.searchParams.get("limit") ?? String(MAX_EXPORT_ROWS), 10);
    const limit = Number.isFinite(limitParam) && limitParam >= 1 ? Math.min(limitParam, MAX_EXPORT_ROWS) : MAX_EXPORT_ROWS;
    const format = url.searchParams.get("format") === "json" ? "json" : "csv";

    // newest=1: the most recent rows, newest first (a quick look, and how smoke.sh finds its own
    // rows on a big production table). Otherwise ascending from `after`, the paging cursor.
    const newest = url.searchParams.get("newest") === "1";
    const rows = this.sql.exec<Record<string, SqlStorageValue>>(newest
      ? `SELECT id, received, t, name, install, session, game, ver, os, arch, props
         FROM events ORDER BY id DESC LIMIT ?`
      : `SELECT id, received, t, name, install, session, game, ver, os, arch, props
         FROM events WHERE id > ? ORDER BY id LIMIT ?`, ...(newest ? [limit] : [after, limit])).toArray();

    if (format === "json") {
      return json({ rows: rows.map(r => ({ ...r, props: JSON.parse(String(r.props)) })) }, 200);
    }
    const columns = ["id", "received", "t", "name", "install", "session", "game", "ver", "os", "arch", "props"];
    const csvCell = (v: SqlStorageValue) => {
      if (v === null) return "";
      const s = String(v);
      return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
    };
    const lines = [columns.join(",")];
    for (const r of rows) lines.push(columns.map(c => csvCell(r[c] ?? null)).join(","));
    return new Response(lines.join("\n") + "\n", { status: 200, headers: { "Content-Type": "text/csv" } });
  }
}
