# 364 — Melee threat is measured straight-line, through impassible terrain

**Status**: open
**Related**: #363 (the sight-line half of the same "estimates ignore terrain" family), #264
(approach term switched to WALKING distance - this is the threat side, still straight-line),
#191 (Tactician umbrella)

## Goal

The Tactician's melee-threat pricing costs a charge what it would actually cost to make. Today
`TacticalAnalysis.MeleeThreatReach` = `ChargeDistanceAgainst` + 2", a pure straight-line
quantity, while the real charge has to be routed by `MovementPlanner`/`GridPathfinder` AROUND
anything Impassible. So an enemy standing on the far side of a solid block prices as if it could
charge through it, and every term keyed on that reach is wrong in the same direction:

- `Score`'s retaliation melee branch (charge threat onto the candidate endpoint),
- the projected-threat melee branch,
- `BestAlternativeTargetValue`'s melee alternative,
- the A5-6 "no credit for walking inside their threat reach" clamp on the approach term
  (`stageGap`) - which deliberately stayed straight-line under #264 with a "conservative in the
  right direction" argument that only holds while nothing else changed,
- `WantsDisembark`'s bail-out check and `ScreenLane`'s ward pick (26" pre-filter).

Net effect: the AI over-fears melee behind hard cover and under-values the ground a solid piece
actually protects - the same shape as #363's phantom volley, one mechanism over.

## Notes

- 2026-08-05: split out of #363 while implementing facet 3 (sight-gated incoming fire).
  #363 deliberately left melee alone - "a charge needs a path, not a sight line" - which is the
  correct sight-side call and exactly why the path-side error is now the visible one.
- Not a rewrite: `RouteMetrics`/`GridPathfinder` already compute walking distance for the
  approach term. The question is which of the threat call sites can afford a route query (they
  run per candidate x enemy) and which need a cheaper conservative test - e.g. "is anything
  Impassible on the straight segment at all", which is one cheap check that only pays for a
  route when it could matter.

## Decisions

(none yet)

## Outcome

(open)
