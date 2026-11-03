# 330 — Pile-in maximizes base contact (slot assignment instead of formation-keeping rays)

**Opened:** 2026-08-04. **Status:** DONE 2026-08-04 (owner hand-verified in the GUI).

## Goal

Owner's field note: "Pile-in doesn't seem to go all the way... I'll usually see the target model move,
but it keeps its formation. It should move to maximize the number of models in contact, while
considering terrain and of course model overlap."

GF v3.5.1 p.9: defender models not in base contact must move by up to 3" to get into base contact
with a charging model, or as close as possible, maintaining unit coherency.

Today `PileInUtilities.ComputePileInMoves` marches each defender on a straight ray toward its
nearest charger, with unit-mates as hard obstacles - so a second-rank model stops dead against the
first rank's back and the unit translates in formation instead of enveloping. Replace with:

1. **Contact slots**: sample candidate positions around every charging model's base where a
   defender base of the given shape/facing would sit in base contact (36 directions per charger,
   settled by bounded binary search on `BaseShapeGeometry.SurfaceGap2D`).
2. **Reachability filter** per defender: straight-line center move <= 3", swept path clear of
   impassable terrain + chargers + third-party enemies/friendly obstacles (fellow defenders are
   NOT swept obstacles - the unit shuffles simultaneously; final positions still may not overlap).
3. **Greedy assignment, most-constrained defender first** (fewest viable slots picks first);
   deterministic tie-breaks (move distance, then slot index, then input order).
4. **Fallback** for defenders with no viable slot: the previous ray-step behavior, unchanged.
5. Keep the existing coherency revert pass; add a residual-overlap revert pass as a final
   belt-and-braces (bounded: each iteration reverts one mover).

Crash-safety is the owner's top priority: the function stays pure (positions in, move list out),
all loops bounded, every emitted position pre-validated by the same overlap/swept primitives the
current code trusts. Worst failure mode = a conservative (formation-like) result, never a crash.

## Out of scope (deliberate, not silently cut)

- True pathfinding around terrain within the 3" budget - kept as "straight step or stay put".
- Charger-side placement (chargers can clump the same way); separate item if wanted.
- Rotating defenders during pile-in (models keep their facing, as today).

## The other half of the field note

"Only models within 2 inches of a model of the other side get to attack - on both sides" is
ALREADY implemented (#017): `DetermineInRangeAttackersStage`, `DetermineInRangeDefendersStage`,
and `StrikeBackStage` all gate through `MeleeRangeUtilities.AreModelsInMeleeRange` (2" horizontal
b2b + 4" vertical). Verified in code 2026-08-04; no change needed.

## Notes

- **2026-08-04** — Implemented + tested. `PileInUtilities.ComputePileInMoves` restructured:
  Phase 1 slot assignment (`AssignDefendersToContactSlots` / `GenerateContactSlots` /
  `IsSlotEndStateFree` / `IsSlotPathClear`), Phase 2 = the old ray-step verbatim for defenders no
  slot could take, then the existing coherency revert + new `RevertResidualOverlaps` safety pass.
  `PileInStage` log line now reports the contact tally ("N moved (M in base contact)").
  5 new tests (14 total in `PileInTests`), incl. the owner-requested partial-terrain cases:
  a thin wall dipping into the direct lane - defender takes an open southern flank slot instead of
  the pre-#330 "terrain on the ray = stay put". Suite 2666/2666 green; headless smoke exit 0 with
  live log lines showing all movers reaching contact (e.g. "3 moved (3 in base contact)").
- **2026-08-04** — Item opened; approach signed off by owner in session ("I like that... make it
  so"), with the explicit ask for tests where terrain is deliberately partially in the way.

## Decisions

- Slot sampling: 36 directions/charger (10 deg); slots keyed per distinct (defender shape, facing)
  group so mixed-base units get correct per-shape contact positions.
- Fellow defenders transparent during the sweep, hard at the end state (simultaneous-shuffle
  interpretation) - this is what lets the unit actually envelop.
- Contact tally added to the pile-in log line for observability.

## Outcome

Shipped 2026-08-04, same day as opened. Engine `23fdbf2`: contact-slot assignment around chargers
(36 sampled directions per charging model, bounded bisection on SurfaceGap2D, most-constrained
defender first, deterministic tie-breaks), swept/overlap validation via the existing #150
primitives, old ray-step retained as the fallback, coherency revert kept + a residual-overlap
revert added. `PileInStage` log line reports the contact tally. 5 new tests (14 in `PileInTests`),
incl. the owner-requested partial-terrain wraps; suite 2666/2666 at commit (2760/2760 after the
rule-log-wording merge). Hand-verified by owner 2026-08-04 in the GUI via
`Scenarios/pilein-wrap-demo.json` / `PileInWrap.fdgsave` (lone tough charger vs a 5-model block
with a wall clipping the wrap arc): "works quite well".

The note's other half (2" attack gate both sides) needed no work - already live via #017's
`MeleeRangeUtilities` chokepoint.
