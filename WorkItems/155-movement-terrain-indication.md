# 155 — Movement GUI: flag difficult/dangerous terrain a move will cross

**Status**: in-progress (shipped + committed; pending user visual confirmation of the overlay)
**Related**: #093 (per-model budgets), #150 (base shapes / swept geometry), engine #153 counts-as-in-terrain grant (not exposed on request — see Notes)

## Goal
When defining a move in the GUI (single-model ghost or group ghost) or previewing committed waypoints, visually indicate when a model's path crosses **Difficult** terrain (total move capped at 6" unless Strider/Flying) or **Dangerous** terrain (each crossing model rolls a d6 on commit, 1 wound on a 1) — so the consequence is visible *before* the player commits, not just after the wound lands. App-side only (`GuiDefineMovementResolver`); the engine geometry and validation already exist.

## Notes

- 2026-07-03: Shipped. Two commits:
  - Engine `6338deb`: `SweptBaseGeometry.MaxTravelBeforeZoneIntersection` (entry-distance bisection over the existing boolean swept check), public `MovementUtilities.DoesPathCrossDifficultTerrain` (shared core with the dangerous check), `MovementUtilities.ClampTravelForDifficultTerrain` (the clamp), `DIFFICULT_TERRAIN_CLAMP_MARGIN_INCHES`. New tests: 4 in `SweptBaseGeometryTests`, `DifficultTerrainClampTests` (10) incl. an end-to-end test that a clamped move passes the authoritative `ValidatePaths`. 1107 engine tests green.
  - Superproject `799384c`: `GuiDefineMovementResolver` single + group ghost clamp, cap-aware range rings + panel max, dangerous `!` badge + panel line, AutoAdvance step clamp; `GroupFormationUtilities.LargestFeasibleScale` (bisection to pull the group translation back past what the closed-form budget solve can express). Full `dotnet build` green; headless smoke exit 0.
  - Group-mode terrain enforcement: after the budget-based `PlanGroupMove`, if any phantom step busts the difficult clamp, the applied translation is bisected down with the engine clamp as the feasibility predicate; if even scale 0 fails the step is `terrainBlocked` (phantoms red, click is a no-op) - same treatment as an over-budget rotation.
  - **Verification caveat (deferred to user):** the change's only runtime surface is the interactive movement overlay (ImGui, reads live pointer position). The only display is the user's live desktop `:0` with no Xvfb, so it couldn't be auto-driven without hijacking their real input. Automated evidence is green (build, headless smoke, engine suite incl. the validator-agreement end-to-end test) but the on-screen visuals (badge placement, clamp feel, panel text) need a hands-on look. Manual repro: host+launch a default game, move a unit on the left half toward the forest at table (20,24) r5 (difficult) or the minefield rect x8-14/z30-36 (dangerous).

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
