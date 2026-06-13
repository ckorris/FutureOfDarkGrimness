# 050 — Movement validation ignores model base radius for terrain footprints

## Goal

`MovementUtilities` terrain validators test a **zero-width** center-to-center line against
terrain footprints, so a model can park with its *center* just outside an impassable shape
while its *base* overlaps it. Inflate the terrain footprint by the model's `BaseRadiusInches`
(Minkowski expansion / swept-disc) so base overlap is caught. Applies to the impassible,
difficult, and dangerous variants. Resolver layer needs no changes.

(Reassigned from 046, whose number was reused for the line-of-sight cluster.)

## Decisions

- **Swept-disc as an `IZone` overload, not ad-hoc math in `MovementUtilities`.** Added
  `DoesPathIntersectZone(Float2 start, Float2 end, float inflationRadius)` to `IZone`, implemented
  per shape. `MovementUtilities` only sees the `IZone`/`ITerrain` interface, so shape-specific
  distance math can't live there cleanly. The overload is also reusable for #048 (block deployment
  into impassible terrain). Zero-arg overload preserved for LoS / cover callers that genuinely want
  the zero-width line.
- **Rotated wrapper passes the radius through unchanged** — rotation about a pivot is rigid, so it
  preserves the segment↔footprint distance; only the endpoints are inverse-rotated into the local frame.
- Shared `SegmentGeometry` helper (point→segment and segment→segment squared distance, plus a robust
  segment-intersection test) backs the circle and rectangle implementations.

## Notes

### 2026-06-13
- Branched `050-movement-base-radius` on both repos off synced master (`af3f5bd` engine / `90ac830` app).
- Implemented the swept-disc overload across `CircularZone`, `RectangularZone`, `CompositeZone`,
  `RotatedZoneWrapper`, `TerrainData`; wired the three `MovementUtilities` validators to pass
  `move.Model.BaseRadiusInches`.
</content>
</invoke>
