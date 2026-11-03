# 189 — Broadcast gating + configurable port

**Status**: done (awaiting GUI hand-verify on the port fields)
**Related**: QF2 (greeting timeout), #273 (the IsAuthenticated flag broadcast gating reuses), #271
(public listing now advertises the chosen port), NetworkingHandoff-2026-07-08.md. Engine commit
`46f387d` + superproject bump.

## Goal
Two loose ends from the QF pass:
1. **Broadcast gating** — `FDGHost.SendCommandToAllAsync` broadcasts to every entry in `_connectedClients`,
   not to the greeted roster. A connection that hasn't greeted yet (QF2 evicts it after 10s, but until then)
   still receives chat, roster, and — once in-game — replicated game data. Done = broadcasts go only to
   roster members (greeted, accepted connections).
2. **Configurable port** — `CommandProtocol.TEMP_PORT` is a hardcoded `const` (6389) with a TODO. Done =
   host can set the listen port and client can set the connect port (surfaced in HostModal / ClientModal),
   so players behind a fixed-port conflict aren't stuck.

## Notes
- 2026-07-23: Implemented both halves (engine `46f387d` + superproject bump). Full suite 2015/2015;
  app build + headless smoke clean. GUI port fields await hand-verify (no display in this session).
- 2026-07-08: Filed. Neither blocks the first shared build (QF2 bounds the un-greeted window; 6389 is fine
  as a default), hence deferred.

## Decisions
- **Broadcast gating reuses #273's `IsAuthenticated`.** "Greeted, accepted roster member" IS exactly
  the flag set at greeting acceptance (which also lifts the pre-auth frame cap). `SendCommandToAllAsync`
  skips un-authenticated connections. The join handshake uses targeted single-sends, and
  `MarkClientAuthenticated` runs before the host's post-accept broadcasts, so a real client never
  misses roster/settings. Verified by two real-TCP tests (un-auth receives nothing; authed receives).
- **One public default port.** Added `NetworkProtocol.DefaultPort` (public) as the single source of
  truth; the engine-internal `CommandProtocol.TEMP_PORT` now aliases it (the app was previously
  restating 6389 in comments because TEMP_PORT was internal). `FDGClient.ConnectAsync` gained an
  optional `port`; `FDGHost` already had one (#273).
- **Port UI**: a plain "Port" field in both modals (default 6389, validated 1024-65535 - sub-1024 is
  privileged/reserved). The server browser auto-fills the port from the listing (`listing.Port`), so
  a manual entry is only needed for a direct connect to a non-default port. `PublicListingService`
  advertises the actual chosen listen port instead of a hardcoded 6389. No fork surfaced: the work
  item itself specified "surfaced in HostModal / ClientModal," and a single validated numeric field
  is routine UI.
- **Scope boundary (recorded, not silently cut)**: the Load Game -> resume-as-host path
  (`Program.cs` LoadGameFlow) still hosts on the default port - it has no port UI (file dialog
  straight to lobby). Fine for now; a resumed host on 6389 is the expected default.

## Outcome
Shipped engine `46f387d` + superproject bump. Broadcasts now reach only the authenticated roster
(closes the scanner/pre-greet info-leak that paired with #271's public listing); the listen/connect
port is player-configurable in both modals, plumbed through a single public `NetworkProtocol.DefaultPort`,
advertised truthfully by the #271 listing, and auto-filled by the browser on join. 2 new real-TCP
broadcast tests; full suite 2015/2015. Remaining: GUI hand-verify of the two port fields (host with a
non-default port + client connect; browser join to a non-default-port host).
