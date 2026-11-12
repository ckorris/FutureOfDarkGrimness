# 392 — FdgLab parallel bench: concurrent games couple through shared state (outcomes depend on the interleaving)

**Status**: CLOSED (2026-08-31, same day) - root-caused and fixed engine-side + harness-side,
regression-pinned, Server GC adopted; see Outcome
**Related**: #193 (determinism/seeding contract - this is a violation of it under parallelism),
#210 (bench divergence hunting / the DOP-16 segfault family), #194 (FdgLab harness), #198
(position-write trace tooling)

## Goal

A game's outcome depends only on its spec + seed - never on which OTHER games happen to be
running in the same process. Done = the discriminating experiment below produces IDENTICAL
hashes, and a regression gate exists for it.

## Evidence (2026-08-31, measured on the Release build, 32-core machine)

Same binary, same args (`bench --a "Robot Legions 2k" --b "Orks 2k" --games 16 --seed-base
1000`), the only variable being GC mode (`DOTNET_gcServer`), which changes nothing about game
math but changes scheduling/throughput:

- `--dop 1`: ws hash == srv hash (`E8343762F4CF8A69`). No overlap -> no divergence. GC mode
  provably does not alter outcomes.
- `--dop 16`: ws `4D40CB1DF11F9BF6` vs srv `BD26A410D85A7F2C` - **7 of 16 games diverge**
  (Win->Tie flips, one-point score shifts, same round counts - single late decisions moving).
- Each mode SELF-reproduces exactly (two ws runs identical, two srv runs identical): the
  contamination is deterministic GIVEN the interleaving, which is why hash comparisons between
  same-config runs (the historical gate methodology) kept working and the leak went unseen.

Implication: bench hashes are only comparable between runs of the SAME config on the SAME
machine; cross-config/cross-machine comparisons are unsound until this is found. Same-config
A/B legs (e.g. the #389 gate cycle) remain valid.

## Suspects (unverified)

Mutable static/shared state reachable from game logic - a static cache or memo keyed without a
game identity, harness-level sharing in FdgLab (LabMessageBus/GameRunner), or engine statics
written during play. The #210 DOP-16 segfault ("plausibly a race under load") may be this
family's crash-shaped cousin. Note object-identity hash codes and fresh GUIDs (PlayerID) are
NOT it - they differ between any two runs, including the ones that reproduced.

## Repro / discriminating experiment

```
FdgLab bench --a <army> --b <army> --games 16 --dop 16 --out A          # workstation GC
DOTNET_gcServer=1 FdgLab bench ... --dop 16 --out B                     # server GC
# diff A/bench.csv B/bench.csv outcome columns; --dop 1 as the control (must match)
```

## Notes

- 2026-08-31 (cont.): **Root cause found and fixed, same session.** The hunt, in order:
  (1) `--dump-logs` flipped the workstation-dop16 run to the server-GC outcome - the observer
  effect killed the "GC mode" framing; any perturbation tips it. (2) dop1 = truth for seed 1000;
  the choked ws-dop16 run was the DIVERGENT one (26 extra decisions). (3) dop4 produced a THIRD
  hash - different seeds diverge at different concurrency; each config still self-reproduces.
  (4) The discriminating experiment: fresh `Armies.LoadSlot` per GAME instead of per matchup ->
  the divergent config snapped to truth, and ws/srv x dop4/16 all agreed. Sharing confirmed as
  the carrier. (5) A serialize-before/after probe in smoke showed slot 1's army JSON REORDERED
  after one game: three Ork units' weapons lists resorted in place. (6) The write:
  `UnitData.cs:141` - the constructor sorted `unitFileEntry.Weapons` (quantity ascending, for
  model distribution) IN PLACE on the caller's ArmyListFile. Idempotent, so sequential reuse
  (`smoke --repeat`, the #193 gate) never showed it; two concurrent games racing sort vs
  enumeration captured different weapon orders, and weapon order feeds resolution order + dice
  consumption -> full-game butterflies.
- 2026-08-31 fixes shipped: **engine** `16977e7` sorts a copy + `ArmyFileImmutabilityTests`
  (whole-file byte-identity pin over the real launch path, red-checked); **harness**
  `Benchmark.cs` loads a fresh ArmyListFile per game (isolation structural, not politeness);
  **FdgLab.csproj** adopts ServerGarbageCollection at this re-baseline boundary. Verification
  matrix on the full stack: ws/srv GC x dop 4/16/24 x logs on/off all hash `BD26A410D85A7F2C`
  on the RL-vs-Orks 16-game repro. Engine suite 3090/3090 green. Throughput ~2.8 games/s on the
  repro (was 1.37); the #389 gate legs rerun at dop 24 in ~1/3 the wall time.
- 2026-08-31: filed. Found while measuring Server GC for bench throughput (2.2x: 1.37 -> 3.03
  games/s at dop 16, decision mean 6.10 -> 2.75ms) - adopting Server GC (and/or higher --dop on
  the 32-core machine) is WORTH IT but re-baselines the hash lineage, so it should land at a
  gate boundary, ideally after this leak is found and fixed.

## Decisions

- 2026-08-31 (Chris): "Wanna start hunting for the 392 leak now?" - hunt authorized; the engine
  fix is the engine-first call CLAUDE.md standing policy covers, and the harness isolation +
  Server GC adoption land together as the one re-baseline boundary.
- 2026-08-31: hash lineage re-baselined - numbers/hashes recorded before this date were
  measured on the contaminated harness and are NOT comparable to post-#392 runs. The #389 gate
  legs were rerun on the fixed stack (numbers of record in #389's ledger).

## Outcome

Concurrent bench games coupled through the shared per-matchup ArmyListFile: army creation
sorted each unit's weapon list in place, and racing games captured different weapon orders.
Fixed engine-side (sort a copy, byte-identity regression pin) and harness-side (fresh army per
game); Server GC + dop 24 adopted at the same re-baseline boundary after proving outcomes are
config-independent on the fixed stack (identical hashes across GC mode / dop / log capture).
The old #210 "DOP-16 segfault" family is plausibly this bug's crash-shaped cousin (a List.Sort
racing an enumerator can throw, not just reorder) - if bench crashes stay gone post-fix, that
item can note this as the likely cause.
