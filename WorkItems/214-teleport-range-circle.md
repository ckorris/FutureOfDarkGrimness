# 214 — Teleport: draw the range-of-motion circle like normal movement does

**Status:** open (filed 2026-07-11 from Chris's play report)
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

## Notes

- 2026-07-11 — filed. Cosmetic/UX only; the rules are already enforced.
