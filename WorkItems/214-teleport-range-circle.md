# 214 — Teleport: draw the range-of-motion circle like normal movement does

**Status:** implemented 2026-07-23, awaiting GUI hand-verify (it is a visual, so it cannot be verified headlessly)
**Related:** #197 (Teleport: the 6in reposition menu action), #205/#204 (recent movement/presentation work)

## Report

Teleport works and correctly STOPS me from placing outside its range, but - unlike normal movement - it
doesn't SHOW the range of motion. A range circle (the reachable radius from the model's position) would make
it clear where you can teleport to, matching the movement resolver's reach preview.

## Where to look

- Teleport shipped in #197 as a pre-attack reposition (place within 3in on Advance/Charge, 6in on Rush;
  ignores terrain + intervening enemies). Find its placement resolver / overlay (the `TeleportStage` seam +
  whatever GUI resolver renders its placement) and draw a reach circle of the allowed radius around the
  model's current position, the way `GuiDefineMovementResolver` renders its move rings.
- Placement is already correctly bounded (it rejects out-of-range) - this is purely the missing visual.

## Root cause (2026-07-23)

Not a regression - there was never a ring here. `git log -S MaxDistanceFromStartInches` on
`GuiPlaceObjectsResolver` shows the field arriving with reposition-at-activation (`7e3d292`) for
VALIDATION only, and no `AddCircle` was ever added for it. Disembark looks right by accident of a
different design choice, not by a shared mechanism:

| | how the constraint is expressed | drawn? |
|---|---|---|
| Disembark (`DisembarkStage.cs:57`) | a `CircularZone` **deployment zone** of 6" around the transport | yes - `DrawZone` renders the request's zone |
| Teleport (`TeleportStage.cs:63-68`) | zone = **the whole table**; the real bound is `MaxDistanceFromStartInches: 6f` | no - nothing drew that field |

The two are genuinely different shapes: Disembark bounds every model by ONE circle (around the
transport), Teleport bounds each model by its OWN start. So Teleport can't just borrow a `CircularZone` -
it needs one ring per model.

## Fix

`GuiPlaceObjectsResolver.DrawReachRings` draws a reach circle per model still to be placed, centred on
that model's own start, whenever `MaxDistanceFromStartInches > 0`. The model the next click affects is
drawn brighter; in group mode before the first drop every ring is live (one click places them all).
Selection rules extracted to `FdgRaylib/Rendering/ReachRingPlan.cs` so they are testable without ImGui
(the `MeasurementGeometry` precedent) - `ReachRingPlanTests`, 7 tests.

**Covers more than Teleport:** every reposition-style placement rides the same field, so Fanatic (9") and
reposition-at-activation (Wolfborn / Bounding / Rapid Blink, 2.5") gain the ring too - they were all
equally blind.

## Notes

- 2026-07-23 — implemented. App-side only, no engine change. App 478/478, engine 1917/1917, build clean,
  headless smoke exit 0 - but none of that touches the pixels: **needs a GUI hand-verify** (teleport a
  multi-model unit, confirm one green ring per unplaced model at the right radius, brighter on the active
  one, and that the ring matches where clicks are actually accepted).
- 2026-07-11 — filed. Cosmetic/UX only; the rules are already enforced.
