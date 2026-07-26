# 187 — Disconnect recovery (auto-save + live-test resume-rejoin)

**Status**: implemented, awaiting hand-verify (two-machine run)
**Related**: #052 (save/load), #076 (PlayerDisconnectedException), #054 (client-side save), #065 (network tests), QF8, NetworkingHandoff-2026-07-08.md

## Goal
An internet game that drops a player currently ends with "Game error: ..." (the engine faults the awaiting
stage with `PlayerDisconnectedException`, `RequestMessageSender.cs`). Over the internet a transient drop is
common, so done = when a game ends via `PlayerDisconnectedException` the host auto-saves a recovery file,
AND the networked resume-rejoin flow (#052's `OnResumeClientGreeting`, currently marked "NOT yet
live-tested") is exercised end-to-end: host loads the recovery save, the dropped player reconnects, adopts
their saved slot, and play continues.

## Notes
- 2026-07-26: **Slices 3 + 4 (auto-save + rejoin coverage) done.** Design forks signed off first (see
  Decisions). Two commits: engine `cff2f21`, app `f11e63a`.
  - *Engine*: a dropped connection now ends the game with a new `EGameOutcome.Disconnect` instead of
    `Fault`, and `ILobbyViewModel` raises the structured `GameResult` (`OnGameCompleted`, forwarded from
    `FDGServer`, host-only — a client has no FDGServer and the record never crosses the wire). It fires
    immediately BEFORE `OnGameEnded`, which is what lets the front end snapshot the game before anything
    tears it down.
  - *App*: `FdgRaylib/SaveLoad/RecoverySave.cs` writes `<exe>/Saves/recovery-<utcstamp>.fdgsave`, keeps the
    newest 5, and never throws (a failed write must not turn a handled disconnect into a crash). Wired in
    `Program.cs` off `LobbyScreen.OnGameCompleted`; the game-over card gains a second line naming the file.
  - *Why the store is safe to save there*: the catch in `FDGServer.LaunchStateMachineOnceReady` runs after
    the state machine has finished unwinding, so it is the most quiescent the store ever is mid-game —
    strictly safer than the manual Save Game (which snapshots a running game, see #054).
  - *Tests*: `DisconnectRecoveryTests` (2) runs a real game with a networked player, drops the connection,
    and pins both the `Disconnect` outcome and that the store at that moment round-trips into a resumable
    save. `ResumeRejoinNetworkTests` (3) covers the rejoin over the REAL transport — `FDGHost` on
    127.0.0.1 with real `FDGClient`s — asserting a returning client adopts its SAVED PlayerID, that two
    returning clients take distinct saved slots, and that neither takes the host's own slot. First tests
    over actual sockets in the suite (#065). 2165/2165 green.
  - *Mutation-checked*: routing rejoin greetings through the new-game path (fresh PlayerID) fails 2 of the
    3 rejoin tests, so they are not passing vacuously.
  - *App-side verification*: FdgRaylib has no test project (#068), so the file policy was exercised through
    a scratch harness — 21 checks over naming, the retention cap, same-second collisions, and the
    null-save (client) case. Retention ordering bug found and fixed there: with a `-2` collision suffix the
    NEWER of a same-second pair sorted as older (`-` < `.` ordinally) and could be pruned first; the
    suffix is now `_2`.
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
- 2026-07-26 (owner sign-off, four forks):
  1. **Engine signals, app writes.** The engine only reports *that* a disconnect ended the game
     (`EGameOutcome.Disconnect` + the structured result on the lobby); file-location policy stays app-side
     with the rest of the save/load UI. Rejected: engine-side file IO (puts user-file policy in the engine
     and would have FdgLab/headless runs inheriting it) and app-side string-matching of the end message
     (any wording change silently disables recovery).
  2. **`<exe>/Saves/`, newest 5 kept**, timestamped names, no dialog — the whole point is that the game
     survives even if the host closes the window straight after the drop.
  3. **Disconnect only, not every fault.** A faulted store may be mid-corruption, so auto-saving it can
     produce a save that crashes on load and quietly normalises "just reload" over fixing crash bugs.
  4. **Loopback-TCP rejoin test + a hand-verify checklist**, rather than checklist alone.
- The recovery save resumes at the start of the current TURN, not the exact moment of the drop: game
  progress is written at `DeterminePlayerTurnStage`, so the in-flight activation is lost. Same granularity
  as any manual mid-game save; not worth per-activation progress writes for this.

## Deferred (explicitly, not dropped)
- **One-click "Resume this game" on the game-over card.** The card names the recovery file and the host
  loads it through the ordinary Load Game flow. A direct button would save the host a file dialog; it needs
  a path-taking variant of `LoadGameFlow` and a second button in the fixed-height card.
- **Client-side recovery saves.** A client's `SaveGameToJson()` returns null, so only the host writes a
  recovery file. That is #054.
- **Host-chosen slot assignment on rejoin.** `OnResumeClientGreeting` still auto-fills the first open saved
  slot in slot order, so with 3+ players who reconnect out of order the wrong player can land in the wrong
  slot. Now covered by a test for *distinctness*, not for *identity*. A host-side "assign this connection
  to that slot" UI is the fix; filed here rather than as its own item until the two-machine run says
  whether it bites in practice.
- **Auto-save on a mid-game host crash** (as opposed to a disconnect) — see Decision 3.

## Hand-verify checklist (two machines, or two processes on one box)
The auto-save half is verifiable solo; the rejoin half needs a real second peer.

1. **Auto-save fires.** Host a game (Host + a networked client), launch, play into round 1, then kill the
   client process. Host shows "<name> left the game. The game has ended." plus a second line naming
   `Saves/recovery-<utcstamp>.fdgsave`. Confirm the file exists and is non-empty.
2. **Not a crash.** No stack trace in the console, and no `crash.log` entry.
3. **Recovery save loads.** Main menu -> Load Game -> pick that file -> the resume lobby seeds both saved
   slots (host on slot 0, the dropped player's slot standing in as AI).
4. **Rejoin as yourself.** With the resume lobby open, reconnect the second machine's client. Its slot flips
   from AI to Network under the returning player's name, and it keeps its ARMY from the save (not a new
   one). This is the flow the loopback tests cover; here it is over a real network.
5. **Play continues.** Resume, and confirm the rejoined player is asked for their own decisions and the
   board/round match where the game dropped (start of the turn it dropped in, per Decisions).
6. **Retention.** Repeat the drop a few times; `Saves/` holds at most 5 `recovery-*.fdgsave` files, newest
   kept.
7. **Client side.** On the client, the escape menu still shows Save/Load disabled ("Host controls saving
   and loading") - a client writes no recovery file.

## Outcome
(pending hand-verify - the two-machine run above)
