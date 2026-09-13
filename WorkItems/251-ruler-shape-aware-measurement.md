# 251 — Ruler overlay measures rectangular bases as circles

**Status**: implemented, awaiting GUI hand-verify
**Related**: #149 (base shapes), #150 (base-shape geometry everywhere), #250 (per-model visuals follow the base shape), #225 (base data audit)

## Goal

Spun out of #250's sweep as an explicitly deferred item. The Ctrl-drag ruler (`MeasurementOverlay`)
used scalar `IModel.BaseRadiusInches` for both of its jobs, so it measured every model as a circle:

- **Edge reading** — `edge = centreDistance - radiusA - radiusB`.
- **Snapping** — `cursorDistance <= BaseRadiusInches + margin`.

`BaseRadiusInches` is a rectangle's **inscribed** radius (half its lesser side, per #149), so both were
wrong for any elongated base, and wrong by an amount that varied with orientation.

## Notes

- 2026-07-19: **Implemented.** App-side only - the engine already had every primitive needed
  (`DistanceUtilities` facing-aware overloads, `BaseShapeGeometry.SurfaceDistanceToPoint2D`); nothing in
  the submodule changed.
  - New `FdgRaylib/Rendering/MeasurementGeometry.cs` - the display-independent core, split out so it is
    testable without ImGui or an `ITableState` (mirrors how `TacticalOverlayGeometryTests` tests the
    overlay's geometry rather than its rendering).
    - `EdgeDistanceInches(shapeA?, posA, facingA, shapeB?, posB, facingB)` -> `float?`. Both ends on a
      model uses the exact facing-aware footprint gap (the same call the engine uses for charge/range
      legality); one end free uses shape-to-point surface distance; neither returns null.
    - `SnapDistanceInches(...)` - surface distance, 0 inside the base.
  - `MeasurementOverlay.MeasurePoint` now carries the snapped `IModel?` instead of a scalar radius.
    The redundant `Snapped` bool was removed - `Model != null` is the single source of truth, and a
    second flag could drift from it.
  - **Behaviour improvement kept deliberately**: the edge reading now also shows when only ONE end is
    snapped (previously it required both). Same code path, strictly more information.

  Magnitude of the defect, pinned in a test: two 60x35mm bike bases nose-to-nose measured from their
  inscribed 17.5mm radius rather than their 30mm half-length, reading **~0.98" too far** - most of an
  inch, against a 12" charge threshold. Side-by-side the same bases measured correctly, which is why the
  bug looked intermittent.

  Snapping was under-inclusive for the same reason: the long ends of a bike or tank base were outside the
  inscribed-radius circle and simply would not snap.

  Verification: app suite **393 passed / 0 failed** (10 new), engine suite **1739 passed / 0 failed**,
  `dotnet build` clean, headless smoke exits 0.

- 2026-07-19: **Found master red while verifying** - `ArmyForgeScreenTests.UpgradeChoices_ReplaceAll_ThenCompile_Gunners`
  was failing on clean `origin/master`, unrelated to this item. It pinned #218's OLD per-model pricing
  (`120 + 15x3 = 165`); #218 made "all" replaces a flat per-unit price, so the correct figure is
  `120 + 15 = 135`. Updated the expectation and its comment. Confirmed pre-existing by stashing this
  item's work and re-running. Flagged to the owner of #218 - the engine suite was green, so an app-side
  test went unnoticed.

## Decisions

- 2026-07-19: Geometry extracted to its own class rather than tested through `MeasurementOverlay`, whose
  entry points need ImGui state and a live `ITableState`. Keeps the overlay to input + drawing.
- 2026-07-19: Kept the ruler LINE and its endpoint dots drawn centre-to-centre. Only the numeric readings
  and the snap test changed - drawing an edge-to-edge line would be a separate UX decision.

## Deferred / out of scope

- Layout-only offsets elsewhere that still use the scalar radius to place pips/labels above a model
  (`TacticalOverlayController.cs:1433,1558`, `TableTooltipOverlay.cs:296`) - slightly misplaced over a
  wide rectangle, but they position UI, they do not measure. Left alone (also noted in #250).

## Outcome

Implemented and green. Open until hand-verified in the GUI.

**Verify by hand:**
1. Ctrl-drag between two bike/tank models nose-to-nose - the edge reading is visibly shorter than before
   and matches what a charge actually reaches.
2. Rotate the situation so the same two models are side-by-side - the reading changes with orientation.
3. Ctrl-hover over the LONG end of a bike base - it snaps (it previously would not).
4. Ctrl-drag from a model to empty table - an edge reading now appears alongside the centre reading.
5. Overlapping bases read 0.0" edge, never a negative.
