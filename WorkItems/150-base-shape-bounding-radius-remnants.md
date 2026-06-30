# 150 — Base-shape geometry: replace remaining bounding-radius approximations

**Status**: in-progress
**Related**: #149 (configurable base shapes — this is its deferred half), #050 (movement base-radius / swept-disc), #002 (zone shapes / SAT — `RotatedZoneWrapper` reusable for oriented rect geometry), #029 (Aircraft heading — refactored onto per-model facing here)
**Branch** (both repos): `150-base-shape-geometry`

## Goal
#149 makes **model-to-model** measurement shape-aware but, by decision, leaves the harder geometry approximating a non-circular base by its **bounding circle** (circumscribing radius). This item replaces those approximations with true shape geometry so a rectangular (or future) base collides/blocks/seizes with its real footprint, not a circle around it.

The bounding-circle remnants to address (catalogued during #149 — exact list finalized when #149 slice C lands):
- **Swept-path vs terrain** — `MovementUtilities.ValidateMovingThroughImpassibleTerrain` / `...DifficultTerrain` / `DoesPathCrossDangerousTerrain` and `PlacementUtilities.OverlapsImpassibleTerrain` inflate zones by the model's `BaseRadiusInches`. True geometry = Minkowski sum of the base shape with the zone along the path. Likely needs **base facing/rotation** to be meaningful for a rectangle.
- **Pile-in swept collision** — `PileInUtilities` (`MaxStepToTouch`, `LimitStepByObstructions`) uses combined radii for swept-disc model collision.
- **Move-through-enemy** — `MovementUtilities.ValidateMovingThroughEnemyUnits` / enemy footprints use radii.
- **Line of sight** — `LineOfSightUtilities.BuildModelBlockers` wraps each model in a `CircularZone(radius)`; a rectangular blocker should be a `RectangularZone`.
- **Objective seizure** — `ReconcileObjectivesStage` uses center-distance − `BaseRadiusInches` (model-to-objective base edge).
- **Base facing/rotation** — prerequisite for most of the above to be exact for rectangles; models have no facing today.

## Decisions
- **2026-06-30 — facing now, not deferred (with the user).** #150 will do oriented geometry, not just axis-aligned-exact. Prerequisite: a real per-model facing. The user clarified the representation: every model gets a **yaw-only rotation expressed as a 2D unit normal**; Aircraft must derive their heading from it (retiring the unit-level `AircraftHeading`).
- **2026-06-30 — facing is a store-backed `DataBinding<Float2>`** (mirrors `Position`): observable for rendering, network-replicated (a model turning syncs host→clients), and round-tripped by save/load. Default `(0,1)` = +Z, which reproduces the pre-#150 axis-aligned `RectangleBase` layout (width→X, height→Z), so existing armies/circles are unchanged. Cost: one appended `RegisterType<Float2>()` (type-map positional, so last). `IModel.Facing`/`SetFacing`/`OnFacingChanged`.
- **2026-06-30 — Aircraft heading lives on the models, gated by a token.** `UnitData.AircraftHeading` (nullable) is retired. `ForcedAircraftMove.EnsureHeading` aims toward table-centre on first move, writes it to every living model's `Facing`, and adds an `AircraftHeadingSet` token; once aimed it reads the heading back (asserting the models share one — an Aircraft never turns). Off-table clears the token to re-aim on redeploy. The token (not a nullable value) carries the "already aimed" tri-state, avoiding a default-vs-deliberate `(0,1)` ambiguity. Behaviorally identical to the old field.
- **2026-06-30 — no `AircraftHeading`-style unit fields going forward** (user: "I definitely don't like AircraftHeading"). Orientation is per-model; unit-level direction is a derived query over the models.

## Notes
- **2026-06-30 — Slice 1a (per-model Facing field) DONE.** Engine `6dae70c`. `IModel.Facing`/`SetFacing`/`OnFacingChanged`; `ModelData.FacingBinding` (`DataBinding<Float2>`, default +Z) threaded through all constructors; `Float2` registered in the store (appended last). Round-trip tests across 6 files thread the new binding through (each model now round-trips wounds+position+**facing**); new `BaseShapeTests.Facing_DefaultsToForward_UpdatesAndSurvivesRoundTrip`. Suite 948/0, build clean.
- **2026-06-30 — Slice 1b (Aircraft onto per-model facing) DONE.** Engine `780c715`. Retired `UnitData.AircraftHeading`; `ForcedAircraftMove.EnsureHeading` reads/writes model `Facing` gated by the new `AircraftHeadingSet` token; `DefinePathStage` off-table clears the token; `CoreRuleCatalog` doc updated; `ForcedAircraftMoveTests` rewritten to the token+facing model. Suite 948/0, build clean, headless exit 0 (full game).
- **Remaining:** Slice 1c (renderer rotates the drawn base by `Facing` — app-side; today a no-op since only Aircraft set non-default facing) → then the geometry slices, all now **orientation-aware**: 2) objective seizure (shape-to-point) → 3) terrain swept-paths (oriented swept-Minkowski) → 4) move-through-enemy + pile-in (oriented swept shape-vs-shape; upgrade #149's `SurfaceGap2D` rect cases) → 5) LoS blockers (oriented rect via `RotatedZoneWrapper`).
- 2026-06-25: Opened from #149 at the user's request, to track the collision paths #149 intentionally leaves on the bounding-circle approximation. Finalize the precise file/line list when #149 slice C is done.

## Outcome
_(open — slices 1a/1b of the facing foundation landed 2026-06-30; geometry slices pending.)_
