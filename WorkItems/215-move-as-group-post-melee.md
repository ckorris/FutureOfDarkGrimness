# 215 — Move-as-group option for the post-melee movements

**Status:** DONE 2026-07-11 (app-side only). GUI UNVERIFIED (needs Chris's eyeball). Scope confirmed:
Consolidation was the only post-melee prompt lacking group mode (the post-combat/Harassing move already has
it via the shared movement resolver; pile-in has no prompt).
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
- 2026-07-11 — **built (Consolidation).** Confirmed with Chris: only the Consolidation prompt (Wipeout /
  Disengage, both served by `GuiConsolidationMoveResolver`) lacked a group option; the post-combat/Harassing
  move already has one (it uses `GuiDefineMovementResolver` + the shared `FormationModeState`), and pile-in
  is engine-computed with no prompt. Chose full parity (translate + rotate).

## Outcome

App-side only (`GuiConsolidationMoveResolver` + one line in `ResolverRegistryFactory`); no engine change.

- `GuiConsolidationMoveResolver` now takes the shared `FormationModeState` (same instance the movement +
  deployment resolvers use, so the Group/Single choice carries across stages) and gained a `DrawGroupMode`:
  drag translates the whole unit so its centroid follows the cursor; mouse wheel / R (Shift+R reverses)
  rotates the formation. It reuses the SAME shared machinery as the normal-move group mode -
  `GroupFormationUtilities.PlanGroupMove` (per-model budget solve against the consolidation cap),
  `RepairCoherencyByContraction` (one drag pulls a casualty-holed unit back into cohesion), and the shape-aware
  overlap/cohesion previews. Phantoms turn red + block commit when a model would end on another unit or bust a
  model's remaining budget; the accumulated rotation is baked into each committed step's facings via
  `GetResultsAsList(facingOffsets)`. A "Mode: Group/Single (G)" button + the G key flip the mode.
- The authoritative `ValidateConsolidationPaths` in `ConsolidateStage` (and the preview's Done gate) still has
  the final say, so group mode only ever previews/commits a step the engine accepts.

**Verify:** app suite 327/0; builds clean; headless smoke clean. The group drag/rotate feel + commit is for
Chris to eyeball - it can't be driven headlessly.

Commit: this superproject commit (no submodule change).
