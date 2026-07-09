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
- 2026-07-08: **Slice 2 (disconnect while not this player's turn) done.** Slice 1 only ended the game when
  the drop coincided with a pending request for that player; a client closed while it wasn't its turn left
  nothing to fail, so the game silently continued and then hung on the dead connection at the player's next
  turn ("closed the client and nothing happened"). `RequestMessageSender` now records dropped players and
  faults any request targeting one on arrival (not just in-flight ones), so the disconnect reliably ends the
  game at the next decision regardless of timing. Test in `DisconnectLifecycleTests`. Known limitation: ends
  at the *next* request, so a drop mid-opponent-turn ends the game one activation later, not instantly -
  acceptable; true immediate end would need out-of-band state-machine faulting.
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
