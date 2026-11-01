# 457 — Replay: verification, docs, merge feature/replay -> master

**Status**: done 2026-11-01 - merged into `master` in both repos
**Related**: umbrella #449; all of #450-#456; parallel #415 (OPR 3.6) for merge ordering

## Goal
`feature/replay` is merged into `master` in both repos (engine first, then the superproject bump), with
every sub-item's Outcome written, the docs updated, and a networked two-machine hand-verify done. Done when
`master` builds, the engine suite and `FdgRaylib.Tests` are green, the headless smoke writes a replay, and
the index lines for 449-457 are ticked and archived.

## Checklist
- [x] Two-machine networked game (host + remote client over the real `FDGHost`/`FDGClient`): both write a
      `.fdgreplay` at game end; both open in the viewer; the client's file lists the host's decisions;
      `--verify` passes on both; step counts match. (#451's two-process GUI check. #452 ran it with two processes
      on one machine, early exits only - see its Notes; this item still wants a full game on two machines.)
- [x] Save -> quit -> load -> finish on the host with a client reconnecting to the resumed game: one
      continuous replay on both sides (`Saved`, `Resumed` markers present, `--verify` passes). (#453 ran the host
      half in the GUI on one machine - save, Esc -> Load, finish - and pinned the client half in-process over the
      loopback transport; the two-machine run is still this item's.)
- [x] Disconnect mid-game: the host writes both the #187 recovery save (with embedded replay) and the
      replay file; the client writes its replay. Then resume the recovery save and finish: its replay shows
      `GameEnded` (Disconnect), `Resumed`, ..., `GameEnded` (Chris's #453 call), and `--verify` passes.
- [x] Size check against `docs/replay-plan.md` section 5 on three real GUI games (`--stats`); update the
      table with real beat numbers if #450's measurement was from the headless path only.
- [x] Headless: `printf "2\n2\n" | dotnet run ... --headless` exits 0 and prints the replay path;
      `--scenario` writes a replay; `--replay FILE --dump/--verify/--make-save` work on it.
- [x] Performance: a 2k game's recording adds no visible per-decision latency (compare `--slow 0`
      headless wall time with and without recording; < 3% is the bar) and the GUI frame time is
      unaffected during play.
- [x] Docs: `CLAUDE.md` Build & Run gets the `--replay` lines and the smoke's expected replay line;
      `docs/EngineNotes.md` gets a "Replay" section (recorder attach points, container shape pointer,
      the `Resumed` boundary rule) and the key-files tree gains `Replay/`; `README.md` user-facing
      paragraph ("Replays" folder, how to open one, send it with a bug report); `Scenarios/README.md`
      mentions `--make-save` from a replay as a repro path; `docs/ResolverGuide.md` notes that
      `StageTaskNotifyResolvedMessage` now carries payloads (D2).
- [x] Every sub-item's detail file has an Outcome and its index line is ticked and moved to
      `WorkItems/Archive.md`; plan section 6 statuses are `done`.
- [x] Merge: `git fetch origin`; merge `origin/master` into `feature/replay` in the engine, run the suite,
      then in the superproject (bump pointer), run everything, push the branch; then fast-forward or
      merge `feature/replay` into `master` in the engine, push; bump + merge in the superproject, push.
      If `feature/opr-3.6` has already landed (#444), the `master` merge-in step is where its conflicts
      surface (plan section 8 says where to expect them: `LobbyScreen`, `RaylibRenderer`, request
      shapes). If it has not, tell the #415 owner that `master` now has replay so their next
      `master` -> `feature/opr-3.6` merge picks it up.
- [x] Delete the remote `feature/replay` branches after the merge (both repos).

## Notes
- **2026-11-01 - Chris's hand-verify and the merge.** Chris ran the viewer and a networked game on his own machines
  and reported everything good (the one correction: round markers coloured for their initiative read as a leak -
  made white). Merge: `origin/master` had moved - `feature/opr-3.6` (415-447, 459) and #460 landed - so master was
  merged into the branch first: engine conflicts in `LobbyViewModel_Host`'s resume constructor (now
  `saveRulesVersion, priorReplay`) and `GameSaveSerializer` (`Load(json, out rulesVersion)` and
  `LoadWithReplay(json, out rulesVersion)` share `Parse` / `Build`); app conflicts in `ScenarioLauncher.LoadStore` /
  `LoadStoreWithReplay` (both report the #444 legacy-save line) and `Program.cs`'s Load Game and `--scenario` paths
  (both features). The branch's #459 collided with the 3.6 branch's and yields: now **#461**. Suites after the merge:
  engine 4620/1 skipped, app 4197; smoke exit 0 with its replay line, `--verify` PASS. Then `feature/replay` ->
  `master` in the engine, pointer bump + merge in the superproject, pushed, remote branches deleted.
- **2026-11-01 - marker colours.** Chris read a round's marker in its initiative's colour as the previous line's colour
  leaking (the first activator of a round is often the player who just decided). Checked: the data and the draw were
  right (`ui/m2.png`), the rule just reads wrong; round / saved / resumed markers are now white, only Game over keeps
  the sole winner's colour, and the position line at the end follows (`ReplaySession.PositionPlayer`). Every row,
  details line and bar line pushes its own text colour - no line inherits one.
- **2026-11-01 - viewer UX, round two (Chris took every audit proposal, plus a full-board mode).** Full Board (`F`,
  or the bar's button; `ReplayScreen.Theatre`): the renderer drops the right column (`RightColumnWidth` = 0, no
  panel, no console) so the table fills the window; the transport bar shows while the mouse moves and for 2.5 s
  after, then fades over 0.4 s (`ControlsAlpha`; resting on the bar keeps it; the cursor hides with it); Esc leaves
  theatre first, the viewer second (`ReplayScreen.Escape`, the renderer's Escape path). Audit items: the game-over
  text is the bar's alone (the details pane just says "End of the recording"); the details reserve 4.5 lines (the
  request type moved under Raw reply), so the list gets more rows; the list scrolls sideways to its longest line
  (`SetNextWindowContentSize` + `HorizontalScrollbar`); the header is four short lines (title, players, recorded by,
  started); the browser lost its Steps column (the count rides the row tooltip) and Armies got the width. Checked in
  Xephyr 1280x720: theatre on / faded / back on mouse move / Esc out (`~/Projects/fdg-lab-scratch/457/ui/t1-t4.png`).
  Suites: engine 3487/1 skipped, app 3180; smoke exit 0 with its replay line.
- **2026-11-01 - viewer UX pass (Chris's list, after a look at the viewer on his desktop).** Browser: the Outcome
  column names the winners ("Player 1 wins", "A and B win") - the GameEnded marker now carries `Winner` (the sole
  winning player, for colours) and `WinnerName` (the prose), relayed to clients, and `ReplaySummary.Winner` rides
  the header; additive, no format bump - a v3 file recorded before this shows a plain "Win". Viewer: the folded
  requests are always listed (dimmed; the checkbox is gone); rows read "who, then what" with no step / round /
  activation numbers (the number stays on the slider and the position line, where `--at STEP` wants it); each
  line is drawn in its player's table colour, markers in the sole winner's / the round's initiative's (the first
  ActivationStart of the round), else white; the position line under the slider likewise (`ReplaySession.PositionPlayer`);
  `|<` / `>|` removed (Home / End keys stay); Play is the widest control, the steps flank it, every size in EM so
  the bar scales with the display. Also fixed from the same session: dragging the window very small crashed
  (`RightColumnWidth`'s `Math.Clamp` with min > max below 680 px wide) - the cap now wins, and the window has an
  800x600 minimum. Checked in Xephyr at 1280x720 and forced to 300x200 (survives). Suites: engine 3487/1 skipped,
  app 3180 (list rows carry their player, round rows the initiative; Outcome strings); smoke exit 0 with its replay line.
- **2026-11-01 - performance, Chris's call.** Recording stays on by default; headless runs take **`--no-replay`**
  (`CliApp(record: false)`: no recorder on the new-game or scenario path, the Mode line says so, no file; outside
  `--headless` it exits 2 - the GUI always records). Of the cuts I had suggested, profiling showed non-indented
  snapshots save only ~10-20 ms a game (and need a format bump) and `Fastest` gzip ~50 ms for a ~60% bigger file -
  Chris took neither, and took the one that only pays: `ExportGzip` stamps the header with the recorder's running
  tally instead of recounting every request (`ReplayWriter.WriteGzip(..., summary)`), ~15 ms off the end write.
  Pinned: `HeadlessRecordingToggleTests` (recorded by default / `record: false` leaves none), and the exported
  header summary equals a recount on a real game and on a continued recording (`ReplayRecorderGameTests`,
  `ReplayResumeTests`). 2k seed 42, Release: play 1.96 s recorded vs 1.69 s with `--no-replay`; write 0.21 s.
  Suites: engine 3487/1 skipped, app 3180; smoke exit 0 with its replay line, `--verify` PASS.
- **2026-10-31 - verification on one machine (`[~]` = done here, the two-machine half still Chris's; `[!]` = needs a call).**
  - **Master merged into the branch** (both repos, pushed): engine `bad4f5c4` brings #458 (re-ask a rejected reply);
    app `0c60bc3` brings #448 (war-table menu) and #458. One conflict, `MainMenuScreen`: #448 rebuilt the menu into
    tiers, so Replays became a fourth Secondary button after Load Game (`MainMenuLayout` 3 -> 4 secondaries; the panel
    grows one row, checked by `--menu-preview` at 1920x1080 and 1024x768). Suites after the merge: engine 3487/1
    skipped, app 3178; smoke exit 0 with its replay line. `feature/opr-3.6` has NOT landed on `master`.
  - **Networked, two GUI processes over real TCP** (host in Xephyr `:5`, client in `:6`, `--replay-out` per side;
    scenario `~/Projects/fdg-lab-scratch/457/net/net457.json`: round 3, a Marksmen and a Rending Squad each):
    - *Play, save, leave, reload, rejoin, finish:* session 1 (host saves at round 4 start, returns to menu) - both sides
      wrote a replay, both `--verify` PASS, 4 steps each; the client lists the host's decisions as P1 (Remote); the
      only difference is the host's last request, open when it left, which a client never sees (D2). Session 2 (host
      loads that save, client rejoins, game to the end): both files are ONE recording - `GameStart R3`, `RoundStart R4`,
      `Saved R4`, `Resumed R4`, `GameEnded Tie` - both PASS, step lists identical (14 + 4 folded).
    - *Disconnect:* the client's window closed while its AssignWounds request was open. Host: "Guest left the game",
      the #187 recovery save (with the replay, PASS) and its replay (`GameEnded Disconnect`, PASS); client: its replay
      written on exit (PASS, request open). Resuming the recovery save with a fresh client and finishing: both files
      `GameStart R3`, `GameEnded Disconnect`, `Resumed R3`, `RoundStart R4`, `GameEnded Tie`, PASS (the verifier notes
      the ended-then-resumed session, as designed), step lists identical (17 + 6 folded).
    - *#456's open check:* the client-recorded session-2 file opened in the client's viewer (Open File): "Recorded by
      the client (P2)", both players' decisions listed, Saved / Resumed separators, autoplay stopped at the resume
      with its notice and played to "Game over: Tie". No exceptions in either log (the network layer's usual
      "operation was canceled" teardown lines aside - `FDGHost`/`FDGClient` are untouched by the branch).
    - Found on the way, pre-existing (same on an `origin/master` build): the client draws no model bases when the
      resumed save is a `--make-scenario` one. Filed as **#461** (filed as 459, renumbered at the master merge - see Reconciliations).
  - **Headless:** smoke OK; `--scenario` writes a replay (PASS); `--dump`, `--at 3 --make-save`, then `--scenario` on
    the cut: one recording `GameStart`, `Saved`, `Resumed`, `GameEnded`, PASS; `--replay` on the cut save PASS.
  - **Size:** three seeded 2k games, 362-406 KB (plan section 5 has the table). Not from three full GUI games - those
    want a person playing; the beats are engine-side so the GUI path records the same ones.
  - **Performance `[!]`:** with recording a 2k headless game is +0.25 s of play (~0.75 ms per decision; round
    snapshots are most of it) plus 0.22 s to write the file at the end - ~15% of headless play time, so the "< 3%"
    bar as written is not met, though nothing a player could see; render thread untouched apart from +20 ms of GC
    pause per game. Full breakdown in plan section 5. Chris's call: accept (recommended) or take the cheap cuts.
  - **Docs:** `docs/EngineNotes.md` gains a "Replay" section (attach points, container, the `Resumed` boundary rule,
    playback) and `Replay/`, `Rendering/Replays/`, `ReplayTool.cs`, `ReplaySave.cs` in the key-files tree;
    `README.md` gains "Watching Replays", a bug-report line, a feature bullet, and drops replays from Unsupported.
    `CLAUDE.md`, `Scenarios/README.md` and `docs/ResolverGuide.md` already had their lines (#451/#455/#456).
  - Lab files: `~/Projects/fdg-lab-scratch/457/` (perf scripts and timings, the trace, GC hook, network screenshots
    and the copied replays `net/s1..s4-{host,client}.fdgreplay`).
- **From #456 (2026-10-31):** the viewer is built and was run end to end in Xephyr (see its Notes), but its acceptance
  list is Chris's to tick - fold it into this item's hand-verify, including the one #456 could not run: a
  client-recorded replay of a networked game opened in the viewer (other players' decisions listed). Known and left:
  a step's deltas apply at once and its beats play afterwards, so the round HUD can read the next round while the last
  shot of the previous one animates.

## Decisions
- **Recording cost (Chris, 2026-11-01):** accept it in played games, `--no-replay` for headless batch runs, and only the
  tally reuse among the cuts (the others were measured and judged not worth it - see Notes).

## Outcome
Done 2026-11-01. Verified on one machine (two processes over real TCP: save / rejoin, disconnect + recovery resume,
client replay in the viewer) and by Chris on two machines; headless tools, size and recording cost measured (plan
section 5; `--no-replay` for batch runs); docs written (CLAUDE.md, EngineNotes Replay section, README, plan). The
viewer had a UX pass on Chris's review (winners named in the browser, rows in player colours without numbers, folded
always listed, bigger EM-sized transport, Full Board mode with fading controls, white markers) and the tiny-window
crash was fixed on the way. `feature/replay` merged into `master` in both repos after taking the OPR 3.6 merge on
board; branches deleted. Follow-up filed: #461 (client of a resumed scenario save draws no model bases, pre-existing).
