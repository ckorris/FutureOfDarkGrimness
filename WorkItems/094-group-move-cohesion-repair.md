# 094 — Group-move coherency repair

**Status**: in-progress
**Related**: #011 (move-through-enemy / standoff), engine coherency-bug memory (resolver owns the cohesion check), `GuiDefineMovementResolver` / `GroupFormationUtilities`

## Goal
In the GUI movement resolver's **Group mode**, a unit that starts an activation out of coherency (e.g. a
model died mid-unit, or a pile-in/consolidation scattered it) can never produce a legal group move today:
the group transform is rigid (rotate + translate about the centroid), so the broken shape is preserved and
the Done button stays disabled until the player drops to Single mode and fixes it model-by-model.

"Done" = when the unit is out of coherency, the group ghost first re-forms into a **legal** coherent shape
(contracted toward the centroid by the least amount needed), so a single click moves the unit into
coherency plus whatever the player dragged/rotated. Crucially, the per-model distance caps (Advance / Rush
/ Charge, and the shoot-after-move Advance cap under Shift) must be enforced on each model's **total travel
from its real starting position** — the cohesion correction plus the rigid move — so dragging "as far as
allowed" can never push any single model past its limit.

## Decisions
- **Repair algorithm = iterative relaxation** (final, 2026-06-21). Decision evolved across the build:
  1. *Uniform contraction toward centroid* (first user pick) — rejected in implementation: a near-touching
     body can't be shrunk without overlap, so a far straggler can never be pulled in (the casualty case).
  2. *Single-model pull toward the rest's centre* — worked but the user flagged the real problem: the group
     step is bottlenecked by whichever model spent the most budget repairing, so loading it all on the
     straggler caps group drag.
  3. *Iterative relaxation* (chosen) — each over-long link pulls both ends together; ripples down the chain
     so displacement is **shared and graded** and the **max single move is minimised**, which is what frees
     up group-drag distance. Damped simultaneous updates; separation sweep prevents overlap; centroid is
     conserved. Only runs when the unit is actually out of coherency (gated on the resolver's `CheckCohesion`),
     so coherent units keep today's exact rigid behavior.
- **Budget measured from the real start, not the repaired shape.** `PlanGroupMove` gains an
  `originPositions` parameter: the rigid transform still operates on the (possibly repaired) base shape,
  but each model's budget constraint is `|finalPos − originPos| ≤ budget`. The existing 7-arg overload
  (origin == base) preserves the coherent-case behavior and all current tests.
- **App-side only.** The engine cohesion check is unreachable/broken (see memory), so the resolver already
  owns coherency; no engine change.

## Deferred (recorded — not silently cut)
- **Coherency unreachable within the move allowance**: if the contraction alone exceeds a model's remaining
  budget, the group step is flagged invalid (red ghosts, no commit) rather than committing a partial
  "as close as possible" move — the player falls back to Single mode. Rare (stragglers from a casualty are
  usually a couple inches out, far inside a full move). Could later contract only as far as budget allows.
- Other repair algorithms (minimal straggler-pull, re-lay) not implemented.

## Notes
- 2026-06-21: **Repair algorithm finalised = iterative relaxation (burden-sharing).** The single-model pull
  (below) was rejected by the user: dumping the whole correction on the straggler eats its budget, and since
  the group step is bottlenecked by the model that moved most, that needlessly caps the group's drag
  distance. New `RepairCoherencyByContraction` relaxes: each iteration, every over-long link (1" nearest per
  model + 9" on the farthest pair) pulls **both** endpoints toward each other by half the excess; the
  correction ripples down the chain so the displacement is **shared and graded** (nearest body model moves
  most, falling off with distance) and the **max single move is minimised** — which is exactly what
  preserves group-drag distance. Damped (0.5) simultaneous updates, ≤600 iters, exits the instant it's
  coherent (so an already-coherent unit is returned untouched). A separation sweep keeps bases from stacking.
  Equal-and-opposite pulls + separation **conserve the centroid** (pinned by a test). Tests updated: the old
  "body stays put" assertion was inverted into a graded-sharing + centroid-conservation test; suite 25/0,
  build clean, headless exit 0. Budget/`PlanGroupMove` plumbing unchanged (still origin-anchored).
- 2026-06-21: **Implemented (app-side; awaiting GUI hand-verification).** [superseded same day by relaxation]
  - `GroupFormationUtilities.RepairCoherencyByContraction(positions, radii, maxNearest, maxFarthest)` — pulls
    each out-of-cohesion model toward the **rest of the unit's centre** (centroid-excluding-self) the least
    amount that rejoins it (stops the instant it reaches a neighbour → ~0.98" b2b, never overlaps). Pass 1
    fixes the 1" nearest rule (repeated until stable); pass 2 is a best-effort net for a residual 9"
    over-spread. In-cohesion models stay put.
  - **Pivoted away from uniform contraction mid-build:** the first cut scaled the whole formation toward the
    centroid, but a near-touching body blocks any contraction (can't shrink without overlap), so a far
    straggler could never be pulled in — exactly the casualty case. Per-model pull fixes this and still
    realizes "toward the centre". Also learned the **global** centroid is dragged off the body by the
    straggler itself (models at 0,1,12 → centroid 4.33, still 2.33" from the body), so the pull must target
    the centroid of the *other* models.
  - `PlanGroupMove` gained an 8-arg overload `(basePositions, originPositions, …)`: the rigid transform runs
    on the repaired base shape while the per-model budget is measured from each model's real start, so total
    travel (repair nudge + rigid move) honours the cap. 7-arg overload (origin == base) preserves the
    coherent-case behavior + all existing tests.
  - `DrawGroupGhostAndInput`: gated on the resolver's existing `CheckCohesion` — repaired base only when the
    unit is actually out of coherency; coherent units keep the pure rigid path untouched.
  - Tests: 6 new in `GroupFormationUtilitiesTests` (already-coherent no-op, straggler pull, body-stays /
    straggler-only, too-spread fix, budget-from-origin includes repair travel, repair-alone-over-budget →
    not within budget). Suite 25/0; full build clean (0 errors); headless smoke exit 0.
  - **Pending: GUI hand-verification** — kill a model mid-unit, enter Group mode, confirm the ghost re-forms
    into coherency, a click commits it, and dragging to the cap never pushes any model past its limit
    (Advance and Shift/shoot bands).
- 2026-06-21: Item opened. Diagnosis confirmed in `GuiDefineMovementResolver.DrawGroupGhostAndInput` +
  `GroupFormationUtilities.PlanGroupMove` (rigid transform). Cohesion constants: 1" nearest / 9" all-pairs,
  both base-to-base. Plan + algorithm fork signed off with the user.

## Outcome
_(open)_
