# 036 — Server readiness handshake

**Status**: done
**Related**: #037, #038 (same branch `036-037-readiness-concurrent`); #082 (network robustness); #076 (disconnect lifecycle)

## Goal
The host must not enter the state machine and start requesting decisions until every assigned player slot is connected and ready to answer — otherwise the host could send a request before a client is wired up to receive it. The old `FDGServer.cs` TODO ("Wait for all clients to indicate that they are connected and ready") and a "Half a second…" comment implied this was a stub.

## Notes
- 2026-06-28: **Investigated — the handshake was already functionally implemented; only the comments were stale.** `FDGServer.LaunchStateMachineOnceReady` already `await`s `PlayerSlotManager.WaitUntilAllSlotsReady()`, which fans out to each slot's `IPlayerController.WaitUntilReadyAsync()`:
  - `NetworkPlayerController` completes when its client sends `PostLaunchPlayerReadyMessage` — which `FDGGame_AsClient.AssignInterfaces` (line 80) genuinely sends once the client has built its resolver registry post-launch.
  - `LocalPlayerController` completes when its stage-resolver registry is assigned.
  - `ComputerPlayerController` is ready immediately (`Task.CompletedTask`).
  There is no `Task.Delay(500)` anywhere in the path — the "Half a second" comment predated the real `WaitUntilReadyAsync` implementations (built under/around #082).
- 2026-06-28: Replaced the stale TODO/"half a second" comments on both launch paths (`LaunchStateMachineOnceReady` + `LaunchSingleTurnTester`) with an accurate description of the readiness contract. Added `Tests/PlayerReadinessTests.cs` (3 tests) pinning `WaitUntilAllSlotsReady`: blocks until every slot is ready, completes immediately when all already ready, throws when a slot is unfilled. Engine suite 843/0, full build clean, headless smoke exit 0.

## Decisions
- Treated this as a **stale-comment cleanup + regression-test** item, not a from-scratch implementation, because the behavior the TODO describes already holds end-to-end. Verified the client actually sends the ready message before concluding.
- **Deferred (explicitly, not silently):** a timeout / "client never sends ready" safety net, and any distinction between "connected" and "ready" beyond the resolver-assignment proxy. The existing TODO only asks to *wait for ready*, which is done; a timeout would be net-new and overlaps the #076 disconnect lifecycle. Flagged as a possible follow-up rather than built unasked.

## Outcome
Closed by confirming the readiness handshake works end-to-end, replacing the misleading stale comments with an accurate description, and adding `PlayerReadinessTests` to pin the `WaitUntilAllSlotsReady` contract so a future regression fails CI. No behavioral change. Timeout/never-ready safety net deferred (possible follow-up).
