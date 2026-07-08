# 188 — Multi-remote-client support (3+ players, 2+ remote clients)

**Status**: todo
**Related**: QF5 (targeted PlayerID assignment), QF6, NetworkingHandoff-2026-07-08.md

## Goal
Only host + one remote client (1v1) is trustworthy today. QF5 fixed the broadcast-PlayerID-assignment bug
(every client adopting the newest joiner's ID) by targeting the assignment, but the multi-client path has
never been live-tested. Done = a 3+ player game with 2+ remote clients works end-to-end: each client keeps
its own identity, roster order / team numbers are correct, the outstanding-task UI attributes waits to the
right players, and per-player request routing (#088) reaches the right client.

## Notes
- 2026-07-08: Filed. QF5 is the enabling fix; this is the verification + whatever edge cases it surfaces
  (team-number assignment is currently `_playerInfos.Value.Count + 1`, which may need rework for teams).

## Decisions
- (none yet)

## Outcome
(pending)
