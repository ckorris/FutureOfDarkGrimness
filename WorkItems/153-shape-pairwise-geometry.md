# 153 — Shape-owned pairwise geometry (support functions / GJK)

**Status**: done
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

## Notes (cont.)
- 2026-07-02: Resolved via a **rounded-convex-hull footprint** rather than a support-function/GJK seam — the
  same goal (shape-owned pairwise geometry, no shape-pair switch, a new shape = one method) reached with less
  code and obvious correctness for our shapes. Each `IBaseShape` implements `Footprint(centre, facing)`
  returning a `BaseFootprint` (convex-hull corners + Minkowski rounding radius): a circle is one point rounded
  by its radius, a rectangle is four oriented corners with zero rounding. `BaseShapeGeometry.SurfaceGap2D` /
  `AreColliding` consume only that — one hull-vs-hull routine (SAT for overlap/penetration, nearest-feature
  for separation), zero branching on game-shape type. The only internal distinction is point-hull vs
  polygon-hull, a closed geometric property that never grows when shapes are added. Circle-vs-circle stays
  byte-identical to `dist − rA − rB` (two point hulls). Engine `2c4291c`.

## Decisions
- 2026-07-02: Chose the hull-footprint representation over GJK support-functions (the user picked
  "shape-owned footprints" and asked for "the most elegant … add more shapes without tons of new code").
  Support-functions/GJK remain a valid future swap **behind the same `SurfaceGap2D`/`AreColliding` API** if a
  shape ever needs it (e.g. a smooth curved base where a hull approximation is too coarse) — the call sites
  wouldn't change. Not built now (no such shape exists; the hull is exact for circles/polygons).

## Outcome
Done (engine `2c4291c`). Pairwise base geometry is shape-owned via `BaseFootprint`; there is no shape-pair
switch anywhere, and the whole engine + app route model-to-model overlap and distance through the one seam.
See also #150 for the call-site migration.
