# 392 — FdgLab parallel bench: concurrent games couple through shared state (outcomes depend on the interleaving)

**Status**: filed (2026-08-31, found during a bench-speed pass; investigation not started)
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

- 2026-08-31: filed. Found while measuring Server GC for bench throughput (2.2x: 1.37 -> 3.03
  games/s at dop 16, decision mean 6.10 -> 2.75ms) - adopting Server GC (and/or higher --dop on
  the 32-core machine) is WORTH IT but re-baselines the hash lineage, so it should land at a
  gate boundary, ideally after this leak is found and fixed.

## Decisions

(none yet)

## Outcome

(open)
