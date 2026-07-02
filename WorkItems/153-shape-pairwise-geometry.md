# 153 — Shape-owned pairwise geometry (support functions / GJK)

**Status**: todo
**Related**: #149 (base shapes), #150 (oriented geometry — where the current pairwise switches were built)

## Goal
Finish moving base-shape geometry "into the shapes". #150's cleanup (engine `b2a42a8`) made every
single-shape question polymorphic on `IBaseShape` — `BoundingRadiusInches`, `CircumscribedRadiusInches`,
`ContainsLocalPoint`, `DistanceToLocalPoint`, `ToZone(centre, facing)` — so no switch on shape type exists
outside the shape files. What remains centralized (documented, in `BaseShapeGeometry.SurfaceGap2D` and
`SweptBaseGeometry`) is the **pairwise** geometry: gap between shape A and shape B, and the swept-base
intersection tests. Pairwise math can't be expressed by one shape alone (double dispatch), so today it's a
switch over known shape pairs with a conservative bounding-circle fallback for unknown pairs.

Replace that with a **support-function seam**: each `IBaseShape` exposes a convex support point
(`Float2 SupportLocal(Float2 direction)` — the farthest point of the shape in a direction), and ONE generic
algorithm (GJK for distance/overlap; its swept extension for the path tests) computes any-shape-vs-any-shape
results with no type inspection. New shapes (ovals, egg bases, …) then only implement the support function
and automatically work everywhere: melee range, coherency, terrain sweeps, pile-in, move-through, LoS.

## Notes
- 2026-07-01: Opened from the user's architecture question during #150 hand-verification ("logic in the
  shapes themselves … more abstract and reusable"). The single-dispatch half was done immediately (#150,
  engine `b2a42a8`); this item tracks the pairwise half. The closed-form circle-circle / rect-rect /
  circle-rect cases in `SurfaceGap2D` should remain as fast paths or test oracles for the GJK results —
  they're exact and cheap.

## Decisions

## Outcome
_(open)_
