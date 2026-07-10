# 205 — AI unit with a large rectangular base drove over friendly models

**Status:** open — needs exploration (filed 2026-07-09 from Chris's play report)
**Related:** #150 (base-shape geometry everywhere), #182 (move through friendlies without stopping
on them), #011 (ending-stacked checks), #018/#019 (pile-in/consolidation stacking)

## Report (verbatim intent)

During a game, an AI unit with a large rectangular base ended up driving over friendly models —
Chris believes during a charge or pile-in. Whether the offense is passing THROUGH friendlies
(legal per the rules? see #182's scoping: friendly pass-through is allowed, ending stacked is not)
or ENDING on top of them (never legal) needs establishing first.

## Exploration notes for whoever picks this up

- Movement validation today only checks ENEMY footprints (#182's headline); the "can't end
  overlapping a different friendly unit" guard does not exist yet - if the rect base ENDED on
  friendlies, this may simply be #182 manifesting at its ugliest (a big vehicle base makes the
  missing guard visible), and the fix may just be #182 with rect-base-aware geometry.
- Pile-in/consolidation paths (`PileInStage`, `ConsolidateStage`, `AiConsolidationMoveResolver`)
  have their own move construction - check whether they run ANY overlap validation at all.
- #150's shape-aware geometry landed for collision/swept paths; verify the AI's charge/pile-in
  construction uses the oriented rectangle, not the circumscribing radius (a rect base validated
  as a circle can legally "cover" models its true footprint overlaps... or vice versa).
- Repro: play/replay a pool game with the HDF tough/vehicle list (large rect bases) vs a horde;
  seeded games make any observed instance replayable exactly - grab the seed when it happens.

## Notes

- 2026-07-09 — filed.
