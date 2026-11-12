# 250 — Per-model visuals must follow the base shape

**Status**: implemented, awaiting GUI hand-verify
**Related**: #149 (base shapes), #150 (base-shape geometry everywhere), #161 (resolver UI consistency), #225 (base data audit)

## Goal

User spotted that selecting a rectangular-based model still draws a CIRCULAR hover/selection effect.
Scope: sweep the whole front end for per-model visuals that ignore `IModel.BaseShape`, and route them
through the shared shape-aware `ModelBaseRenderer`.

Two distinct causes, both producing the same wrong picture:

1. **Raw circle draws** — callers bypassing `ModelBaseRenderer` entirely, calling
   `Raylib.DrawCircle*` / `ImDrawList.AddCircle` with `model.BaseRadiusInches`.
2. **Dropped facing** — callers that DO use `ModelBaseRenderer` but omit the optional `facing`
   argument. `Forward()` then defaults to `(0,1)`, so a rotated rectangular base renders
   axis-aligned and mismatches the model drawn beneath it by `RaylibRenderer.DrawModels`.

## Notes

- 2026-07-19: Filed from user report during #225. Swept all of `FdgRaylib/`.

- 2026-07-19: **Implemented.** `ModelBaseRenderer` gained the missing canvas primitive - there was no
  Raylib outline-only/inflate overload at all (`DrawOutlineImGui` was ImGui-only, `DrawFilledRaylib`
  had no inflate), which is why the most visible offender had no shape-aware call to make:
  - NEW `DrawOutlineRaylib(shape, cx, cy, scale, outline, thickness, inflateInches, facing)`.
  - `DrawFilledRaylib` gained an optional `inflateInches` (defaults 0, back-compatible).

  Cause-1 fixes (raw circles -> shape-aware), ranked by how visible they are in play:
  - `RaylibRenderer.cs:811-818` — **active-unit spotlight halo**. The pulsing halo under every model of
    the activating unit; drawn every frame, so the most visible offender and almost certainly what the
    user saw. Note `DrawModels` twenty lines below was already correct - the two contradicted each other.
  - `Resolvers/GuiChooseRangedAttackResolver.cs:493` — **shooting target rings**. Same method already
    used `BaseShape` + `Facing` for the base-to-base distance label, so the ring visually disagreed with
    the number printed next to it.
  - `TacticalOverlay/TacticalOverlayController.cs:1489` — ghost threat-tint emphasis ring.
  - `Resolvers/GuiCastAssistResolver.cs:72` — cast-assist model highlight.

  Cause-2 fixes (dropped `facing`):
  - `Resolvers/GuiDefineMovementResolver.cs:259` — movement start-position outline (highly visible).
  - `Resolvers/GuiConsolidationMoveResolver.cs:111` — consolidation start outline.
  - `Resolvers/GuiConsolidationMoveResolver.cs:126` — final committed ghost fill.
  - `Resolvers/GuiConsolidationMoveResolver.cs:187` — mouse-following ghost. It already computed
    `_selectedModel.Facing` at line 182 for the overlap test without passing it to the draw.

  Verification: `dotnet build` clean (0 errors), engine suite 1739 passed / 0 failed, headless smoke
  exits 0. Post-fix grep confirms zero remaining `AddCircle`/`DrawCircle` calls keyed off
  `BaseRadiusInches`.

## Decisions

- 2026-07-19: Filed as its own item rather than folded into #225 (a DATA audit) or #150 (already in
  awaiting-verification). Same defect family as #150 but a distinct, separately verifiable surface.
- 2026-07-19: The inflated rectangle outline keeps **sharp corners** rather than rounding, matching the
  existing `DrawOutlineImGui` behaviour. Consistency over prettiness; revisit if it reads badly by hand.

## Deferred / explicitly out of scope

- **Hero star** (`RaylibRenderer.cs:859`) sized by `BaseRadiusInches`. It overlays the base rather than
  tracing it, so it is not a shape defect - but on a rectangle the inscribed radius can let it overhang
  the short axis. Sizing nit, left alone.
- **`MeasurementOverlay.cs:168,179`** snaps and measures using the scalar radius, so measuring to a
  rectangular base is approximated by its circle. That is a measurement-ACCURACY gap, not a drawing one
  - the engine already has shape-aware `DistanceUtilities.GetBaseToBaseDistanceInches_3D`. **Worth its
  own item; not fixed here.**
- Layout-only offsets that use the scalar radius to place pips/labels above a model
  (`TacticalOverlayController.cs:1433,1558`, `TableTooltipOverlay.cs:296`) - slightly misplaced over a
  wide rectangle, but not circle-vs-rectangle bugs.
- Genuinely circular visuals left untouched: objective markers + 3" seizure rings, weapon range /
  charge rings, ambush exclusion blobs, unit-cohesion bounding circles, threat-field rasterization,
  token chips, dice pips, HUD pips, and all attack/impact effects.

## Outcome

All 8 defect sites fixed and building green. Open until hand-verified in the GUI with a
rectangular-based army (bikes, tanks, titans) - see the verify list below.

**Verify by hand:**
1. Activate a bike/tank unit — the pulsing spotlight halo is a rotated rectangle, not a circle.
2. Shoot at a rectangular-based unit — target rings are oriented rectangles matching the bases.
3. Move a bike unit — start outlines and ghosts stay rotated to each model's facing.
4. Consolidate with a rectangular-based unit — start outline, dragged ghost and committed ghost all
   keep facing.
5. Cast-assist with a rectangular-based assister — highlight follows the base.
