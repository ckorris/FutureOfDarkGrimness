# 041 — Factor line of sight into movement resolver's ranged-targeting overlay

**Status**: todo
**Related**: commit 5525fb3 (and the MovementUpdates branch broadly)

## Goal
`GuiDefineMovementResolver.DrawRangedTargeting` currently classifies an enemy model as "in range" using only `DistanceUtilities.GetBaseToBaseDistanceInches_2D` vs `weapon.RangeInches`. Both halves of the overlay (the per-enemy-unit aggregate weapon list and the per-line fire arrows from the selected model) need to additionally require line of sight, mirroring what `ChooseRangedAttackStage` and `OcclusionCheckStage` already do — i.e. call `LineOfSightUtilities.HasLineOfSight` with terrain plus other models' bases as circular blockers, excluding the attacking unit's own models and the defending unit's own models.

Done when the overlay no longer surfaces weapons or fire lines that would be blocked by terrain or intervening bases, and the behavior matches what the engine would actually allow if the user committed the move and clicked Shoot.

## Notes
- 2026-05-17: spun off from the path-based movement UI work. Search for the existing `// TODO: factor in line of sight` comment in `DrawRangedTargeting` for the exact call site.

## Decisions
(none yet)

## Outcome
(pending)
