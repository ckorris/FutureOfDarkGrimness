# 456 — GUI replay viewer

**Status**: done 2026-11-01 - hand-verified by Chris; merged into `master` with #457 (forks F1, F2 answered 2026-10-30 - see Decisions)
**Related**: plan `docs/replay-plan.md` sections 4.3, 4.4, 7; depends on #454, #452; reuses the in-game canvas (`RaylibRenderer.TransitionToGame`), `PresentationPlayer`, `GameLog`

## Goal
From the main menu, open a replay (from the `Replays/` list or a file dialog, `.fdgreplay` or `.fdgsave`),
watch it on the real table view with the real animations, pause, step forward and back one decision at a
time, jump via a decision list, autoplay at a chosen speed, and see for each step who decided what. Done
when Chris has hand-verified it on a replay of a GUI game and on a client-recorded replay of a networked
game, and the acceptance list below is ticked.

## Scope (`FdgRaylib/Rendering/ReplayBrowserScreen.cs`, `ReplayScreen.cs`, wiring in `Program.cs`)
- Main menu: `Replays` button (between `Load Game` and `Options`; `MainMenuScreen.OnReplaysClicked`).
- `ReplayBrowserScreen` (`IAppScreen`): table of `ReplaySave.List()` newest first - date, armies,
  players (controller tags), rounds, steps, outcome, size - from `ReplaySummary` (header only; do not
  decompress every file on open: #450's header is the first JSONL line, read just that). `Open file...`
  (`TinyDialogs`, filter `*.fdgreplay;*.fdgsave`), `Delete`, `Back`. Parsing failures show the
  `ReplayFormatException` message inline, never a crash.
- `ReplayScreen`: a new `IAppScreen` (F2) that calls the same wiring `TransitionToGame` uses. It needs:
  - A `ReplayPlayer` (from #454); the renderer draws `player.TableState`; on `OnStoreReplaced` the
    screen re-runs the table-state subscription setup (`TransitionToGame`'s model subscription code; the
    #052 note about seeding `_placedModels` from already-positioned models applies - restored stores do
    not fire `OnPositionChanged`).
  - Player colours from the header slots (palette-indexed as `LobbyScreen.HandleLaunch` does).
  - `PresentationPlayer` fed the span's beats on each step; `GameLog` fed the span's log lines; the
    tactical overlay / hover tooltips in read-only mode if they come for free, otherwise off.
  - No resolvers, no outstanding-task display, no save button, no chat.
- Controls (ImGui bar, bottom): `<`  `Play/Pause`  `>` (the `|<` / `>|` buttons went in #457's UX pass; Home /
  End keys remain), speed `0.5x 1x 2x 4x Instant`, a
  step slider, and the label `Round 2 - Activation 5 - P1 (AI) - step 12 / 341`. Keys: Space
  play/pause, Left/Right step, Home/End, Esc back to the browser (see `ResolverKeybinds` for how
  advertised keys are centralised; do not collide with its in-game bindings - the replay screen has
  none of them active).
- Decision list (right panel, scrollable): one row per step `P1 (AI)  Warriors: Shoot`
  (`ReplayStep.Summary`; #457's UX pass dropped the step / round / activation numbers and always lists the
  folded requests, dimmed), current row highlighted and kept in view, click = seek. Markers (`Round 3`,
  `Saved`, `Resumed`, `Game over`) as separator rows. Every line is drawn in its player's table colour (markers
  white, except Game over in the sole winner's); so is the position line under the slider.
- Details (below the list, for the current step): request `DisplayName`, who answered (controller), the
  describer summary, and a `Raw` toggle printing the reply JSON trimmed to ~20 lines.
- Autoplay (F1, decided): after a step's span beats finish (`PresentationPlayer` idle) plus a dwell
  of 0.75 s / speed; `Instant` = no beats, 0.1 s dwell. Autoplay stops at the end and at a `Resumed`
  marker (shows a one-line banner "Game was saved and resumed here; the unfinished activation was
  replayed").
- **From #452:** `ReplaySave.List(directory = null)` returns full paths, newest first (by name: the UTC stamp leads);
  `ReplaySave.DirectoryPath` is the folder. Names are `replay-<stamp>-<armyA>-vs-<armyB>[-<n>p][_<k>].fdgreplay`
  with the army names cut to `[A-Za-z0-9-]` and 24 characters - show the header's names, never parse the file
  name. `--replay-out` only redirects where a run writes; the browser reads `DirectoryPath`.
- **From #453:** a recording can span sessions. `Header.ResumedUtc` lists the resumes (show them on the header line);
  each is a `Resumed` marker followed by a snapshot that replaces the state - `ReplayTimeline.SnapshotFor` and the
  player already never cross one. A `Saved` marker sits just before each (none when the save was a #187 recovery save:
  that recording keeps its `GameEnded` (Disconnect) right before the `Resumed`). Show the outcome from
  `ReplayTimeline.GameEndedMarker` (the last session's end), never the first `GameEnded`. A request open at the save
  has no reply and is asked again after the resume under a new task, so it never becomes a step. A client's recording
  of a resumed game begins with the host's events (verbatim, through the `Resumed`), so its early steps carry the
  host's `T`. A `.fdgsave` carries `GameSaveFile.ReplaySummary` ahead of its entries, should the browser ever list
  saves; `--replay` already opens one.
- Step-back cost: a seek rebuilds the store (plan 4.3). Show a brief "seeking..." if it exceeds 100 ms;
  no frame may block the UI for more than that - do the `Load` on a worker and swap on the main thread
  (same lock-and-handoff discipline the resolvers use; see `docs/ResolverGuide.md`).
- ASCII-only in every string (CLAUDE.md).

## Tests (`FdgRaylib.Tests/ReplayViewerTests.cs`)
- Browser row formatting from a `ReplaySummary`; sort order; a corrupt file yields an error row.
- Control state machine (pure class `ReplayTransport`): play/pause/step/seek transitions, autoplay
  advance gating on "beats idle + dwell", stop at end and at `Resumed`.
- Decision-list row text for each marker kind and for a folded step.

## Acceptance (Chris, hand-verify)
- [x] Open a replay of a local GUI game; models and terrain appear at step 0; stepping forward plays
      shooting beams / dice / wounds exactly as in play; log scrolls in sync.
- [x] Step back from the middle; the table reverts; step forward again replays the same beats.
- [x] Autoplay at 2x runs to the end without stalling; `Instant` finishes a 300-step game in under 30 s.
- [x] Open a `.fdgsave` with an embedded replay; the `Saved`/`Resumed` separators appear.
- [x] Open a client-recorded replay of a networked game; other players' decisions are listed.
- [x] Esc returns to the browser; the browser lists the file just watched; Delete removes it.

## Verification
`dotnet build`; FdgRaylib.Tests green; headless smoke unaffected; acceptance list above.

## Notes
- 2026-10-31 - Built; verified by tests and in the GUI; the acceptance list stays Chris's.
  - Engine: `ReplayWriter` stamps the summary of what it writes on the header line (`ReplayHeader.Summary`, format
    v3); `ReplayReader.ReadHeaderGzip` reads that line alone; `ReplayTally` (shared by recorder and writer) counts
    steps as `DefaultStepFilter` does. App: `FdgRaylib/Rendering/Replays/` - `ReplayLabels` + `ReplayFiles` (shared
    with the `--replay` CLI), `ReplayCatalog` (browser rows), `ReplayListRows` (decision list), `ReplayTransport`,
    `ReplaySession`, `ReplayScreen`, `ReplayBrowserScreen`; `RaylibRenderer.ShowReplay` / `ReplaceTableState`;
    `PresentationPlayer.Reset`, `GameLog.Clear`; main-menu `Replays` button; `Program.cs` wiring.
  - Tests: engine 3484 passed / 1 skipped (+1: the header carries the summary of what was written and reads alone;
    the step-count assertions now compare with the timeline). App 3097 (+14 `ReplayViewerTests`: browser rows from
    the header, newest first, an unreadable file a row saying why; the transport's dwell / stop at end / stop at a
    resume; the decision list's order, markers, folded rows; the session over a real recorded game - step forward
    queues beats in place, a seek lands on the worker and hands over the new table state with the log rebuilt,
    latest-of-several seeks wins, Instant autoplay reaches the end in the same state a rebuild of the end has, and
    stops at a resume; a `.fdgsave` opens). Smoke unaffected (exit 0, `Game result:`, `Replay saved to:`).
  - GUI, Xephyr :5 (1280x720, llvmpipe), kit `~/Projects/fdg-lab-scratch/replay-456/`: main menu -> Replays listed
    three v3 replays newest first (one "(1 resume)") and four older-format files as unreadable rows with the reason;
    Open showed the table at step 0 with terrain and objectives, the decision list and the log; Right stepped
    through deployment (models appear); End jumped to the game's end (round 4/4, "Game over: Tie", log rebuilt);
    23 rapid Lefts chained seeks; stepping forward on a shot played the hit and save dice as in a game; the slider
    jumped to the resume, where `-- Saved in round 2 --` / `-- Resumed ... --` separate the list and the notice says
    the game was saved and resumed; 2x autoplay played melee dice and activation banners with the list following;
    Esc returned to the browser; Delete asked, then removed a file; Open File (zenity) opened a `.fdgsave`, whose
    replay ends `-- Saved in round 2 --` with "the game did not finish here". No exception in the logs.
  - Fixed while looking: a doubled "(AI)" for scenario-named slots, clipped columns, the list too short at 720p
    (the panel takes 68% of the column in a replay), 1-based "step 79" against the list's `#078` (now `#NNN`
    everywhere, as `--at` takes it), the dice stack under the transport bar (now stands on it), the browser's
    step count disagreeing with the viewer's (now one count).

## Decisions
- F1 (Chris, 2026-10-30): beat-paced autoplay + 0.75 s dwell. F2: new `ReplayScreen` + main-menu `Replays` button.
- **The viewer is a renderer mode, not an `IAppScreen.Draw`.** The table is drawn by `RaylibRenderer` outside ImGui,
  so `ShowReplay` reuses `TransitionToGame` (no resolvers, task display, chat, save) and `ReplayScreen` draws only
  what replaces the game's controls; Esc goes through the Escape arbiter and leaves to the browser without raising
  `OnGameExited` (no game, no lobby, no replay to write).
- **Re-seeding on a new store:** `TransitionToGame` / `ExitGame` were split around `AttachTableState` /
  `DetachTableState` (overlays, model / terrain / objective subscriptions, the placed-model seed); a rebuilt store is
  swapped in by `ReplaceTableState` on the main thread. The tooltip, army list, measurement and tactical overlays
  come along for free and work read-only; the tactical overlay's toggle resets on a seek.
- **Threads:** a step forward runs on the main thread so its beats are queued in the frame its deltas land (the
  presentation player holds moving / dying models from the moment a beat is queued - a worker would let a model jump
  first). A seek (jump, step back, slider, row click) rebuilds a NEW store on a worker; the renderer keeps drawing
  the old, untouched one until `Update` hands the new one over. Moves asked for meanwhile are remembered, latest wins.
  A step forward across a resume rebuilds on the main thread (one step, rare).
- **The browser reads one line per file:** the plan's browser columns need steps / rounds / outcome, which no header
  had, so the writer now stamps `ReplaySummary` on the header (format 3; plan rule 5). And `Summary.Steps` now counts
  steps exactly as `DefaultStepFilter` does, so one number appears everywhere; #452's "no decision, no file" and the
  #453 save summary follow it.
- **Instant dwell is 0.05 s, not the plan's 0.1 s:** 300 steps x 0.1 s is already 30 s before frame rounding, and the
  acceptance asks for under 30 s.
- **After a seek the log is rebuilt** to every recorded line up to the new position (in sync with the table); log
  lines carry no colour in the recording, so the replay's log is one colour.
- **Colours** are the palette in slot order - a replay does not record lobby colour picks.
- Steps are numbered `#NNN` from 0 in the list, the label, the slider and the details - the number `--at` takes.

## Outcome
Built 2026-10-31 on `feature/replay` (engine cc57a476): main menu -> Replays lists every recorded game from its header
line; any replay or replay-carrying save opens on the real table view with its animations, pause / play at four speeds
or Instant, step forward and back, seeks by list row, slider and Home / End, the decision list with markers and folded
requests, and the current step's details and raw reply. Verified by 15 new tests and run through end to end in the
GUI (Notes). Chris ticked the acceptance list 2026-11-01 (client-recorded networked replay included) after #457's UX
pass (see its Notes: winners in the browser, player-coloured rows, Full Board mode). Known and left: a step's deltas apply at once and its
beats play after, so the round HUD can read the next round while the previous round's last shot animates; replays
recorded before today (formats 1 and 2) list as unreadable.
