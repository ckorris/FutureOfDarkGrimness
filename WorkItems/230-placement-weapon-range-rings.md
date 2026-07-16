# 230 — Show weapon ranges when deploying, embarking, or ambushing

**Status**: todo
**Related**: #223 (picker stat tooltip), `TableTooltipOverlay.DrawRangeRings` (hover rings for on-table units), `GuiPlaceObjectsResolver`

## Goal
During placement decisions (deployment, ambush arrival, disembark/embark placement) the player should be able to see the unit's weapon ranges on the canvas - e.g. range rings from the ghost/placement position - so they can judge what the spot actually threatens before committing. The hover ring machinery exists for on-table units (`DrawRangeRings`); placement ghosts aren't on-table yet, so the rings need to anchor to the candidate position instead. Decide whether rings follow the cursor ghost live or draw on demand (modifier key?) - live rings on a whole-unit group ghost could be noisy.

## Notes
- 2026-07-15: Filed from user playtest feedback.

## Decisions

## Outcome
