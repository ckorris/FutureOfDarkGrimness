# 366 — The on-path snake stacks a unit's own models (and no validator forbids it)

**Status**: implemented + tested; awaiting in-game confirmation
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

- 2026-08-08: **Slice 3 landed** (engine `cafb873`) - the snake's value gate. Its bar was the bare
  `MinBackoffStepInches` (0.05"), which the collapse cleared easily. Instrumented every accepted snake
  across the suite: legitimate ones gain 0.44-0.95 of their arc (n=232, median 0.76), degenerate ones
  0.04, so `SnakeMinProgressFraction` is 0.25 - a wide margin either side. Pin:
  `ValidateWithBackoff_SnakeThatBarelyAdvances_IsRejectedForTheHalvingLadder`, red without the gate.

- 2026-08-08: **Measured against the reported save.** `FdgLab analyze` on
  `YellowWarriorsMovedBackAndSomehowModelsOverlap.fdgsave`, same unit, same state:
  - before: every macro action scored -0.0004 and ended at the same (6.3,2.7) - a 0.85" nudge that the
    ladder manufactured for each one, with Hold at -0.0099 losing to all of them;
  - after: top candidate 0.1338, `RushObjective` end (4.5,6.5) - a real 4.4" advance up-field - and the
    degenerate candidates are honestly reported `Blocked` instead of a fake `BudgetClipped` nudge.
  Bench A/B (builtin vs builtin-basic, tactician both sides, 40 games): pre-#366 71.3% 25/8/7, post-#366
  71.3% 24/7/9, **0 faults / 0 timeouts both**, decision cost flat (39.9 -> 39.8ms mean). Feasibility
  probe 100% (gate >= 95%), 0 generator faults. Suite 2918/2918, app 1140/1140, build clean, smoke 0.

- 2026-08-08: **Slice 2 landed** (engine `e6690f4`) - the validator rule, plus a correction to slice 1.
  `ValidateNoSelfOverlap` runs in all four `ValidatePaths` forms and in `ValidateConsolidationPaths`,
  "not worsened" like the friendly-overlap and off-table rules; new `EErrorReasonType.EndedOnOwnUnitModel`
  with GUI text. Four pins in `MovementValidationTests` (same spot / overlapping-but-not-coincident /
  clear / already-stacked-still-moves); the two rejection pins are red without the rule.
  Turning the rule on immediately broke three existing tests, all of them honest catches:
  - `BeyondRush_DifferentModelEndsInMelee_Accepted` - the FIXTURE ended two 0.75" bases 1" apart. It was
    about charge reach, never about spacing; the overshooter is now offset off the lane.
  - the two `TacticianWalledUnitTests` corridor cases - these exposed a SECOND latent defect (below).
  Suite 2917/2917, app 1140/1140, `dotnet build` clean, smoke exit 0.

- 2026-08-08: **Latent defect found by the new rule**: the snake staggered ranks by ARC length, but
  around a bend equal arc steps are a much shorter straight-line gap, so consecutive ranks bunched into
  each other at exactly the corners the snake exists to round. Invisible for as long as nothing checked a
  unit against its own models. Ranks now back off along the route (bounded, `SnakeRankBackoffSteps`)
  until they clear the rank ahead by a full base width.

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

- 2026-08-08 (**reversed same day** - see the entry below): for ranks that do not fit the arc, **hold
  position** rather than (a) extending the file backwards behind `path[0]` or (c) refusing the snake
  outright. (a) is the geometrically honest column
  and would let the existing `ForwardProgress` gate reject the degenerate case for free, but forming a
  column from a blob genuinely costs backward movement - which is the very thing being reported. (c)
  would regress the #256/#264 cases the snake exists for (the walled Battle Brothers pocket cleared at a
  3" arc with more ranks than 3" can seat). Holding matches the documented intent - "the first snake move
  mostly STRETCHES the unit into the file" - and means no model ever moves backwards in a snake; the file
  forms over successive activations. Cost: a held model can be overlapped by an advancing one, which is
  exactly what the new same-unit validator is there to catch, so the ladder backs off instead.

- 2026-08-08: **Reversed to (a), the backward file extension.** Holding was chosen to guarantee no model
  ever moves backwards in a snake, and it does fix the reported save - but with the same-unit validator
  switched on it broke both `TacticianWalledUnitTests` corridor cases: a held model sits in the path of an
  advancing one, the snake fails validation, and the walled unit can no longer file out of its pocket.
  That is the case #256/#264 built the snake for. Forming a column out of a blob genuinely costs the rear
  models ground, so (a) is the honest geometry, and it is self-policing: the tail's cost is then visible to
  the ladder's existing `ForwardProgress` gate, which rejects a snake that nets backwards. Measured on the
  save's geometry: the honest tail drops the candidate's centroid gain from +0.30" to -0.06", i.e. below
  `MinBackoffStepInches`, so the reported unit would not have snaked at all. Recorded because the reasoning
  inverts: the property "no model moves backwards" is NOT compatible with the snake's purpose.

- 2026-08-08: `PlanMoveToward_GoalCellInsideWallInflation_StillRoundsTheWall` was re-pinned rather than
  relaxed. Its 2.5" centroid bound encoded the stacked file's geometry; a file that no longer overlaps
  itself is physically longer, so the centroid trails 2.9" instead of 2.4". Verified the behaviour that
  matters is unchanged - the lead model lands EXACTLY on the marker in both, and seizure is per model, not
  per centroid - so the test now asserts the lead model on the marker (a stronger claim than it made
  before) with the centroid bounded by the 3" seizure radius.

## Outcome

Three defects, all engine-side, landed as three slices (engine `048e72b` -> `e6690f4` -> `cafb873`).

1. **The snake stacked models.** `BuildSnakeToSide` floored every rank that overran the arc onto one
   route point. Ranks now extend the file behind the route start, and are staggered by REAL distance
   rather than arc length - the second defect, latent until a validator existed to catch it, since equal
   arc steps are a much shorter straight-line gap around a bend.
2. **Nothing forbade it.** `ValidateNoSelfOverlap` in all four `ValidatePaths` forms and in
   `ValidateConsolidationPaths`, "not worsened" so an already-stacked unit is not frozen. New
   `EErrorReasonType.EndedOnOwnUnitModel` with GUI text. The GUI already blocked a human from doing it,
   which is why it read as guarded.
3. **The AI chose it.** The snake's progress bar went from 0.05" absolute to a quarter of its arc.

The backwards movement was not a separate defect: the pile sat at the route start, which is the pre-move
centroid, so every model ahead of it had to walk back into the pile.

**Deferred / not done:** no GUI or in-game hand-verify yet - the whole item is engine-side and measured
through the suite, the bench and `analyze` on the reporting save, but nobody has watched a live game
confirm the yellow Warriors advance normally. **Recorded, not built:** a model that IS advancing may still
travel backwards along the route while funnelling onto the route line from a flank (measured -0.90" on
the save's geometry). That is the corridor behaviour #256/#264 built the snake for, and the attempt to
forbid it outright is what the reversal in Decisions is about.
