# 457 — Replay: verification, docs, merge feature/replay -> master

**Status**: todo
**Related**: umbrella #449; all of #450-#456; parallel #415 (OPR 3.6) for merge ordering

## Goal
`feature/replay` is merged into `master` in both repos (engine first, then the superproject bump), with
every sub-item's Outcome written, the docs updated, and a networked two-machine hand-verify done. Done when
`master` builds, the engine suite and `FdgRaylib.Tests` are green, the headless smoke writes a replay, and
the index lines for 449-457 are ticked and archived.

## Checklist
- [ ] Two-machine networked game (host + remote client over the real `FDGHost`/`FDGClient`): both write a
      `.fdgreplay` at game end; both open in the viewer; the client's file lists the host's decisions;
      `--verify` passes on both; step counts match. (#451's two-process GUI check - until #452 lands, compare
      the `Replay recorded: N decisions ...` console lines both sides print at game end.)
- [ ] Save -> quit -> load -> finish on the host with a client reconnecting to the resumed game: one
      continuous replay on both sides (`Saved`, `Resumed` markers present, `--verify` passes).
- [ ] Disconnect mid-game: the host writes both the #187 recovery save (with embedded replay) and the
      replay file; the client writes its replay.
- [ ] Size check against `docs/replay-plan.md` section 5 on three real GUI games (`--stats`); update the
      table with real beat numbers if #450's measurement was from the headless path only.
- [ ] Headless: `printf "2\n2\n" | dotnet run ... --headless` exits 0 and prints the replay path;
      `--scenario` writes a replay; `--replay FILE --dump/--verify/--make-save` work on it.
- [ ] Performance: a 2k game's recording adds no visible per-decision latency (compare `--slow 0`
      headless wall time with and without recording; < 3% is the bar) and the GUI frame time is
      unaffected during play.
- [ ] Docs: `CLAUDE.md` Build & Run gets the `--replay` lines and the smoke's expected replay line;
      `docs/EngineNotes.md` gets a "Replay" section (recorder attach points, container shape pointer,
      the `Resumed` boundary rule) and the key-files tree gains `Replay/`; `README.md` user-facing
      paragraph ("Replays" folder, how to open one, send it with a bug report); `Scenarios/README.md`
      mentions `--make-save` from a replay as a repro path; `docs/ResolverGuide.md` notes that
      `StageTaskNotifyResolvedMessage` now carries payloads (D2).
- [ ] Every sub-item's detail file has an Outcome and its index line is ticked and moved to
      `WorkItems/Archive.md`; plan section 6 statuses are `done`.
- [ ] Merge: `git fetch origin`; merge `origin/master` into `feature/replay` in the engine, run the suite,
      then in the superproject (bump pointer), run everything, push the branch; then fast-forward or
      merge `feature/replay` into `master` in the engine, push; bump + merge in the superproject, push.
      If `feature/opr-3.6` has already landed (#444), the `master` merge-in step is where its conflicts
      surface (plan section 8 says where to expect them: `LobbyScreen`, `RaylibRenderer`, request
      shapes). If it has not, tell the #415 owner that `master` now has replay so their next
      `master` -> `feature/opr-3.6` merge picks it up.
- [ ] Delete the remote `feature/replay` branches after the merge (both repos).

## Notes
- (none yet)

## Decisions
- (none yet)

## Outcome
(open)
