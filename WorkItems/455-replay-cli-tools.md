# 455 — Headless replay tooling: dump / stats / verify / export / make-save

**Status**: done (2026-10-30)
**Related**: plan `docs/replay-plan.md` ("useful for an agent to debug"); depends on #454; sibling of the #167 scenario tools (`--make-scenario`, `--scenario`)

## Goal
From a `.fdgreplay` (or a `.fdgsave` with an embedded replay) an agent can, without a display: print a
readable timeline of decisions with the log and beats that followed each; print every event; print a
size breakdown; verify the recording's integrity; export plain JSONL for grepping; and cut a resumable
`.fdgsave` at any step so a reported bug turns into a scenario/repro save in one command. Done when each
flag below works on a replay written by the headless smoke and is documented in CLAUDE.md's "Build & Run"
block (two lines, like the scenario tools) and `Scenarios/README.md`.

## Scope (`FdgRaylib/Cli/ReplayTool.cs`, wired in `Program.cs` next to `--make-scenario`)
All forms accept `.fdgreplay` or `.fdgsave` (embedded replay via `GameSaveSerializer.LoadWithReplay`);
all exit 0 on success, 1 with a one-line reason otherwise, and never open a window.
- `--replay FILE --dump [--from N] [--to M] [--folded] [--no-log] [--no-beats]`: one block per step:
  `#012  R2 A05  P1 (AI)   ChooseActionRequest   Warriors: Shoot` then indented `log:` lines and `beat:`
  one-liners (`PresentationBeat` type + its text fields) from the step's span, and `store: 14 updates
  (UnitData x3, Position x10, GameProgressData x1)`. Markers print as their own lines. `--folded` also
  prints folded requests. Header first: file, format/build, GameId, seed, randomness, slots with
  controllers, rounds, steps, outcome, duration.
- `--replay FILE --events [--from SEQ] [--to SEQ] [--kind K,...]`: every event, one line each, store
  events as `Seq Kind Type Ref` plus, for `StoreUpdate`, the names of top-level JSON properties whose
  value differs from the previous recorded value of that reference (a cheap field diff; the full JSON
  on `--verbose`).
- `--replay FILE --stats`: the plan section 5 table for this file (counts and raw/gzip bytes by kind,
  by store type, by request type; snapshot sizes), so the estimate stays honest as the game grows.
- `--replay FILE --verify`: for every `Snapshot` after the first (not crossing `Resumed`), play deltas
  from the previous snapshot and compare `GameSaveSerializer.Save` to the snapshot's save JSON; report
  the first divergence (Seq, type, reference, differing properties). Also: every `Request` has a
  `Reply`/`RequestError`; `Seq` dense; exactly one `GameEnded` unless the game was abandoned.
- `--replay FILE --export OUT.jsonl`: the decompressed container, verbatim (for `grep`/`jq`).
- `--replay FILE --at STEP --make-save OUT.fdgsave`: `ReplayPlayer.SeekToStep(STEP)`, then
  `GameSaveSerializer.Save(player.Store, replayUpTo: STEP)` - embed the replay truncated at that step
  with a `Saved` marker so resuming it continues the recording. Print the caveat that a resume restarts
  at the activation boundary the store's `GameProgressData` points at (the #052 rolling snapshot), so
  the resumed game re-plays that activation from its start. `--at end` = after the last step.
- Output is ASCII; width-stable columns; no colour codes (it is read by agents and diffed).

## Tests (`FdgRaylib.Tests/ReplayToolTests.cs`)
- Run the tool's entry method in-process on a replay recorded by a tiny in-process game (reuse the
  engine test's recorder fixture via a shared helper, or record one in the test); assert `--dump`
  contains every step index once and the header fields; `--verify` passes; `--verify` on a replay with
  one `StoreUpdate` event removed reports the right Seq; `--export` decompresses to N+1 lines; `--at 3
  --make-save` produces a save that `GameSaveSerializer.LoadWithReplay` loads with a replay whose last
  marker is `Saved`.

## Verification
`dotnet build`; FdgRaylib.Tests green; headless smoke -> `--replay Replays/<file> --dump` prints a timeline
with ~300 steps; `--verify` passes on it; `--at 20 --make-save /tmp-like scratch path` -> `--scenario`
that save resumes and plays to the end headless (`printf "2\n2\n" | ... --headless --scenario out.fdgsave`
is NOT the right flag set - check `ScenarioLauncher` accepts a `.fdgsave` directly; if only JSON
scenarios are accepted, add `--resume FILE` as the headless twin of the GUI's Load Game, and say so).

## Notes
- 2026-10-30 slice 2 (engine db9862e4): `FdgRaylib/Cli/ReplayTool.cs` (`--dump`, `--events`, `--stats`, `--verify`,
  `--export`, `--at --make-save`), wired in `Program.cs` before `--make-scenario`; `ReplayText` made public for
  it. Found and fixed a #454 bug: the build-mismatch warning fired between any two dev builds, because the SDK
  appends the commit (`0.3.0-dev+<sha>`); `ReplayReader.SameBuild` now ignores the `+` part between `-dev`
  stamps (release stamps still match exactly). 7 app tests (`FdgRaylib.Tests/ReplayToolTests.cs`) on a replay
  recorded in-process through the `--scenario --all-ai` path (example-shootout, seed 455), +1 engine test.
  Engine 3455 passed / 1 skipped, app 3066 passed, build clean, smoke exit 0.
  Checked by hand: on the 2k BB vs Orks seed 42 replay (#454's) `--verify` PASS (4 snapshots compared, 1234
  deltas, 421 beats, 2.3 s), `--dump` 2323 lines, `--stats` matches plan section 5, `--export` byte-identical
  to `zcat`; `--at 96` and `--at end` saves resume with `--headless --scenario --all-ai` to a Game result.
  On a headless-smoke replay (written by a temporary, reverted `FDG_REPLAY_OUT` patch in `CliApp` - archived
  at `~/Projects/fdg-lab-scratch/replay-455/CliApp.export.patch`; #452 owns writing it for real): 97 steps
  (+17 folded) - the smoke plays the two-unit test army, so not the ticket's "~300", which assumed a 2k list -
  `--verify` PASS (4 snapshots, 366 deltas, 103 beats), `--at 20 --make-save` -> `--headless --scenario` resumes
  to a Game result both with the human slot on EOF defaults and with `--all-ai`.
- 2026-10-30 slice 1 (engine 5212670a): `FDG.Replay.ReplayVerifier` + `ReplayJsonDiff`. Checks: `Seq` from 0 and
  dense; every request answered or failed unless still open at the end; no reply without its request (host) and
  no second answer; at most one `GameEnded` (none = a note); every snapshot after the first rebuilt from its
  predecessor + deltas and compared as a re-saved store (not across `Resumed`), with a per-entry divergence
  report; every delta applies and every beat reads back at its position. 7 engine tests: a clean scripted
  recording, a removed update (gap at its Seq + the divergence it causes), a corrupted update (anchored to its
  Seq, names the field), request/marker tampering, no snapshot, a real in-process game verifies clean, the diff.
  Engine 3454 passed / 1 skipped.

## Decisions
- **Verifier lives in the engine** (`FDG.Replay.ReplayVerifier`), the CLI only formats it: it needs playback's
  exact snapshot load (`stampLegacyReserves: false`, internal) and `EDeltaGenerationPolicy.Adopt`, and an
  engine test can pin it against a real in-process game. Results are structured (`Seq`, check, message,
  details) so tests assert on the Seq rather than parse text.
- **Field diff = top-level JSON properties compared as token trees** (`ReplayJsonDiff.ChangedProperties`),
  shared by `--verify` and `--events`. Key order is not a change; non-object values (a bare `Float2`, a
  string entry) differ as `(value)`. No nested paths: the top-level name (`_tokens`, `Position3D`) is enough to
  point at the bug, and the full JSON is one `--verbose` away.
- **"Unanswered" is judged per player**: a request with no reply/error fails only when the same player answered
  a later request (it was skipped, not pending); ones still open at the end are one note. A `RequestError` is
  a note (it is a recorded fact, not a broken recording). A reply to an unknown task fails on a host
  recording, a note on a client one (#451 clients may join mid-request).
- **No `--resume` flag**: `--scenario` already loads a `.fdgsave` directly (`ScenarioLauncher.LoadStore`), headless
  or GUI, so a cut save plays with `--headless --scenario OUT.fdgsave [--all-ai]` and the tool prints that line.
- **`--make-save` refuses a step before the main phase** (exit 1, naming the first resumable step): the progress
  record is only written at the first activation boundary and `FDGServer`'s resume constructor throws without
  it, so a save from deployment would be a trap. The caveat it prints names the cut's activation and, when the
  step is inside one, the activation's first step (`--at K`) for a clean cut.
- **`.fdgsave` input and the embedded truncated replay with a `Saved` marker are handed to #453** (they need
  #453's `GameSaveFile.Replay`): `--replay X.fdgsave` exits 1 saying so, and `--make-save` says its save carries
  no replay. Recorded in #453's scope. Likewise the "replay written by the headless smoke" check is re-run by
  #452 once replays are written for real.
- **Output choices**: `--dump` hides Debug-category log lines unless `--debug-log` (a 2k game has ~1500 of them;
  the header says they are hidden); store changes are tallied per type and flushed before each marker and at
  each step's end; a beat prints its type, its `Text`, then any other prose-like string field (durations and
  enum names skipped). `--events` marks each request `[step N]`/`[folded]` and prints a step's reply as its
  summary (others as compact JSON without `$type`); T is seconds since the recording began. `--stats` gzips each
  group on its own (so groups do not sum to the file, said in the output). Column widths come from the whole
  file, never the `--from/--to` window, so two windows of one file diff cleanly.
- **Divergence anchoring**: each differing entry carries the Seq of its last recorded change in the window
  ("no change recorded since snapshot N" when none); the issue is anchored to the earliest. A removed event
  is reported exactly by the dense-Seq check; a corrupted one by its own Seq. After a divergence the check
  reloads the recorded snapshot and carries on, so one bad delta is one failure.

## Outcome
Done 2026-10-30 on `feature/replay` (engine 5212670a, db9862e4; superproject ede90fc, 5a83c41, + the close-out
commit). `--replay FILE` with `--dump`, `--events`, `--stats`, `--verify`, `--export OUT.jsonl` and
`--at STEP|end --make-save OUT.fdgsave` works headless on a real 2k replay and on a headless-smoke replay;
a cut save resumes with `--headless --scenario` (human slot on EOF defaults, or `--all-ai`) to a Game result.
Documented in CLAUDE.md "Build & Run" and `Scenarios/README.md` ("From a replay"). Integrity checking lives in
the engine (`ReplayVerifier`, 7 tests); the CLI has 7 app tests on an in-process recorded scenario game.
Found and fixed on the way: the #454 build-mismatch warning fired between dev builds of different commits.
Not done here, by design and handed over explicitly: `.fdgsave` input and the embedded truncated replay with
a `Saved` marker (#453's scope, now listed there); re-running the checks on a replay the headless smoke writes
for real (#452's verification, now listed there).
