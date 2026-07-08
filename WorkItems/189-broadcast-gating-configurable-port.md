# 189 — Broadcast gating + configurable port

**Status**: todo
**Related**: QF2 (greeting timeout), CommandProtocol.TEMP_PORT, NetworkingHandoff-2026-07-08.md

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
- 2026-07-08: Filed. Neither blocks the first shared build (QF2 bounds the un-greeted window; 6389 is fine
  as a default), hence deferred.

## Decisions
- (none yet)

## Outcome
(pending)
