# 209 — Weapon-choice option order is nondeterministic (breaks #193 same-seed replay)

**Goal:** same seed => same game, including multi-weapon units. Restore the #193 determinism
contract that all benchmark hashes and seed-repro debugging rely on.

## Problem

`CombatActionContext._availableWeapons` is a `ConcurrentDictionary<Weapon, int>` keyed by the
`Weapon` reference type (identity hash). Enumeration order therefore depends on the allocation
addresses of the fresh per-game `Weapon` objects - it varies run to run AND game to game within
one process. Both choice stages enumerate it to build their option lists:

- `ChooseMeleeWeaponStage.cs:41` - builds the `StringSelectionRequest` options in dict order.
- `ChooseRangedAttackStage.BuildWeaponOptions` - same pool, same enumeration.

The solo AI picks `ValidOptions[0]`, so any unit with 2+ distinct weapon types swings/fires in a
random order; the dice stream shifts and the whole game diverges from there.

## Evidence (2026-07-10)

- `smoke --seed 3105 HEF-vs-Orks --profile-a tactician` x3 in one process: scores 2-0 / 2-1 / 2-2.
- Same at engine `dd0b1f1` (pre-A5): 3-1 / 2-1 / 3-1 - NOT introduced by the A5 casting slice.
- Solo-vs-solo affected too: identical 10-game Orks-vs-HEF bench run twice gave hashes
  `FEE1DB956618CC30` vs `E74D9E47A20B8C0F`.
- First log divergence (GUID-normalized diff of `--dump-logs`): "Chose weapon: Mirror Scythe"
  fires at a different position among the unit's melee batches; a shifted save roll then flips a
  morale test.
- Why the #193/#198 pins never caught it: the builtin determinism-gate armies' units each carry
  ONE weapon type, so the option list always had a single entry.

## Consequences while open

- Every frozen outcome hash (solo pool baseline v3 `0888D6E37A1F11E8`, all #191 gate hashes) is a
  one-shot sample, not a reproducible artifact. Benchmark PERCENTAGES stay valid as statistics.
- Same-seed fault repro (the #207 workflow) is unreliable for games containing multi-weapon
  melee/shooting.

## Candidate fix (engine core - needs Chris's sign-off)

Deterministic option order at the two consumption sites: sort melee `validOptions` (and the
invalid list, for transcript stability) by label in `ChooseMeleeWeaponStage`, and sort the weapon
batches by name in `ChooseRangedAttackStage.BuildWeaponOptions` (names are unique there - it
dedupes by name and throws on collisions). Alphabetical is arbitrary but transparent, and the #028
Deadly-first gating already overrides priority where order matters for rules. Pin: build the
option list from the same weapon pool inserted in two different orders - identical labels out.
Afterwards: re-freeze the solo pool baseline (v4) and treat prior gate hashes as historical.

## Notes (newest first)

**2026-07-10 — FIXED (Chris-authorized), engine `52d1968`.** Both consumption sites order their
options deterministically (melee: ordinal sort of the option labels + invalid list;
ranged: BuildWeaponOptions returns name-ordered). Pinned by WeaponOrderDeterminismTests (same
weapon set inserted in opposite orders -> identical option sequence, both stages). Verified: the
original repro (smoke seed 3105 HEF-vs-Orks, Tactician) is now bit-identical across three
separate processes; the solo 10-game mini-bench reproduces its hash exactly at --dop 1 across
processes. Remaining DOP>1 flips split off as #210 (separate, contention-dependent mechanism).
Baseline re-freeze (v4) pending the A5-1 gate.

**2026-07-10 — filed.** Found during A5-1 casting verification (a G2 smoke of HEF-as-Tactician
gave different outcomes per run). Root-caused the same day; fix awaiting authorization.
