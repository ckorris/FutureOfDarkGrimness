# 275 — Formation cycling in group placement/movement (Shift+Wheel)

**Status**: implemented, awaiting GUI hand-verify
**Related**: #094 (coherency repair), #150 (base shapes), #159 (mixed-base packing), #170 (AI grid port), #214/#269 (reposition placement), #215 (consolidation group mode)

## Goal
When moving, deploying, or repositioning (Teleport/Fanatic), the only whole-unit options were "exact
current shape" (moving) or one generated default (deploying). Give Group mode a set of simple tight
formations to scroll through - line, 5x2, 4-3-3, ... - via Shift+Wheel, with "formation 0 = current
shape, unchanged" wherever the unit already stands. Consolidate the formation-layout math (previously
three independent implementations) into one engine home so the conventions live in one place.
UI shape chosen with the user 2026-07-24: stay in Group mode, Shift+Wheel cycles (no third mode);
plain Wheel keeps rotating.

## Notes

- 2026-07-24: Implemented end to end. Engine: new `Helpers/FormationLibrary.cs` (namespace `FDG`) -
  `RowPartitions` (balanced splits, no lone-model rows per #159), `LayoutOffsets` (per-model row
  layout generalizing PackGrid's), `LegalFormations` (filters shapes whose circumscribed-radii span
  breaks the 9" all-pairs rule - an illegal 10-model line simply isn't offered), `PlanFormationOffsets`
  (nearest-slot assignment + extent permutation so mixed bases keep per-model spacing),
  `Describe` ("line (10)" / "5x2" / "4-3-3", ASCII). `CohesiveFormation.PackGrid` now lays rows out
  through `LayoutOffsets` (block-center changed to centroid-center; all 7 existing tests still green).
  Engine commit `a7e9f4d`, 2105/2105 green (6 new).
- 2026-07-24: App: new `FormationCycle` (per-request catalog + index; index 0 = current shape for
  movement/consolidation/reposition, first legal partition for fresh deployment - which reproduces the
  old ComputeDeploymentOffsets default exactly) and `GroupInput` (shared Wheel/R reader, replacing
  three copy-pasted blocks; Shift+Wheel = cycle). Movement/consolidation feed the picked formation as
  the base shape into the existing two-array `PlanGroupMove` (same mechanism as the #094 coherency
  repair), so per-model budgets, terrain clamps, and red-phantom feedback all apply unchanged; index
  resets to "current" on every committed step. `GuiPlaceObjectsResolver` group mode uses the cycle for
  deployment AND reposition (Teleport now defaults to relocating in the unit's current shape with
  per-model facings preserved, rotating with the wheel). `GroupFormationUtilities.ComputeDeploymentOffsets`
  and its private row helpers deleted; the forward-row mirror (front row toward table centre) moved
  into the resolver. App 576/576 green (5 ported/new in `GroupFormationUtilitiesTests`), full build
  clean, headless smoke exit 0.
- 2026-07-24: **Shift conflict**: in the movement resolver Shift-hold was "stay within Advance" in both
  modes. In group mode Shift now belongs to the formation cycle; the advance lock there rides the
  checkbox alone (single mode keeps the Shift-hold shortcut, checkbox label says so only in single mode).

## Decisions

- **No third mode.** Group mode + Shift+Wheel (user sign-off 2026-07-24). Plain Wheel rotation and G
  toggle unchanged.
- **Formation catalog = balanced row partitions only** (line, 2-row, ... rows of pairs). No lone-model
  rows (#159); shapes breaking the 9" span are filtered at catalog build, not at click time.
- **Movement morphs ride the two-array PlanGroupMove**, not new machinery - budget measured from each
  model's real start, so a formation change can never push a model past its cap; unreachable shapes
  show as over-budget red phantoms.
- **Movement/consolidation catalogs use circumscribed radii for both axes** (rotation-safe,
  conservative for rectangles); deployment keeps true per-axis extents at the zone facing, as before.
- **Deferred, explicitly**: porting `AiPlaceObjectsResolver.BuildGrid` onto FormationLibrary is #170's
  scope (its block-search assumes uniform spacing; the port is now a straightforward follow-up since
  the shared layout exists). CLI resolvers keep their auto-pack behavior - no CLI UX change; they
  share the layout indirectly through PackGrid.

## Outcome
(open - awaiting GUI hand-verify: deploy cycle + default parity, Teleport current-shape default and
formation swap within reach rings, movement morph budgets/red phantoms, consolidation cycle, Shift
advance-lock still works in single mode.)
