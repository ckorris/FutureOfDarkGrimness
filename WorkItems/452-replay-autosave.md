# 452 — App: auto-write the replay on every game exit

**Status**: todo
**Related**: plan `docs/replay-plan.md` section 4.5 and fork F3 (answered: no cap); depends on #450, #451; model: `FdgRaylib/SaveLoad/RecoverySave.cs` (#187)

## Goal
Every game that reached at least one decision leaves a `.fdgreplay` in `Replays/` beside the executable,
on the host and on every remote client, whether the game ended normally, by disconnect, by the escape
menu (return to menu / load / quit to desktop), by closing the window, by `--headless` or `--scenario`
finishing, or (best effort) by a crash. The game-over card says where the file went. Done when each exit
path listed below has been exercised once (GUI by hand, headless by script) and produced exactly one file
per game.

## Scope
- `FdgRaylib/SaveLoad/ReplaySave.cs` mirroring `RecoverySave`: `DirectoryName = "Replays"`,
  NO pruning and no `KeepNewest` (F3: the files are small, keep them all), `TryWrite(IReplaySource?,
  DateTime utc, string armyA, string armyB) -> string?`, `List()` newest-first (name-sorted: UTC timestamp first).
  File name `replay-<yyyyMMdd-HHmmss>-<armyA>-vs-<armyB>.fdgreplay` with army names sanitised to
  `[A-Za-z0-9-]` and truncated to 24 chars each; FFA/2v2: `-<n>p`. Every failure swallowed and reported
  via the return value and a `[replay]` console line, exactly like #187.
- Idempotence: the recorder gets `string? WrittenPath` (or the app keeps a per-game flag keyed on
  `GameId`) so a game that ends normally and is then exited through the escape menu writes once. A
  second write for the same `GameId` overwrites the same file (a later write has more events, e.g. the
  `GameEnded` marker).
- Exit hooks (all call one `WriteReplayIfAny()` in `Program.cs` that reads `activeLobby?.Replay`):
  - Host and client normal end: `renderer.LobbyScreen.OnGameCompleted` (host) and `OnGameEnded` (client
    only has this one). Write BEFORE `renderer.ShowGameOver` so the card can show the path: extend
    `SetGameOverNote` or add a second note line ("Replay saved to: ...").
  - Disconnect end: same hook as #187's recovery save; write both files.
  - Escape menu: `RaylibRenderer.OnGameExited` fires at the end of `ExitGame()` for return-to-menu, load
    and quit-to-desktop - write there (the lobby is still alive; `TeardownLobby` runs after).
  - Window close while in game: `RaylibRenderer.Run()` leaves the loop on `WindowShouldClose()`; make
    sure `ExitGame()` (and therefore `OnGameExited`) runs on that path, or call the writer explicitly
    after the loop.
  - Headless: `CliApp.RunAsync` after the game-ended TCS completes (both the fresh and the resume path);
    `--replay-out PATH` optional override of the directory (useful for scripts and for #455's tests).
  - `--scenario` (GUI and headless): same as the lobby paths; the server is constructed in
    `Program.cs`/`CliApp` so the recorder is in reach.
  - Crash: `Program.WriteCrash` tries `WriteReplayIfAny()` first, in its own try/catch; the GUI's
    `renderer.Run()` catch and the top-level handler both go through `WriteCrash`.
- Headless smoke must keep exiting 0: `printf "2\n2\n" | dotnet run ... --headless` now also prints
  `Replay saved to: Replays/replay-....fdgreplay`. Add that line to the expected-output check in
  CLAUDE.md's smoke instructions once it exists.

## Tests (`FdgRaylib.Tests/ReplaySaveTests.cs`)
- File naming (sanitising, truncation, FFA suffix), `TryWrite(null)`
  returns null, a failing directory returns null and does not throw.
- Idempotence: two writes for one `GameId` produce one file whose content is the later export.

## Verification
`dotnet build`; FdgRaylib.Tests green; headless smoke writes a file; GUI: play a short local game and
exit via each of (game over card, Esc -> main menu, Esc -> quit to desktop, window X) checking `Replays/`
each time; a client-side file from one networked game (two processes on one machine is enough here;
#457 does two machines).

## Notes
- (none yet)

## Decisions
- F3 (Chris, 2026-10-30): no cap on `Replays/`; a game with zero decisions writes nothing.
- (record where the window-close path actually runs the writer)

## Outcome
(open)
