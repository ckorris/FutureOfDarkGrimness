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
  `move.Model.BaseRadiusInches`. Submodule `57e667b`, superproject bump `896303d`. Suite 421/0.
- **AI resolver follow-up (same day).** `AiDefineMovementResolver` had the matching blind spot: its
  terrain pre-check used the zero-width overload on the *unit-centroid* path only, never called
  `ValidatePaths`, and `DefinePathStage` *throws* (no retry) on an invalid move — so #050 made the
  validator stricter than the AI's own pre-filter, a latent crash near terrain in GUI games (headless
  default games place no terrain — `MapSetupStage` is stubbed — so the smoke can't exercise it). The
  human GUI resolver was already safe (its Done-gating calls the same `ValidatePaths`). Fixed with
  **both**: (A) base-radius-inflated centroid pre-filter, and (B) validate the actual candidate against
  `MovementUtilities.ValidatePaths` and back the step off (halving, ≤6×) until it passes, standing still
  as a last resort. New `AiDefineMovementResolverTests` (clip-would-be-invalid → valid; clear-lane →
  still advances). Suite 423/0.
</content>
</invoke>
