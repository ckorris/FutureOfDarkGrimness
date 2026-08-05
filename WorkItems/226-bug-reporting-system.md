# 226 — In-app bug reporting system

**Status**: in progress
**Related**: #271 (list server — reports ride the same Worker), #054 (clients can't save — client reports are log-only), #187 (RecoverySave — template for the local bundle writer)

## Goal
Give the user (and future testers) a way to report a bug from inside the app rather than out-of-band. Not yet scoped to a mechanism — options include an in-app "report bug" action that dumps recent log/state to a file for the user to send, versus something that files directly to a tracker. Surface the design fork before building.

## Notes
- 2026-08-05: Design agreed with owner (see Decisions). Building: Worker slice first, then app slice.
- 2026-07-15: Filed from user playtest feedback. No mechanism decided yet.

## Decisions
- 2026-08-05 (owner): **Hybrid local + upload, riding the existing #271 Cloudflare Worker.** No new paid service. "Report a Bug" in the escape menu -> description box -> always writes a local JSON bundle (`BugReports/` beside the exe, mirroring RecoverySave), then fire-and-forget uploads the gzipped bundle to `POST /reports` on the list-server Worker. Local-first, so the report survives an offline/failed upload.
- 2026-08-05 (owner): **Retrieval is pull, not push.** `GET /reports` / `GET /reports/{id}` / `DELETE /reports/{id}` behind an `ADMIN_TOKEN` wrangler secret; a `fetch-reports.sh` script downloads them. GitHub-issue push notification is a possible later slice, deliberately not built now.
- 2026-08-05: **Bundle contents**: description, app version stamp, protocol version, platform, player name, merged log+chat snapshot (incl. debug lines), crash.log tail if present, and the full `.fdgsave` JSON on the host. Clients are log-only (#054) — recorded, not a surprise.
- 2026-08-05: **Abuse posture** (same philosophy as #271): ~1MB compressed body cap, decompressed-size cap, per-IP min-interval rate limit, and a hard total-storage cap that REJECTS new reports rather than evicting stored ones (a flood must not delete real reports). Free-tier only; storage in a second SQLite DO class, no R2 (R2 wants a payment card).
- 2026-08-05: **Build stamp folded in**: `InformationalVersion` stamped by `build-dist.sh` from git; local builds report a dev version. Without it reports can't be tied to a binary.

## Deferred facets (explicit, not silently cut)
- Reporting from CLI/headless mode and from non-game screens (main menu, army builder) — escape-menu only for now.
- GitHub-issue push notification on new report.
- Client-side save capture (blocked on #054).

## Outcome
