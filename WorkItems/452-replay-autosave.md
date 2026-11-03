# 452 — App: auto-write the replay on every game exit

**Status**: done 2026-10-31
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
- **From #451:** a client's recording is `LobbyViewModel_Client.Replay` (so `activeLobby?.Replay` covers host and
  client alike); it closes itself on the host's `GameEnded` marker, which arrives before the client's
  `OnGameEnded`. `LobbyScreen`'s `viewModel.OnGameEnded` handler prints a temporary `Replay recorded: N decisions,
  R rounds, outcome X (not written to disk yet - #452).` line on both sides - replace it with the write.
- Headless smoke must keep exiting 0: `printf "2\n2\n" | dotnet run ... --headless` now also prints
  `Replay saved to: Replays/replay-....fdgreplay`. Add that line to the expected-output check in
  CLAUDE.md's smoke instructions once it exists.

## Tests (`FdgRaylib.Tests/ReplaySaveTests.cs`)
- File naming (sanitising, truncation, FFA suffix), `TryWrite(null)`
  returns null, a failing directory returns null and does not throw.
- Idempotence: two writes for one `GameId` produce one file whose content is the later export.

## Verification
`dotnet build`; FdgRaylib.Tests green; headless smoke writes a file, and `--replay Replays/<file> --verify`
passes on it (#455's tool - checked so far only on a temporary export patch, see #455 Notes); GUI: play a short local game and
exit via each of (game over card, Esc -> main menu, Esc -> quit to desktop, window X) checking `Replays/`
each time; a client-side file from one networked game (two processes on one machine is enough here;
#457 does two machines).

## Notes
- 2026-10-31 - Built in one slice (engine: `IReplaySource.Header`; app: `ReplaySave`, the `Program.cs` hooks, the
  card note, `CliApp.Replay`, `--replay-out`). Three corrections to the scope above, found reading the code:
  - Escape-menu **Quit to Desktop does not run `ExitGame()`** - it calls `RequestClose`, which ends the render loop
    exactly like the window's close button. So `OnGameExited` covers return-to-menu and load only; Quit to Desktop
    and window X are both covered by one `WriteReplayIfAny()` at the top of `Program.cs`'s `finally` after
    `renderer.Run()` (also the answer to the Decisions placeholder below).
  - Idempotence keyed on `GameId` would be wrong: a resumed save keeps its game ID, so Esc -> Load the same save ->
    play would overwrite the first session's file. Keyed on the recording instance instead
    (`ConditionalWeakTable<IReplaySource, ...>` in `ReplaySave`), and a repeat call with an unchanged `Summary` is a
    no-op, so the exit after a game-over card neither rewrites nor prints a second "Replay saved to" line.
  - `TryWrite` takes no army-name parameters: the names come from the recording's own header, so `IReplaySource`
    gained `ReplayHeader? Header` (both recorders already had it; the client's was internal).
  - Crash: only the fatal paths write - `AppDomain.UnhandledException` and the `renderer.Run()` catch (before its
    "Press Enter" wait). `WriteCrash` itself does not: it also serves the non-fatal
    `TaskScheduler.UnobservedTaskException` (the game carries on) and "Load game failed".
- Card: the note now stacks paragraphs (`RaylibRenderer.AddGameOverNote`, under any #187 recovery note) and the card's
  height follows the wrapped note text instead of the fixed 200/290.
- Verification (2026-10-31): engine 3463 passed / 1 skipped; app 3082 passed (16 new in `ReplaySaveTests`); build
  clean. Headless smoke exit 0, `Game result:` then `Replay saved to: .../bin/Debug/net8.0/Replays/replay-...-Player-1-s-Test-Army-vs-Player-2-s-Test-Army.fdgreplay`,
  and `--replay <it> --verify` PASS (4 snapshots, 418 deltas, 112 beats) - the check #455 left for here. Headless
  `--scenario --all-ai --replay-out DIR` writes into DIR and verifies.
- GUI, driven by xdotool inside a nested Xephyr display (no window manager; software GL) so nothing took over the
  desktop. Scenario files in `~/Projects/fdg-lab-scratch/replay-452/` (`end-ai.json`: round 4 with the human's
  units already activated, so the AI ends the game; `mid-ai.json`: the AI moves, then the game waits on the human),
  plus `click.sh` (a held click - xdotool's instant click falls between llvmpipe frames) and `wm_close.py` (sends
  WM_DELETE_WINDOW, what a close button does). Each run wrote exactly one file, `--verify` PASS on every one:
  - `--scenario` game over: card shows "Replay saved to"; then Return to Main Menu and main-menu Quit wrote nothing more.
  - `--scenario` mid-game: Esc -> Return to Main Menu -> Yes; Esc -> Quit to Desktop -> Yes; window close. One file each.
  - Lobby game vs a Strategist bot (the ordinary Host path): two decisions, then Esc -> Return to Main Menu.
  - Networked, two processes on 127.0.0.1: host and client each wrote one file with the same `GameId`/seed and the
    same 3 steps (slots tagged Human/Remote from each side). Host left via Esc -> menu; the client's card read
    "Connection to the host was lost." with its replay path.
  - Networked, the client quits: the host's game ends with `Disconnect` once its next request goes to the departed
    player (existing #187 behaviour - it waits until it needs them); its card stacks the recovery-save and replay
    notes and fits both. The test recovery save was moved out of `bin/.../Saves/` (that folder did not exist before).
- Not exercised: a real crash (no safe trigger without a code change; the two fatal hooks are plain calls to the
  same writer), and a full networked game to its natural end on two machines (#457 item 1). Ctrl+C on a headless run
  writes nothing (no `CancelKeyPress` hook; not in this ticket's list).

## Decisions
- F3 (Chris, 2026-10-30): no cap on `Replays/`; a game with zero decisions writes nothing.
- Window close and Quit to Desktop: one `WriteReplayIfAny()` at the top of the `finally` after `renderer.Run()` -
  neither path runs `ExitGame()` (see Notes).
- One file per recording instance, not per `GameId`; an unchanged recording is not rewritten (see Notes).
- File names take the army names from `IReplaySource.Header` (engine change) rather than parameters; the stamp is the
  first write's UTC time; same-second collisions get `_2`, `_3`, as `RecoverySave` does.
- Writes go through `PATH.tmp` + `File.Move(overwrite)`, so a failed rewrite keeps the earlier complete file.
- `TryWrite` prints the `Replay saved to:` line itself, so every mode reports it the same way.
- The game-over card says "Could not save the replay (see the console for why)." when a game with decisions failed
  to write, the same pattern as #187's recovery note.

## Outcome
Done 2026-10-31 on `feature/replay`. Every game with at least one decision leaves one `.fdgreplay` in `Replays/`
beside the executable (or `--replay-out DIR`), on the host and on each client, whichever way it ends: game over,
a disconnect on either side, Esc -> main menu or load, Quit to Desktop, the window's close button, a headless or
headless `--scenario` run finishing, and (best effort) a fatal crash. The game-over card names the file. Each path
was exercised once in a real GUI (nested X display) or by script, producing exactly one file that `--verify`
passes. Not done here, named: a provoked crash, and the two-machine full game (#457 item 1).
