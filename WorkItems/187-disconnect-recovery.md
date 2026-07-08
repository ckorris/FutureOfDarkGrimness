# 187 — Disconnect recovery (auto-save + live-test resume-rejoin)

**Status**: todo
**Related**: #052 (save/load), #076 (PlayerDisconnectedException), QF8, NetworkingHandoff-2026-07-08.md

## Goal
An internet game that drops a player currently ends with "Game error: ..." (the engine faults the awaiting
stage with `PlayerDisconnectedException`, `RequestMessageSender.cs`). Over the internet a transient drop is
common, so done = when a game ends via `PlayerDisconnectedException` the host auto-saves a recovery file,
AND the networked resume-rejoin flow (#052's `OnResumeClientGreeting`, currently marked "NOT yet
live-tested") is exercised end-to-end: host loads the recovery save, the dropped player reconnects, adopts
their saved slot, and play continues.

## Notes
- 2026-07-08: **Slice 1 (graceful player-left messaging) done.** Closing a client mid-game surfaced as a
  "Game error: PlayerDisconnectedException ..." Game Over popup + a console stack trace - reads like a crash
  when a player simply left. `FDGServer.LaunchStateMachineOnceReady` now catches
  `PlayerDisconnectedException` ahead of the generic handler and ends with a plain "<name> left the game.
  The game has ended." (name via `DescribePlayerLeft`, generic "A player" fallback if the slot is gone); no
  scary console dump. Tests in `DisconnectLifecycleTests`. This is presentation only - the *recovery* work
  (auto-save + rejoin) below is still open.
- 2026-07-08: Filed. QF8 makes the *client* leave cleanly on host loss; this item is the *recovery* half
  (don't lose the game to a 20s wifi blip). The resume machinery exists but its networked path is untested.

## Decisions
- (none yet)

## Outcome
(pending)
