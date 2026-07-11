# 215 — Move-as-group option for the post-melee movements

**Status:** open (filed 2026-07-11 from Chris's play report)
**Related:** #019 (consolidation moves), #159 (pile-in/consolidation validation), the normal-move
move-as-group option this mirrors

## Report

The post-melee movements (both of them) should have a "move as group" option, the way the normal movement
resolver does - so the player can translate the whole unit together instead of placing each model
individually.

"Both of them" = the two post-melee movement prompts. Confirm which two the report means when picking this
up - most likely **Consolidation** (Wipeout / Disengage) and the **Pile-in** / post-combat move - and give
each a move-as-group toggle mirroring the normal-move resolver's group mode.

## Where to look

- `GuiConsolidationMoveResolver` (and the post-combat / triggered-move GUI resolver, if that's the second
  one) - port the move-as-group affordance from `GuiDefineMovementResolver` (its group-translate mode +
  the cohesion/overlap re-validation on the whole-unit delta).

## Notes

- 2026-07-11 — filed. Scope note: confirm exactly which two prompts "both of them" refers to before building.
