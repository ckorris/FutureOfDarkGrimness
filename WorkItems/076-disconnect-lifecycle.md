# 076 — Disconnect lifecycle (fail pending + surface)

**Status**: done (fail-and-surface scope; AI takeover deferred)
**Related**: audit §6/§10; branch `082-network-robustness` (third of #082/#075/#076/#077); composes with #075 (a rejected client self-disconnects, exercising this path) and #088 (per-player request routing)

## Goal

A dropped client was just removed from the lobby roster; its pending decision requests in `RequestMessageSender._pendingTaskAndResolvers` stayed forever (no timeout/cancellation), so a stage awaiting that player stalled the whole game. Fail a departed player's pending requests with a distinct, typed signal so the awaiting stage code stops hanging and the disconnect is visible. **AI takeover of the slot deferred** (user-chosen scope).

## Decisions

- **2026-06-25** — **Route the disconnect through `IMessageBusHost`, not `INetworkHost`.** `RequestMessageSender` already depends on `IMessageBusHost` (and the `PlayerSlotManager`), but not on the raw network host. Added `event Action<ConnectionID>? OnClientDisconnected` to `IMessageBusHost`; `MessageBusHost_Networked` forwards it straight from `INetworkHost.OnClientDisconnected`. The sender subscribes directly — no `FDGServer` plumbing change. Ripple: the app's `LocalMessageBus` and the test `MockMessageBusHost` implement the new event (both no-op / test-only; in-process play has no disconnects).
- **2026-06-25** — **Map connection→player via the game's `PlayerSlotManager`, not the lobby roster.** The lobby's `OnClientDisconnected` removal touches `_playerInfosFull` (lobby phase); the running game's slot manager is separate and still holds the live `NetworkPlayerController`s. Exposed `NetworkPlayerController.ConnectionID` and added `PlayerSlotManager.TryGetPlayerIDByConnection`. A connection with no game slot (spectator / already torn down) maps to nothing and is ignored.
- **2026-06-25** — **Typed `PlayerDisconnectedException` (carries the `PlayerID`), faulted directly onto the awaiting TCS** via a new `FailWithException` closure stored per pending task — distinct from the existing `NetworkedRequestFailedException` (a remote *error reply*). Each task is atomically claimed with the same `TryRemove` the reply/error handlers use, so a reply racing the disconnect still resolves exactly once (`TrySetException`, belt-and-suspenders). The fault propagates up the awaited transition chain (#083) to `FDGServer`'s top-level handler; `FailPendingRequestsForPlayer` also `Log`s a one-line "Player X disconnected — failed N pending request(s)" and broadcasts `StageTaskNotifyResolvedMessage` so every client clears the task from its outstanding-task UI.
- **2026-06-25** — **No `RequestDecision` timeout added.** A timeout is a separate safety net (covers a wedged-but-connected client, not a disconnect) and would need a tuned duration; out of scope for the fail-and-surface fix. Noted as a possible follow-up.
- **2026-06-25** — **AI takeover deferred** (user-chosen "fail + surface" scope). Swapping a dropped slot's controller/resolver to AI and re-resolving the pending request is the richer behavior; the resolver architecture makes it feasible later. The typed exception + the connection→player mapping are the seam it would build on.

## Notes

- **2026-06-25** — Implemented + verified. Engine suite **780/0**, full `dotnet build` clean, headless exit 0.
  - Engine: `MessageBus/IMessageBusHost.cs` (+`OnClientDisconnected`); `MessageBus/MessageBusHost_Networked.cs` (forward from network host); `Players/NetworkPlayerController.cs` (expose `ConnectionID`); `Players/PlayerSlotManager.cs` (`TryGetPlayerIDByConnection`); `StageResolution/RequestMessageSender.cs` (subscribe; per-task `TargetPlayerID` + `FailWithException`; `OnClientDisconnected` handler; `FailPendingRequestsForPlayer`; `PlayerDisconnectedException`; Dispose deregister).
  - App: `FdgRaylib/Cli/LocalMessageBus.cs` (no-op event to satisfy the interface).
  - Tests: `Tests/DisconnectLifecycleTests.cs` (4 — direct fail faults that player only; full path via `NetworkPlayerController` + simulated disconnect; unknown connection no-op) + `Tests/RequestSystemTests.cs` mock gains the event + `SimulateClientDisconnected`.
  - End-to-end over a real socket belongs to the #065 loopback fixture.

## Outcome

A disconnected player's pending decision requests now fail with a typed `PlayerDisconnectedException` (logged, task cleared from clients' UI) instead of hanging the game; the connection→player mapping and typed exception are the seam a future AI-takeover (#076 follow-up) would use. Deferred: AI takeover, a `RequestDecision` timeout, and the socket-level loopback test (#065).
