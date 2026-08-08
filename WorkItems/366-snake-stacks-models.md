# 366 — The on-path snake stacks a unit's own models (and no validator forbids it)

**Status**: in-progress
**Related**: #256 (S4 snake), #264 (issue 4/5 snake gating), #205 (friendly end-overlap), #159 (lenient coherency)

## Goal
Three defects, one save (`YellowWarriorsMovedBackAndSomehowModelsOverlap.fdgsave`, 2026-08-08):
1. `MovementPlanner.BuildSnakeToSide` clamps every rank that does not fit the arc onto the SAME route
   point, so a big unit's models end stacked on each other.
2. `MovementUtilities.ValidatePaths` has no same-unit end-overlap rule at all, so the engine accepts
   that move. (The GUI resolver DOES block it for a human, which is why it reads as "guarded".)
3. The AI picks the collapsed candidate because the head reaching the arc looks like forward progress,
   even though most of the unit walked backwards into the pile.

Done = the snake never gives two models the same destination, the validator rejects a same-unit
end-overlap ("not worsened", so an already-stacked unit isn't frozen), and the ladder stops accepting a
degenerate snake. Engine suite green, headless smoke exit 0.

## Notes

- 2026-08-08: **Slice 1 landed** (engine `048e72b`) - `BuildSnakeToSide` no longer floors overrunning
  ranks onto `path[0]`; they hold position (`MinSnakeRankArcInches`). Two red-by-design pins in
  `MovementPlannerTests`: `BuildSnakeCandidate_ArcShorterThanTheFile_NeverStacksTwoModelsOnOneSpot`
  (builder level) and `PlanMoveToward_BlockedBigUnitWithNoRoomToFormAFile_NeitherStacksNorRetreats`
  (ladder level, on the save's geometry - blocking building diagonally across the lane, 4" advance);
  both red before, green after. Suite 2913/2913, smoke exit 0.
  Measured on the save's 4/4/3 deploy grid at a 2" arc: pre-fix the ranks that overran walked
  -0.90 / -0.42 / -0.20 / -0.04" backwards into two piles; post-fix seven models hold at exactly 0.00
  and the unit's centroid displacement drops from +0.30" to -0.06". That negative centroid is the
  point: the collapse used to LOOK like progress to `ForwardProgress` because piling the rear models
  onto the start dragged them forward. With honest geometry the ladder's existing gate now sees a
  degenerate snake for what it is. **Kept explicitly out of scope for this slice** and pinned as
  a bound rather than zero: a model that IS advancing may still travel backwards along the route
  while funnelling onto the route line from a flank (measured -0.90" here) - that is the corridor
  behaviour #256/#264 built the snake for, not the reported defect.

- 2026-08-08: Diagnosed from the save. Unit 29 `Warriors (Combined)`, 11 models r=0.62992", owner
  `Tactician Bot 3` (slot 3, team 2), round 1, `MovedThisRound` + `ActivatedThisRound`. Its 11 models
  occupy **6 distinct points**: 4 exactly on (5.467,2.860), 3 exactly on (5.032,1.572), plus 7 further
  genuine base overlaps (0.59" centre-to-centre where 1.26" is needed). Exact coincidence => distinct
  models were handed the same destination, not a geometry rounding problem.
  Reconstructed the candidate exactly: snake, spacing `2r+0.1 = 1.35984"`, `files = 2`, `ranks = 6`,
  **arc = 2.000"**. Ranks 0/1 get arcs 2.000 / 0.640; ranks 2-5 all clamp to 0.05 (`MovementPlanner.cs:614`,
  `Math.Max(0.05f, arc - rank * spacing)`) => 4 models on one point, 3 on the other. A throwaway probe
  feeding an 11-model unit + a 2" arc into `BuildSnakeCandidate` reproduced the save's six positions to
  three decimals with the same 4/3 multiplicities, and `MovementUtilities.ValidatePaths` accepted the
  result with **0 errors**.
  The snake needs `arc >= (ranks-1) * spacing = 6.8"` to lay this unit out collision-free, which a 4"
  advance can never supply - so the collapse is structural for any large unit, not an edge case.
  Backwards movement is the same defect: the tail pile sits at `path[0]`, which IS the pre-move centroid
  (reconstructed (4.985,1.588)), so every model that started ahead of the centroid must walk backwards
  into it. Unit centroid gained 0.54" toward the enemy while front-rank models went ~1.4" backwards and
  the left flank slid ~2.5" east into the Central building's shadow.

## Decisions

- 2026-08-08: For ranks that do not fit the arc, **hold position** rather than (a) extending the file
  backwards behind `path[0]` or (c) refusing the snake outright. (a) is the geometrically honest column
  and would let the existing `ForwardProgress` gate reject the degenerate case for free, but forming a
  column from a blob genuinely costs backward movement - which is the very thing being reported. (c)
  would regress the #256/#264 cases the snake exists for (the walled Battle Brothers pocket cleared at a
  3" arc with more ranks than 3" can seat). Holding matches the documented intent - "the first snake move
  mostly STRETCHES the unit into the file" - and means no model ever moves backwards in a snake; the file
  forms over successive activations. Cost: a held model can be overlapped by an advancing one, which is
  exactly what the new same-unit validator is there to catch, so the ladder backs off instead.

## Outcome
