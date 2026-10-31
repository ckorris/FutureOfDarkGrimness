# 046 — GetFirstBlockingHit engine API

**Status**: implementation complete, awaiting user test
**Related**: #041 (the consumer)

## Goal
`LineOfSightUtilities` only exposes binary / categorical results: "is sight Clear/Cover/Blocking?". For UI overlays we need to know *where* on the (attacker, target) segment the first Blocking piece interrupts the line so we can draw a stub and a marker at the block point. Done when the engine offers a `GetFirstBlockingHit(attacker, target, terrain) -> (Position hit, ITerrain piece)?` helper that returns the closest blocker entry along the segment, with tests covering circular, rectangular, multi-blocker, cover-ignored, and attacker-inside-blocker cases.

## Notes
- 2026-05-26: Implemented.
  - Added `IZone.GetFirstSegmentEntry(Float2 start, Float2 end) -> Float2?` and implemented on `CircularZone` (quadratic solve for the near root) and `RectangularZone` (segment-segment intersection vs the four edges, min t). `TerrainData` delegates to its `Shape`; the test-local `DoorTerrain` likewise.
  - Added `LineOfSightUtilities.GetFirstBlockingHit` which walks the terrain list, filters to pieces whose `EvaluateSightLine` returns `Blocking`, and picks the entry with the smallest squared distance from the attacker.
  - 6 new tests in `LineOfSightTests` (no terrain → null, rect/circle entry points, two-blocker closest wins, cover ignored, attacker-inside-blocker returns start). Full suite 135/135 green (was 129/135).

## Decisions
- **Engine-side helper, not client-side geometry**: per user direction (memory: prefer engine-side when better, with oversight). Keeps the math in one place and lets headless tests cover it.
- **`Cover` pieces are not returned**: only `Blocking` pieces interrupt the line; cover doesn't, even though it tints the sight result. Consumers that want a "where does cover start" indicator would need a separate helper.
- **Attacker-inside-blocker returns the attacker position**: an edge case in practice (the attacker would have to be physically inside a wall), but returning `null` would mean "nothing blocks" which contradicts the geometry. Returning the attacker position lets the overlay draw the X at the attacker's feet without further special-casing.
- **`CircularZone` returns only the near root**: because the helper assumes `start` is outside the circle (the in-zone case is handled before the quadratic), `t1` is always the entry. Saves one branch and one sqrt-extras.

## Outcome
(pending — implementation complete, awaiting user test before close-out)
