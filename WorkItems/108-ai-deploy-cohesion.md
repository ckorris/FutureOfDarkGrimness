# 108 — AI deployment: coherent block packing

**Status**: in-progress
**Related**: #149 (introduced the deploy fan-out this builds on), #094 (the movement-side cohesion-repair analogue), reuses the `CohesiveFormation.PackGrid` formation idiom
**Branch** (both repos): `149-base-shapes`

## Goal
The AI must deploy a unit as a **cohesion-valid block**, never scattered. Reported bug (user, 2026-06-26): a 10-model unit deployed with ~9 models in an over-wide line and **one model stranded far away** near impassible (cross) terrain — illegal (out of cohesion). Done = the AI places every unit so that all its models satisfy BOTH cohesion rules (each within 1″ of a neighbour; every pair within 9″), wrapping into ranks when a single row would exceed the 9″ spread, and clearing terrain/zone/other units; units still fan out across the zone.

## Notes
- 2026-06-26: **Fixed — rewrote `AiPlaceObjectsResolver` (engine) to block-pack.** Replaced the greedy left-to-right single-row scan (which (a) had a cohesion-less center fallback that stranded a model, and (b) only checked the 1″ nearest-neighbour rule, not the 9″ all-pairs spread) with: pack the unit into a tight square-ish grid (`cols = ceil(√n)`, 0.1″ base-to-base — same math as `CohesiveFormation.PackGrid`); keep the #149 fan-out lane/band as the **preferred block centre**; search centres outward (preferred first, in `spacing` steps) for one where the whole block clears the zone shape, impassible terrain, already-placed models, and (Ambush) every enemy ≥ `MinDistanceFromEnemiesInches`; if none is fully clear, place the **intact block** at the clamped preferred centre — cramped but never scattered. A 10-wide rank breaks the 9″ rule, so wrapping into ranks (10 → 4×3) falls out of the square-ish grid for free. Removed the now-dead `OverlapsAny`/`InCohesion`/`FindPosition`/`FindValidPosition`/`TryPlaceRow`/`ZRowOffset`.
- Verified: engine build clean; **engine suite 839/0** incl. a new `AiPlaceObjectsResolverTests.PacksTenModelsIntoCohesiveBlock_NearImpassibleTerrain` (10 models, full-height wall splitting the zone → asserts all placed, none on terrain, every model ≤1″ from a neighbour and ≤9″ from all others); the two existing AI-deploy tests (Ambush min-distance, impassible terrain) still pass; headless smoke exit 0 with **Player 2 (AI)** deploying through the rewritten resolver and the game completing.

## Decisions
- **Square-ish grid (not wide ranks).** Minimises the block diagonal, so the 9″ all-pairs rule holds for any realistic unit size with no special-casing. 10 → 4×3 (the user's "5×2 or something" was illustrative; both are coherent).
- **Coherence beats clearance.** When the zone is too cramped to place a fully-clear block, keep the block intact (slightly overlapping) rather than scatter to find gaps — being out of cohesion is the worse failure.
- **Block validation uses the bounding radius** (`BaseRadiusInches`), consistent with the rest of the resolver; exact rectangular-base packing stays in #150.

## Outcome
_(written on close)_
