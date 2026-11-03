# 158 — Shooting-target chooser: dead models offered + stale rings

**Status**: implemented — awaiting GUI hand-verification
**Related**: #156 (hand-verify round 2 origin), #157 (Takedown per-shot picks, same session), #042 (targeting), #150 (LoS blocker footprints)

## Goal

GUI hand-verify 2026-07-03 (user, #156 round 2): the ranged-attack chooser (a) showed the player
"shooting at a dead model", and (b) drew circular highlights at the last positions of dead AND alive
models. Dead models must not appear anywhere in targeting.

## What was actually wrong (three dead-model leaks, one family)

1. **App — `GuiChooseRangedAttackResolver`** (the reported symptoms): the target-ring loop and the
   shooter-line `NearestModel` helper iterated `targetUnit.ModelBindings` with **no `GetIsAlive()`
   filter** — corpses got rings at their death positions, and the shooter line often aimed at a corpse
   (a just-killed model is frequently the nearest candidate: you shot it last volley). The
   "N/M in range" denominator also counted dead models. Fixed: rings, lines, nearest-candidate, and
   the denominator all consider only living, placed models; the CLI resolver's model count likewise.
2. **Engine — `LineOfSightUtilities.BuildModelBlockers`** (found by the new test, not the user): dead
   models' bases **still blocked line of sight** from wherever they died — casualties are removed from
   play and must not occlude. Fixed with a `GetIsAlive()` skip.
3. **Engine — `ChooseRangedAttackStage.ComputeHasCover`**: the cover majority counted dead models on
   both sides — a squad whose casualties died behind a wall granted its survivors in the open a cover
   bonus. Fixed: only living attackers/defenders count (made internal for the direct test).

Already correct engine-side (verified + pinned): a fully-dead unit is excluded from target enumeration
(`GetIsOnBattlefield` requires a living placed model), and `modelsThatCanShoot` / per-defender range-LoS
checks were alive-filtered all along.

## Notes

- 2026-07-03: Implemented all three fixes. +2 engine tests (`Enter_FullyDeadEnemyUnit_IsNotOffered` —
  deliberately places the wiped unit exactly between attacker and living target, so it also pins the
  corpse-doesn't-block-LoS rule; `ComputeHasCover_CountsOnlyLivingModels` — corpses behind a wall grant
  no cover, a living model behind the wall still gets it), +2 app tests (`NearestModel` skips dead and
  unplaced candidates; all-dead returns null). Engine 1094/0, app 71/0, full build clean; default + AH +
  Takedown-volley headless smokes exit 0. **Awaiting GUI hand-verification**: shoot a unit that has taken
  casualties — no rings or shooter lines on corpses, "in range" counts only the living.
