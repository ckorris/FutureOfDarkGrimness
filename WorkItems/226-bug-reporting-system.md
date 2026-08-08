# 226 — In-app bug reporting system

**Status**: done (closed 2026-08-05)
**Related**: #271 (list server — reports ride the same Worker), #054 (clients can't save — client reports are log-only), #187 (RecoverySave — template for the local bundle writer)

## Goal
Give the user (and future testers) a way to report a bug from inside the app rather than out-of-band. Not yet scoped to a mechanism — options include an in-app "report bug" action that dumps recent log/state to a file for the user to send, versus something that files directly to a tracker. Surface the design fork before building.

## Notes
- 2026-08-05 (later): Local `wrangler dev` + `./smoke.sh` SMOKE PASSED (all 14 sections, needed Node 22 via NodeSource - apt ships 18), and owner hand-verified the GUI flow. **Remaining: deploy** (`npx wrangler deploy` + one-time `npx wrangler secret put ADMIN_TOKEN`, then prod smoke). Known cosmetic quirk: workerd logs an uncaught "Can't read from request stream" after 429 responses (body returned unconsumed) - pre-existing on the registry's 429 too, clients unaffected.
- 2026-08-05: Both slices landed (9ca9d7b Worker, bb0854a app). Verified: engine 2862 green, app 1116 green (8 new in `BugReportTests`), full build, headless smoke exit 0.
- 2026-08-05: Design agreed with owner (see Decisions). Building: Worker slice first, then app slice.
- 2026-07-15: Filed from user playtest feedback. No mechanism decided yet.

## Decisions
- 2026-08-05 (owner): **Hybrid local + upload, riding the existing #271 Cloudflare Worker.** No new paid service. "Report a Bug" in the escape menu -> description box -> always writes a local JSON bundle (`BugReports/` beside the exe, mirroring RecoverySave), then fire-and-forget uploads the gzipped bundle to `POST /reports` on the list-server Worker. Local-first, so the report survives an offline/failed upload.
- 2026-08-05 (owner): **Retrieval is pull, not push.** `GET /reports` / `GET /reports/{id}` / `DELETE /reports/{id}` behind an `ADMIN_TOKEN` wrangler secret; a `fetch-reports.sh` script downloads them. GitHub-issue push notification is a possible later slice, deliberately not built now.
- 2026-08-05: **Bundle contents**: description, app version stamp, protocol version, platform, player name, merged log+chat snapshot (incl. debug lines), crash.log tail if present, and the full `.fdgsave` JSON on the host. Clients are log-only (#054) — recorded, not a surprise.
- 2026-08-05: **Abuse posture** (same philosophy as #271): ~1MB compressed body cap, decompressed-size cap, per-IP min-interval rate limit, and a hard total-storage cap that REJECTS new reports rather than evicting stored ones (a flood must not delete real reports). Free-tier only; storage in a second SQLite DO class, no R2 (R2 wants a payment card).
- 2026-08-05 (owner caught this): **Nothing secret or player-owned lives in the repo.** The production `ADMIN_TOKEN` only ever goes through `wrangler secret put` (encrypted at Cloudflare, never written to disk). `.dev.vars` is untracked and gitignored with a committed `.dev.vars.example` to copy - the first cut committed it, which leaked nothing (the value is a fake that only guards localhost, so no history scrub is needed) but invited someone to later paste the real token into a tracked file. `tools/list-server/reports/` is gitignored too: fetched bundles carry other people's player names, army lists and saves. Don't re-track either.
- 2026-08-05: **Build stamp folded in**: `InformationalVersion` stamped by `build-dist.sh` from git; local builds report a dev version. Without it reports can't be tied to a binary.

## Next steps (deploy + end-to-end test) - all that remains to close this item
All from `~/Projects/fdg-raylib_Green/tools/list-server` (needs the Node 22 installed 2026-08-05):

1. ~~Deploy the Worker with the new /reports endpoints~~ **DONE 2026-08-05**, version
   `19f49225-8ccb-45e7-ad86-ded6fa0e9ebb`, both DO bindings live. Verified from outside:
   `GET /` 200, `GET /servers` 200 (registry survived the v2 migration), `GET /reports` 503
   "admin token not configured" - the fail-closed path, which local dev can't exercise because
   `.dev.vars` always supplies a token. NB the first request right after deploy hit a stale edge
   node and 404'd; it settled within seconds.
   ```bash
   npx wrangler deploy
   ```
2. One-time: set the admin token (generate with `openssl rand -hex 24`, keep it in your password manager - it is what fetch-reports.sh authenticates with):
   ```bash
   npx wrangler secret put ADMIN_TOKEN
   ```
3. Prod smoke (registers + cleans up its own test entries, safe on the live server):
   ```bash
   ./smoke.sh https://fdg-list-server.ckorris.workers.dev <admin-token>
   ```
4. End-to-end test: play any game, Esc -> Report a Bug -> type a fake bug -> Send.
   Expect "Report sent. Thank you!". Then fetch it:
   ```bash
   ADMIN_TOKEN=<admin-token> ./fetch-reports.sh https://fdg-list-server.ckorris.workers.dev
   ```
   The report lands in `tools/list-server/reports/<timestamp>-<id>.json` (description + log +
   embedded save). Add `--delete` to clear fetched reports off the server.
5. Then: write the Outcome here, tick the index line, move to Archive.

Caveats until step 1 is done: in-game Send against the live server fails with
"Upload failed (server answered 404)" - by design it still writes the local copy
(`BugReports/` beside the executable; `FdgRaylib/bin/Debug/net8.0/BugReports/` for dotnet run).
To test without deploying: `npx wrangler dev`, launch the game with
`FDG_LIST_SERVER_URL=http://localhost:8787`, fetch with `./fetch-reports.sh http://localhost:8787`
(dev admin token is the built-in default).

## Deferred facets (explicit, not silently cut)
- Reporting from CLI/headless mode and from non-game screens (main menu, army builder) — escape-menu only for now.
- GitHub-issue push notification on new report.
- Client-side save capture (blocked on #054).

## Outcome
Closed 2026-08-05. In-game bug reporting is live end to end: **Report a Bug** in the escape menu takes the player's notes, always writes a local bundle to `BugReports/` beside the executable, then fire-and-forget uploads it gzipped to `POST /reports` on the existing #271 Cloudflare Worker (no new service, free tier, `wrangler deploy` version `19f49225-8ccb-45e7-ad86-ded6fa0e9ebb`). Retrieval is pull: `fetch-reports.sh` with the `ADMIN_TOKEN` secret. Local-first ordering means an offline or failed upload still leaves a complete report on disk, and the status line says so honestly rather than claiming success.

The bundle carries description, build stamp, protocol version, platform, player name, the session's log and chat merged in true arrival order (newest 4000 lines), the crash.log tail, and - on the host only - the full save. A disclosure above the Send button itemizes every one of those, including the two that are not the reporter's own data (other players' chat, and their army lists inside the save) and the IP the drop box records.

Two things this item pulled in that were missing generally: an app **build stamp** (`InformationalVersion`, stamped from git by `build-dist.sh`, `dev` otherwise) - without it a report cannot be tied to a binary; and gitignoring `tools/list-server/reports/`, since fetched bundles carry other people's data.

Abuse posture mirrors #271's: uploads are unauthenticated by necessity, so 1MB compressed / 16MB decompressed caps, a per-IP interval, and total caps that REJECT rather than evict (a flood must not delete real reports). Reads are admin-token-only and fail CLOSED when the secret is unset - verified in production, where `/reports` answered 503 before the secret and 403 after.

Verification: engine 2862 green, app 1118 green (10 new in `BugReportTests`), full build, headless smoke exit 0, local `wrangler dev` smoke passed, and the deployed Worker passed `smoke.sh` against production (all 14 sections). GUI hand-verified by owner.

**Full round trip confirmed 2026-08-08**: a report filed from inside a live game uploaded to the deployed Worker and was pulled back with `fetch-reports.sh` (that script's first-ever run - it works). The fetched bundle was complete and well-formed: 138 merged log lines with debug and chat lines correctly tagged and interleaved in arrival order, a 234KB save that parses as the ordinary `Version`/`TypeMap`/`Entries` save shape (so a report is replayable through Load Game), platform, player name, protocol version 4. Size 311.8KB raw -> **20.1KB gzipped**, i.e. 2% of the 1MB cap, confirming the ~15:1 compression the whole design leans on.

Two things that surfaced from reading a real bundle:
- `appVersion` on a `dotnet run` build reads `dev+<full git sha>` - the SDK appends the source commit by itself, so even non-dist builds are traceable to a commit. Better than assumed when the stamp was designed.
- **`crashLog` carries stale crashes.** The real report's tail held crashes from 2026-07-27/28, twelve days before the session and unrelated to it, because `crash.log` is append-only across runs. Nothing is wrong, but a reader can easily mistake an old crash for the reported one. Worth either filtering to entries newer than process start or labelling the block - not done.

Deliberately not built (see Deferred facets): CLI/headless and non-game-screen reporting, GitHub-issue push notification, client-side save capture (blocked on #054).
