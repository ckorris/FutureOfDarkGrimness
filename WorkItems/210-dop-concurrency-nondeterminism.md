# 210 — Residual bench nondeterminism under concurrency (DOP > 1)

**Goal:** identical bench outcome hashes at any --dop, not just --dop 1.

## Problem

After the #209 weapon-order fix, seeded games reproduce exactly when run serially, but the SAME
10-game solo-vs-solo bench (Orks vs HEF, seeds 7000+) at the default DOP 16 still varies run to
run: hashes `351C1566CBE175C1` (x3, = the serial hash) / `55E24618EA1B53D7` / `825DF226C8E964BF`
across five runs. --dop 1 twice: bit-identical.

- The flipped games are SCATTERED across seeds (different rows each run), not clustered at
  process start - so it is not a lazy-static-init race at first touch.
- One-process `smoke --repeat 4` of a flipping game (seed 7002 swapped) is stable when games run
  SEQUENTIALLY - the race needs concurrent games in the process, and its probability rises with
  CPU contention (~1-2 flipped games per 20 at DOP 16 on this machine).

## Ruled out

- #209's weapon-pool enumeration (fixed; serial runs now reproduce exactly across processes).
- Per-process string-hash randomization (serial runs match across processes).
- FdgLab harness sharing: GameRunner builds fresh store/bus/server/registries per game; the
  timing/log sinks are lock-protected and decision-neutral; LabMessageBus dispatch is synchronous.
- `TEST_SINGLE_TURN` / `LaunchSingleTurnTester` (debug flag, off).

## Suspect directions (unproven)

Some process-wide mutable state the engine touches mid-game (static caches with unsynchronized
lazy population, a shared Random reachable from concurrent games, or ConcurrentDictionary
enumeration during a concurrent structural change). Finding it likely needs the #198 tracer wired
into the bench path (bench has no --dump-logs today) so two divergent DOP-16 runs can be diffed
game by game.

## Workaround / practical impact

- Exact reproducibility when needed: run the bench with `--dop 1` (~8x slower).
- At DOP 16 the effect is noise of roughly 1-2 games per 20 on THIS box; on 1800-game gates the
  percentage impact is small but hashes are only approximately reproducible. Gate/baseline ledger
  entries from 2026-07-10 onward note this.

## Notes (newest first)

**2026-09-03 - SECOND SERVER-GC SEGFAULT, this time with a core dump: a DOP-16 bench died
mid-run and silently produced no report.** During the #191 B+C campaign's step-2 baseline
(`bench --panel points-1k --profile-a tactician --profile-b solorules --games 100 --dop 16`,
seeds 6000), the FdgLab process crashed after 50/400 games. Evidence, unlike the 2026-07-27
occurrence whose cause was recorded as unknown: `dmesg` shows `.NET Server GC[24263]: segfault
at 0 ip ... error 4 in libcoreclr.so`, and systemd captured a 51.2MB core
(`coredumpctl`, PID 24228, Thu 2026-09-03 13:00:04 CDT, SIGSEGV). Not OOM - 22GB free at the
time. The fault is in the RUNTIME's GC thread, not managed code, and FdgLab deliberately runs
`ServerGarbageCollection=true` (#392, 2.2x throughput). So the two known crashes now share a
signature: DOP 16, Server GC, transient, no managed stack.

Whether this is the same root cause as this item's outcome nondeterminism is UNKNOWN and should
not be assumed - a GC-thread segfault and a decision-order race are different failure classes;
they share only "concurrency at DOP 16". Recorded here because this is where the first one was
filed and because the practical mitigation is the same.

**Practical impact, and why it mattered more this time:** the crash is survivable ONLY if the
runner notices. The step-2 script reported `exit=0` for the dead cell because `$?` had already
been reset by a `$(date)` substitution inside the same echo - so a lost cell looked like a
clean one, and the missing report was found only by reading the results. Any long unattended
run (the campaign's 4-day self-play window especially) must therefore: capture the real exit
code immediately after the command, RETRY a crashed run, and VERIFY the expected output file
exists rather than trusting the exit code alone. The campaign chain script now does all three;
step 4's self-play driver must too (its "crash-tolerant, restartable" requirement in
docs/tactician-bc-campaign.md now has a measured reason behind it).

**2026-08-06 (later) - the bit-identical repeat below does NOT mean the race narrowed; it means the
BINARY was the same.** While gating #365, two builds differing only by a hoist of a duplicated
`HasLineOfSight` call - verified behaviour-neutral - were run on the same matchup (Alien Hives vs
Orks, tactician vs solorules, seeds 1000+, 10 games): at DOP 16 they differed on **8 of 10 games**;
at `--dop 1` both produced hash `A5236375796FBCDA`. So determinism at DOP 16 is a PER-BINARY
property on this box, and the 640-game repeat below (same binary, bit-identical) is consistent with
that rather than evidence of a narrowed race. Practical rule for gates: any A/B rebuilds, therefore
any A/B carries schedule noise - compare paired game-by-game, never by hash equality. It also gives
whoever picks this up a much cheaper repro than the 20-game tactician cell: one neutral refactor,
one matchup, DOP 16 vs DOP 1.

**2026-08-06 — a 640-game Tactician-vs-SoloRules pool repeat came back BIT-IDENTICAL at DOP 16**
(#363's gate: control vs a control REPEAT of the same build, run sequentially ~50 minutes apart on
a 32-core box, 8-army pool x 64 ordered matchups x 10 games, seeds from 1000, Realistic dice).
Same outcome hash `6638851179176049`, zero flipped games out of 640. That is the opposite of the
2026-07-26 datum above (17/20 flips on a tactician-vs-tactician cell), so whatever the race is, it
is NOT hit uniformly: candidates worth checking when this item is picked up are (a) profile mix -
that cell was tactician on BOTH sides, twice the planner concurrency, (b) DOP 16 against 32 real
cores here vs the earlier machine/load, (c) code that has landed since (#209-adjacent ordering
fixes, the #361 pathfinder work). Worth reproducing the old cell before hunting further - the bug
may have narrowed or moved.

**2026-07-26 — tactician cells quantified during the #191 perf pass: outcome flips are an order
of magnitude worse than the solo baseline.** Same code (the #191 grid-cache build), same args,
two DOP-16 runs of a 20-game tactician-vs-tactician bench (Hives vs Orks, seeds 3000+): hashes
`09E3A940544AA18D` vs `D039BC2DC29A1B5F`, **17/20 outcome flips** - vs the ~1-2/20 recorded for
solo-vs-solo above (consistent with the 2026-07-10 note that INTERNAL divergence was already
16/20; tactician's argmax planning amplifies internal divergence into outcome flips). `--dop 1`
remains exact with tactician profiles: two same-binary runs of 3 matchups x 10 games (horde
mirror-ish, caster, transport+ambush) matched all three hashes (`6267BEA2307042D2` /
`16C0181B0279BAFB` / `1EEF569455930F1D`), and GUID-normalized logs are bit-identical across
`smoke --repeat` and across fresh processes. Practice adopted for #191: decision-neutrality and
attribution claims verify at `--dop 1` only; DOP-16 cell scores are treated as carrying schedule
noise on top of binomial noise (the two flipping runs above still both scored the cell 40.0 -
aggregates are far more stable than per-game outcomes). Process trap worth recording: a failed
stash-verification rebuild (untracked test file referencing a stashed-away class; exit code
unchecked) silently left stale binaries in place and nearly passed a cache-vs-cache comparison
off as old-vs-new - verify builds by exit code, and stash untracked test files with their
subject.
`bench --dump-logs DIR [--trace]` now writes per-game logs/traces with run-stable filenames
(superproject `053ca25`). Two untraced 20-game DOP-16 runs: 16/20 games diverge INTERNALLY
(GUID-normalized diff), far more than the outcome flips suggest. The signature (s7002_fwd):
logs identical up to a melee exchange where run 1 has a "Heavy Claw. Count: 1" batch and run 2
has "Count: 2" - the IN-RANGE MODEL SET differs while every logged prior event (moves, rolls,
casts) matches. So a model POSITION differs below log precision, or the range check itself is
reading racy state. The melee pool is rebuilt per exchange from InRangeAttackingModels
(MeleeRangeUtilities), so suspicion falls on pile-in/charge placement float paths or a shared
cache they read. **--trace does NOT reproduce it: two traced 10-game runs were bit-identical
(hash 351C1566CBE175C1, zero trace diffs) - the tracer's lock serializes enough to suppress the
race.** Next session: diff untraced logs to shortlist the exact combats, then instrument the
melee-range check inputs (positions at full precision) via a lighter-weight channel than the
global tracer.
