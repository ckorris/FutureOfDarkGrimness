# 041 — Factor line of sight into movement resolver's ranged-targeting overlay

**Status**: implementation complete, awaiting user test
**Related**: #044, #046, commit 5525fb3 (and the MovementUpdates branch broadly)

## Goal
`GuiDefineMovementResolver.DrawTargeting` (was `DrawRangedTargeting`) classified an enemy model as "in range" using only `DistanceUtilities.GetBaseToBaseDistanceInches_2D` vs `weapon.RangeInches`. Both halves of the overlay (the per-enemy-unit aggregate weapon list and the per-line fire arrows from the selected model) need to additionally require line of sight, mirroring what `ChooseRangedAttackStage` and `OcclusionCheckStage` already do — i.e. call `LineOfSightUtilities.HasLineOfSight` with terrain plus other models' bases as circular blockers, excluding the attacker's whole team (044) and the defending unit's own models.

Done when the overlay no longer surfaces weapons or fire lines that would be blocked by terrain or intervening bases, the per-line view shows a red stub + X at the block point when no enemy model in the unit is visible, and the behavior matches what the engine would actually allow if the user committed the move and clicked Shoot.

## Notes
- 2026-05-26: Implemented.
  - At the top of `DrawTargeting`, build a per-enemy-unit `(terrain + model blockers)` snapshot — one `BuildModelBlockers` call per enemy unit per frame.
  - Added a per-frame LoS cache keyed by `(IModel attacker, IModel defender)` over committed positions, populated lazily via a local helper. The selected model's ghost-position LoS bypasses the cache (the ghost moves each frame).
  - Section 1 (per-enemy-unit aggregate weapon counts): added a `HasLineOfSight` check to the inner loop. Weapons whose only in-range enemy model is blocked no longer show up.
  - Section 3 (per-selected-model fire lines): rewrote target-picking per weapon. Now prefers the nearest in-range enemy model with LoS (green line, unchanged). If no in-range model has LoS, falls back to the nearest in-range model overall and renders a red stub from `selPos` to the first blocker's entry point via `LineOfSightUtilities.GetFirstBlockingHit` (046), with an X drawn at that point and weapon labels in the same red.
  - Added an IUnit-typed overload of `BuildModelBlockers` so the resolver can pass `IUnit` objects pulled from `_tableState.Units.Objects` directly, without needing `DataBinding<UnitData>`s.
  - Updated the "Show targeting" checkbox tooltip to mention LoS and the blocked-stub indicator.
  - Build + full submodule test suite still 135/135 green.

## Decisions
- **Render blocked stubs only when no enemy model in that unit has LoS** (one stub + X per weapon, not per enemy model). Matches the existing "one line per weapon to its picked target" convention; rendering blocked stubs for every blocked candidate gets noisy fast and obscures the clear shots that are usually what the player wants to see.
- **Per-frame LoS cache only for committed positions.** The selected model's ghost moves every frame with the mouse, so caching a ghost-position result would either flicker or require keying on a quantized position — neither is worth the complexity for one attacker model. Committed-position checks (used in the aggregate counts) are cached because the same `(our model, enemy model)` pair is asked repeatedly across weapons.
- **Red for blocked**: distinct enough from the green "clear shot" lines and the yellow "can charge" text that the user can tell at a glance whether they're looking at clear, blocked, or charge.

## Outcome
(pending — implementation complete, awaiting user test before close-out)
