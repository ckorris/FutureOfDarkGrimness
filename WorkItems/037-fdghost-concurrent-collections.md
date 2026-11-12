# 037 — Replace non-concurrent collections in FDGHost

**Status**: done
**Related**: #036, #038 (same branch `036-037-readiness-concurrent`); #086 (per-connection write lock); #065 (deferred networking loopback tests)

## Goal
`FDGHost._connectedClients` was a plain `Dictionary<ConnectionID, ClientConnection>` guarded by manual `lock (_connectedClients)` blocks (two carried `//TODO: Change to concurrent collection.`). It's touched concurrently from the accept loop, every per-client read loop's disconnect cleanup, and broadcast/single sends. Swap it for a concurrent collection and drop the hand-rolled locking.

## Notes
- 2026-06-28: Replaced the field with `ConcurrentDictionary<ConnectionID, ClientConnection>` (added `using System.Collections.Concurrent;`) and removed all five `lock (_connectedClients)` blocks:
  - accept loop add → `TryAdd`
  - disconnect cleanup → `TryRemove(connectionID, out _)`
  - `SendCommandToSingleClientAsync` → `TryGetValue` (skips cleanly if the client vanished mid-send instead of throwing `KeyNotFoundException` into the catch)
  - `SendCommandToAllAsync` broadcast snapshot → iterate `Values` (already a moment-in-time snapshot)
  - `Stop()` → iterate `Values` + `Clear()` without a lock
  Per-connection outbound framing is still serialized by each `ClientConnection.WriteLock` (#086) — untouched. Engine suite 843/0, full build clean, headless smoke exit 0.

## Decisions
- `ConcurrentDictionary` over a `lock`-wrapped `Dictionary`: the original TODO asked for exactly this, and the access pattern (independent add/remove/lookup from several threads, plus snapshot-for-broadcast) is the textbook fit. `.Values` returning a snapshot removes the need to copy-under-lock before broadcasting.
- **No new automated test.** `FDGHost` opens real TCP sockets and has no test harness today; the socket-level loopback fixture is explicitly tracked as #065 (deferred). This is a behavior-preserving refactor covered by the existing suite + headless smoke; not silently skipped — recorded here.

## Outcome
`_connectedClients` is now a `ConcurrentDictionary` with no manual locking; the two stale TODOs are gone. Behavior-preserving (per-connection write serialization unchanged). Direct socket-level test left to #065.
