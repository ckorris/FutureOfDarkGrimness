# 170 — Mixed-base AI deploy strands small models out of cohesion

**Status**: done
**Related**: #159 (the movement-side PackGrid fix this ports), #277 (FormationLibrary.LayoutOffsets,
the shared row-layout primitive), #150 (per-axis extents at deploy facing)

## Goal
`AiPlaceObjectsResolver` laid out every deploy block on a uniform grid spaced by the LARGEST base
in the unit, so a mixed-base unit (a joined hero's big base among small troopers) deployed with two
adjacent small models far over the 1" nearest-neighbour rule - out of cohesion on turn zero. Port
`CohesiveFormation.PackGrid`'s per-row sizing (the #159 fix) into the deploy path and add the
missing mixed-base deploy test.

## Notes

- 2026-08-05 (implemented, tested): `BlockOffsets` computes the block's per-model offsets once per
  Resolve. **Mixed-base units** (any footprint extent differing >0.005" from the first model's) go
  through `FormationLibrary.LayoutOffsets` with PackGrid's exact conventions: edge-to-edge 0.1" at
  each model's OWN extent within a row, rows stacked by their tallest member, cols bumped when the
  last row would hold a single (neighbourless) model. **Uniform units keep the historical row-major
  max-extent grid, byte-identical** - deliberately gated so the solo bot's deployments do not move
  (confirmed: solo D1 builtin mirror `4B73F1B9DBBC8102` and builtin-vs-basic `E86503B238B27EA1`
  both bit-identical to same-session pre-change controls). The sweep/penalty machinery
  (`FindBlockCenter` / `FindEdgeBlockCenter` / `BlockPenalty`) now consumes the offsets list instead
  of re-deriving a uniform grid per centre; sweep steps and zone-containment margins still use the
  conservative max-extent spacing / circumscribing radius. Dead `BlockIsValid` removed.
  - Test: `AiPlaceObjectsResolverTests.MixedBaseUnit_DeploysInCohesion_WithoutOverlaps` (1x r=1.81
    leader + 6x r=0.55 troopers; every model must have a neighbour within 1", no overlaps, all
    in-zone). Red-by-design verified: pre-fix the leader sits 2.62"+ from every trooper.
  - Engine suite 2868/2868; full build; headless smoke exit 0.

## Decisions
- **The new layout is gated to actually-mixed units.** LayoutOffsets centres partial rows and
  PackGrid bumps column counts, so applying it to uniform units would shift every solo deployment
  for some model counts (7, 13, ...) - a behaviour change with zero player-visible benefit that
  would also invalidate every recorded solo D1 baseline. Uniform units reproduce the old grid to
  the byte; only the broken case changes.
- **Zone margins stay conservative (largest circumscribing radius for every cell).** Per-model radii
  would let mixed blocks fit tighter to zone edges, but the uniform bound is what the existing
  clamps/penalty use and is always safe; not worth widening the diff.

## Outcome
Shipped with the port + mixed-base deploy pin (engine commit; see index). Deployments of hero-joined
(mixed-base) units in AI/auto play now start in cohesion; uniform units are byte-unchanged. Note for
benchmark readers: pool-army outcomes involving joined heroes can legitimately shift from this fix -
it is a rules-correctness change, verified by pin + suite + solo-D1 stability per the #159 precedent,
not a policy change needing a win-rate gate.
