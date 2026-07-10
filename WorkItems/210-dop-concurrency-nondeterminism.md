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

**2026-07-10 — filed.** Found while verifying the #209 fix (which this item's evidence isolates
from). Pre-existing; every historical bench hash was a one-shot sample.
