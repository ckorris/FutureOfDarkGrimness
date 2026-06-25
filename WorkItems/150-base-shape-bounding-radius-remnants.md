# 150 — Base-shape geometry: replace remaining bounding-radius approximations

**Status**: open
**Related**: #149 (configurable base shapes — this is its deferred half), #050 (movement base-radius / swept-disc), #002 (zone shapes / SAT)

## Goal
#149 makes **model-to-model** measurement shape-aware but, by decision, leaves the harder geometry approximating a non-circular base by its **bounding circle** (circumscribing radius). This item replaces those approximations with true shape geometry so a rectangular (or future) base collides/blocks/seizes with its real footprint, not a circle around it.

The bounding-circle remnants to address (catalogued during #149 — exact list finalized when #149 slice C lands):
- **Swept-path vs terrain** — `MovementUtilities.ValidateMovingThroughImpassibleTerrain` / `...DifficultTerrain` / `DoesPathCrossDangerousTerrain` and `PlacementUtilities.OverlapsImpassibleTerrain` inflate zones by the model's `BaseRadiusInches`. True geometry = Minkowski sum of the base shape with the zone along the path. Likely needs **base facing/rotation** to be meaningful for a rectangle.
- **Pile-in swept collision** — `PileInUtilities` (`MaxStepToTouch`, `LimitStepByObstructions`) uses combined radii for swept-disc model collision.
- **Move-through-enemy** — `MovementUtilities.ValidateMovingThroughEnemyUnits` / enemy footprints use radii.
- **Line of sight** — `LineOfSightUtilities.BuildModelBlockers` wraps each model in a `CircularZone(radius)`; a rectangular blocker should be a `RectangularZone`.
- **Objective seizure** — `ReconcileObjectivesStage` uses center-distance − `BaseRadiusInches` (model-to-objective base edge).
- **Base facing/rotation** — prerequisite for most of the above to be exact for rectangles; models have no facing today.

## Notes
- 2026-06-25: Opened from #149 at the user's request, to track the collision paths #149 intentionally leaves on the bounding-circle approximation. Finalize the precise file/line list when #149 slice C is done.

## Outcome
_(open)_
