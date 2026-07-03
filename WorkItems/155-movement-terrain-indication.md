# 155 — Movement GUI: flag difficult/dangerous terrain a move will cross

**Status**: in-progress
**Related**: #093 (per-model budgets), #150 (base shapes / swept geometry), engine #153 counts-as-in-terrain grant (not exposed on request — see Notes)

## Goal
When defining a move in the GUI (single-model ghost or group ghost) or previewing committed waypoints, visually indicate when a model's path crosses **Difficult** terrain (total move capped at 6" unless Strider/Flying) or **Dangerous** terrain (each crossing model rolls a d6 on commit, 1 wound on a 1) — so the consequence is visible *before* the player commits, not just after the wound lands. App-side only (`GuiDefineMovementResolver`); the engine geometry and validation already exist.

## Notes

- 2026-07-03: Work started. Engine survey:
  - `MovementUtilities.DoesPathCrossDangerousTerrain(ModelMoveEntry, IEnumerable<ITerrain>)` is public — swept-base check per segment, start = model's live position, against `ETerrainType.Dangerous` pieces. Reusable directly if we map `IModel` -> `DataBinding<ModelData>` (via `request.UnitDataBinding.GetValue().ModelBindings`).
  - Difficult check (`ValidateMovingThroughDifficultTerrain`) is private, but `SweptBaseGeometry.DoesSweptBaseIntersectZone(zone, segStart, segEnd, baseShape, facing)` is public — an app-side per-segment loop against `ETerrainType.Difficult` pieces mirrors it exactly. Cap constant: `GameWideConstants.DIFFICULT_TERRAIN_MOVE_CAP_INCHES` = 6, per-model total distance.
  - Flag mapping confirmed at `DefinePathStage` (request construction): `request.IgnoresDifficultTerrain` = Strider/Flying waiver of the 6" cap; `request.IgnoresImpassibleTerrain` = `IgnoresAllTerrain` query (Flying), which is the SAME flag `ApplyNonMovementTerrainEffectsStage` uses to waive Dangerous rolls entirely. So overlay suppression: difficult warnings off when `IgnoresDifficultTerrain`, dangerous warnings off when `IgnoresImpassibleTerrain`.
  - **Not representable**: the engine-#153 "counts as being in Dangerous Terrain" one-shot grant (spell-applied; every moving model tests regardless of path) is read via `MovementRuleQueries.CountsAsInTerrain` engine-side and is NOT on `DefineMovementPathRequest`. The overlay cannot show it without an engine change (submodule). Deferred — recorded here, not silently cut.
  - Consolidation moves (`GuiConsolidationMoveResolver`) are out of scope per the index entry (movement resolver overlay); noted as possible follow-up if dangerous rolls apply there too.

## Decisions

- 2026-07-03 (user sign-off): **Dangerous and Difficult get different treatments.**
  - **Dangerous** = advisory: warning badge beside each crossing model's ghost + live info-panel line ("N models cross - d6 on commit, wound on a 1"). No clamping — the roll is a gamble the player may take deliberately.
  - **Difficult** = enforced in the preview: the ghost/paths clamp so a move can never be drawn that the difficult cap would invalidate. Concretely: if entering difficult terrain would happen when the model's cumulative move is already at/past 6" (or the model's committed path total is already past 6"), the path stops just short of the terrain edge (tiny margin); if the entry happens before 6" cumulative, entering is allowed but 6" total becomes the model's hard cap (again minus margin), as though it were the model's max move.
  - **Utilities live engine-side** (user explicitly authorized submodule changes): entry-distance-along-segment and the difficult-cap clamp go in the engine (near `SweptBaseGeometry` / `MovementUtilities`), with engine tests; the GUI resolver only calls them. Submodule-first commit cadence applies.

## Outcome
