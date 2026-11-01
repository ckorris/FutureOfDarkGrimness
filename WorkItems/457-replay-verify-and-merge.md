# 457 — Replay: verification, docs, merge feature/replay -> master

**Status**: in-progress - verified on one machine 2026-10-31; the two-machine run, the perf-bar call and the `master` merge wait on Chris
**Related**: umbrella #449; all of #450-#456; parallel #415 (OPR 3.6) for merge ordering

## Goal
`feature/replay` is merged into `master` in both repos (engine first, then the superproject bump), with
every sub-item's Outcome written, the docs updated, and a networked two-machine hand-verify done. Done when
`master` builds, the engine suite and `FdgRaylib.Tests` are green, the headless smoke writes a replay, and
the index lines for 449-457 are ticked and archived.

## Checklist
- [~] Two-machine networked game (host + remote client over the real `FDGHost`/`FDGClient`): both write a
      `.fdgreplay` at game end; both open in the viewer; the client's file lists the host's decisions;
      `--verify` passes on both; step counts match. (#451's two-process GUI check. #452 ran it with two processes
      on one machine, early exits only - see its Notes; this item still wants a full game on two machines.)
- [~] Save -> quit -> load -> finish on the host with a client reconnecting to the resumed game: one
      continuous replay on both sides (`Saved`, `Resumed` markers present, `--verify` passes). (#453 ran the host
      half in the GUI on one machine - save, Esc -> Load, finish - and pinned the client half in-process over the
      loopback transport; the two-machine run is still this item's.)
- [~] Disconnect mid-game: the host writes both the #187 recovery save (with embedded replay) and the
      replay file; the client writes its replay. Then resume the recovery save and finish: its replay shows
      `GameEnded` (Disconnect), `Resumed`, ..., `GameEnded` (Chris's #453 call), and `--verify` passes.
- [~] Size check against `docs/replay-plan.md` section 5 on three real GUI games (`--stats`); update the
      table with real beat numbers if #450's measurement was from the headless path only.
- [x] Headless: `printf "2\n2\n" | dotnet run ... --headless` exits 0 and prints the replay path;
      `--scenario` writes a replay; `--replay FILE --dump/--verify/--make-save` work on it.
- [!] Performance: a 2k game's recording adds no visible per-decision latency (compare `--slow 0`
      headless wall time with and without recording; < 3% is the bar) and the GUI frame time is
      unaffected during play.
- [x] Docs: `CLAUDE.md` Build & Run gets the `--replay` lines and the smoke's expected replay line;
      `docs/EngineNotes.md` gets a "Replay" section (recorder attach points, container shape pointer,
      the `Resumed` boundary rule) and the key-files tree gains `Replay/`; `README.md` user-facing
      paragraph ("Replays" folder, how to open one, send it with a bug report); `Scenarios/README.md`
      mentions `--make-save` from a replay as a repro path; `docs/ResolverGuide.md` notes that
      `StageTaskNotifyResolvedMessage` now carries payloads (D2).
- [ ] Every sub-item's detail file has an Outcome and its index line is ticked and moved to
      `WorkItems/Archive.md`; plan section 6 statuses are `done`.
- [~] Merge: `git fetch origin`; merge `origin/master` into `feature/replay` in the engine, run the suite,
      then in the superproject (bump pointer), run everything, push the branch; then fast-forward or
      merge `feature/replay` into `master` in the engine, push; bump + merge in the superproject, push.
      If `feature/opr-3.6` has already landed (#444), the `master` merge-in step is where its conflicts
      surface (plan section 8 says where to expect them: `LobbyScreen`, `RaylibRenderer`, request
      shapes). If it has not, tell the #415 owner that `master` now has replay so their next
      `master` -> `feature/opr-3.6` merge picks it up.
- [ ] Delete the remote `feature/replay` branches after the merge (both repos).

## Notes
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
      resumed save is a `--make-scenario` one. Filed as **#459**.
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
- (none yet)

## Outcome
(open)
