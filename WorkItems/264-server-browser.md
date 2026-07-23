# 264 — Server browser (public game listing via a master list server)

**Status**: in-progress (P1-P3 implemented + locally verified on branch `264-server-browser`)
**Related**: #189 (configurable port — soft prerequisite), #186 (wire deserialization hardening — should land before advertising servers to strangers), #065 (networking tests), #075 handshake (`NewLobbyClientGreeting` / `NetworkProtocol.TryValidateJoin`), #226 (bug reporting — could share the same Worker later)

## Goal

An old-school server browser: a host can tick "List publicly" when creating a lobby; other players
open a Browse screen, see live servers (name, players, password lock, compatibility), click one, and
land in the existing ClientModal connect flow with the address pre-filled. Runs on infrastructure
cheap enough to forget about forever (target: $0/mo flat, independent of player count). Actual game
traffic stays peer-to-peer over the existing `FDGHost`/`FDGClient` TCP path — the list server never
relays gameplay.

## Architecture (two pieces, only one is new infrastructure)

```
 Host machine                     List server (Cloudflare Worker)          Client machine
 ------------                     -------------------------------          --------------
 FDGHost (TCP :6389)  <-------------------------------------------------  FDGClient connect
       |                                                                        ^
       |  POST /servers every ~30s        GET /servers                          |
       +----------------------------->  [registry, TTL 90s]  <---- Browse screen
                                        observed public IP                 pre-fills address
```

- **List server**: tiny HTTP registry. Hosts heartbeat; entries expire on TTL. It records the
  *observed* source IP (never trusts a client-supplied one) and can optionally TCP-probe the host's
  port to mark it "verified reachable" (Workers support outbound `connect()`).
- **Game hosting**: unchanged. `FDGHost` listens, `FDGClient` connects directly. No relay, no
  per-game server cost — the only always-on component is the registry, whose load is a few hundred
  bytes per listed host per 30s. Cost is flat regardless of popularity.

## List server API sketch

All JSON over HTTPS. Worker + one storage binding (Durable Object or D1 — both on the free plan).

| Verb | Path | Body / Query | Returns |
|---|---|---|---|
| `POST` | `/servers` | `{name, port, protocolVersion, typeMapHash, hasPassword, playerCount, maxPlayers, state}` (+ `serverId`,`token` on renew) | `{serverId, token, publicIp, ttlSeconds}` |
| `GET` | `/servers` | `?protocolVersion=N` (optional filter) | `[{serverId, name, host, port, playerCount, maxPlayers, hasPassword, state, protocolVersion, typeMapHash, reachable, ageSeconds}]` |
| `DELETE` | `/servers/{id}` | header `X-Token` | 204 |

Semantics:
- First `POST` = register: server generates `serverId` + random `token`; subsequent `POST`s with
  both = heartbeat/update (player count, lobby -> in-game state). Wrong token = 403 (prevents
  listing hijack).
- TTL ~90s (3 missed heartbeats) — crash-safe expiry; `DELETE` is just the polite fast path.
- `host` in listings = the IP the Worker *observed* on the heartbeat (`CF-Connecting-IP`). Client
  never gets to claim an address, so the list can't be poisoned with someone else's IP.
- `state`: `lobby` | `in-game`. Browser greys or hides in-game entries (post-launch join is already
  gated app-side, QF6).
- Abuse guards: name length clamp + ASCII fold (reuse the game's ASCII-only convention), max ~1
  registration per IP per few seconds, cap total entries, cap entries per IP (~4).
- Compatibility: entries carry `protocolVersion` + `typeMapHash` straight from the #075 handshake
  vocabulary (`NetworkProtocol` / `NewLobbyClientGreeting`), so the browser can mark incompatible
  servers *before* a doomed connect attempt — same check `TryValidateJoin` does, done early.
- Passwords never leave the host; only `hasPassword` is listed. Join prompts as today.
- ~150 lines of TypeScript. No shared C# types needed — the wire is plain JSON, and keeping the
  Worker dependency-free beats reusing engine serialization here.

## App-side changes (all in FdgRaylib; no engine changes anticipated)

1. **Host side** — `HostModal` gains a "List publicly" checkbox (default off). When on, after
   `FDGHost.StartAsync()`, start a background heartbeat loop (plain `HttpClient`); read live player
   count from the lobby view model; flip `state` to `in-game` on launch; `DELETE` + stop on lobby
   close/app exit. Failure-tolerant: registry down = log once, keep hosting.
2. **Client side** — a Browse surface listing servers: name, players `2/4`, lock marker for
   password, compatibility, reachable flag, age; Refresh button; Join hands the address:port to the
   existing `AttemptConnect` path (which already does the #075 accept/reject + timeout properly).
3. **Config** — master-server base URL in a small app config (not hardcoded), so shipped builds can
   be repointed without a rebuild if the URL ever changes. Empty URL = feature hidden entirely.
4. **Port** — phase 1 can assume `CommandProtocol.TEMP_PORT` (6389) but the API carries `port` from
   day one; #189 (configurable port) slots in cleanly later.

## NAT stance (the one hard problem)

The transport is raw TCP, and TCP hole-punching is unreliable — so classic NAT traversal is out
unless the transport changes or a relay exists. v1 stance: **direct connect only, honestly labeled.**

- The Worker's reachability probe tells hosts *at listing time* whether their port is open, so
  "nobody can join me" becomes visible immediately ("Port 6389 unreachable - see port forwarding
  help") instead of a mystery.
- Escape hatches that cost nothing: port forwarding (the 2003 answer), or Tailscale/ZeroTier
  (already acknowledged in the ClientModal QF10 comment).
- Deferred forks if the game ever grows: a TURN-style relay (the only path with real per-player
  cost — deliberately out of scope), or migrating matchmaking+transport to Epic Online Services
  (free including relays, but an SDK dependency and a transport rewrite; noted, not chosen).

## Cost & ops

- **Cloudflare Workers free tier**: 100k requests/day. Worst realistic case (20 listed servers
  heartbeating at 30s + browsers polling) is a few thousand/day. **$0/mo, flat.**
- Optional custom domain ~$10/yr; the free `*.workers.dev` URL is fine (config-file indirection
  covers a future move).
- Ops burden: none day-to-day (no VM, no OS patching). The Worker code should live in this repo
  (e.g. `tools/list-server/`) with a one-command `wrangler deploy`.
- Kill switch: deleting the Worker (or blanking the config URL) degrades gracefully to today's
  direct-IP flow.

## Phases (each a vertical slice, in order)

- **P0 — sign-off** on the design forks below. Nothing built before this.
- **P1 — list server**: Worker + storage + the 3 endpoints + TTL + abuse guards; deploy;
  `curl`-level smoke + a tiny test script.
- **P2 — host registration**: checkbox + heartbeat loop + deregister; verify an entry appears/
  expires via `curl`.
- **P3 — browser UI**: Browse surface -> pre-filled connect; end-to-end join via the listing on LAN.
- **P4 — reachability + NAT UX**: Worker TCP probe, host-side "unreachable" warning with help text;
  live two-machine internet test.
- **Deferred (explicit, not silent)**: relay fallback; EOS migration; listing filters/sort beyond
  compatibility; server-side stats. #186 hardening is tracked separately but should precede any
  real public announcement of the feature.

## Design forks (need sign-off — none chosen yet)

1. **Registry provider**: Cloudflare Worker (recommended: $0, no ops, TCP probe support) vs $4-6/mo
   VPS (full ownership, but an OS to patch forever) vs EOS (also $0 and solves NAT, but SDK + lock-in
   + transport rewrite).
2. **Browse surface**: new `ServerBrowserScreen` off the main menu ("Browse Games") vs a
   Browse/Direct tab pair inside `ClientModal`. Recommendation: tab inside ClientModal — smaller
   diff, one connect flow, matches the modal-based menu graph.
3. **v1 NAT stance**: direct-connect + reachability badge + docs (recommended) vs building relay
   support now.
4. **Worker language**: TypeScript (recommended) vs a C# ASP.NET registry (only makes sense on the
   VPS path).

## Notes

- 2026-07-23 (later): P1-P3 implemented on `264-server-browser`:
  - **P1** `tools/list-server/` — Worker + single `Registry` Durable Object (SQLite class, free
    plan), all three endpoints, 90s TTL lazy sweep, token auth, per-IP rate limit (3s) + caps
    (4/IP, 200 total), 4KB body cap, ASCII name fold, observed-IP-only listing + probe.
    Verified: `npm run typecheck` clean; `smoke.sh` (18 asserts incl. token-leak, rate-limit,
    validation, delete) green against `wrangler dev`.
  - **P2** `FdgRaylib/ListServer/` — `ListServerConfig` (env var -> `listserver.url` file ->
    compiled default; empty = feature hidden), `ListServerClient` (System.Text.Json plain DTOs,
    deliberately no Newtonsoft/$type on this internet-facing path), `PublicListingService`
    (30s heartbeat, re-register on 404, in-game state flip via `OnLaunched`, best-effort delist
    on dispose). HostModal "List publicly" checkbox (label warns the IP becomes visible);
    Program.cs disposes on lobby back / game exit (`RaylibRenderer.OnGameExited`, new seam at the
    end of `ExitGame`) / app exit.
  - **P3** ClientModal — when a list server is configured the modal becomes "JOIN GAME" with the
    SERVER BROWSER as the default tab (Server/Players/Access/Build/Port columns, 15s
    auto-refresh, compat check against `NetworkProtocol.Version` + `LocalTypeMapHash`, in-game
    rows disabled) and DIRECT CONNECT as the second tab; unconfigured builds render exactly the
    old modal. Locked servers get an inline password popup (popup opened at dialog scope — an
    `OpenPopup` inside the row's `PushID` never matches).
  - Verified: engine suite 1991/1991 green; headless smoke exit 0; a scratch C# harness ran the
    real `ListServerClient` against `wrangler dev` end-to-end (register -> list -> heartbeat
    state/count update -> delete) — CONTRACT CHECK PASSED.
- 2026-07-23: Filed. Design + API sketch + cost analysis written. Grounding checked in-repo:
  `FDGHost` TCP on `CommandProtocol.TEMP_PORT` 6389 (hardcoded, #189); #075 greeting carries
  `ProtocolVersion` + `TypeMapHash`; ClientModal already resolves DNS names and runs the
  accept/reject handshake with timeout — the browser only needs to feed it an address.

## Remaining (explicit, not silently dropped)

- **Deploy**: owner runs `npx wrangler login` + `npx wrangler deploy` (tools/list-server/README),
  then bakes the printed URL into `ListServerConfig.DefaultBaseUrl` (or ships `listserver.url`).
  Until then the feature is invisible in shipped builds. Dev testing: `FDG_LIST_SERVER_URL=http://localhost:8787`.
- **P4 status surface**: `PublicListingService.Status` (incl. the "port unreachable" warning from
  the probe) is computed but not yet shown anywhere — needs a line on the lobby screen. Port
  forwarding help text also unwritten.
- **GUI hand-verify** (no display in the implementing session): host checkbox flow, browse tab
  layout/join, password popup, teardown on all exit paths.
- **Live two-machine internet test** after deploy (reachability probe result on a real WAN host).
- Loaded-game lobbies (`LoadGameFlow`) are never listed — deliberate v1 scope.
- `maxPlayers` advertised as a constant 8 (the #221 color-palette ceiling); no real lobby cap exists.
- Security follow-ups filed separately: #265 (untrusted content files, open), #266 (FDGHost
  pre-auth limits — DONE 2026-07-23, engine `842c43b`); #186 elevated to a prerequisite for
  announcing public listing.

## Decisions

- Forks resolved by owner 2026-07-23: Cloudflare Worker + TypeScript; browser is the DEFAULT join
  surface (direct connect demoted to second tab); v1 NAT stance = direct connect + honest
  reachability labeling, no relay.
- Password entry for locked listings is an inline popup on the browse tab, not a jump to the
  direct tab — the installed ImGui.NET lacks the `BeginTabItem(label, flags)` overload needed for
  a programmatic `SetSelected` switch, and the popup is better UX anyway.
- `package-lock.json` is committed (pins wrangler/toolchain — supply-chain hygiene).
- Listing keeps heartbeating during play with `state: "in-game"` so the browser can show the
  server as occupied rather than having it vanish.

## Outcome

(open)
