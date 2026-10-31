# 044 — Allied/same-team models don't block line of sight

**Status**: done
**Related**: #041, #045, #046

## Goal
`LineOfSightUtilities.BuildModelBlockers` previously excluded only the attacker's unit and the defender's unit; models from any *other* same-team friendly unit on the attacker's side still acted as Blocking circular terrain. Per the rules a sight line passes through models of the shooter's own player and allied players, so every model on the attacker's team must be excluded. Done when no same-team model blocks LoS in `ChooseRangedAttackStage` / `OcclusionCheckStage` / `CoverCheckStage`, and the new behavior is covered by unit tests.

## Notes
- 2026-05-26: Implemented. `BuildModelBlockers` now looks up the attacker's team via `tableState.Teams` and excludes every model whose unit's `PlayerID` is on that team, plus the defender unit's models. Falls back to attacker-player-only exclusion when no team is registered (matches behavior expected by existing helper tests). Added `AlliedUnitModel_OnSightLine_DoesNotBlock` and `ThirdPartyEnemyUnitModel_OnSightLine_StillBlocks` in `ModelBlockerTests`; full submodule suite 129/129 green.

## Decisions
- Excluded the **defender unit** as well even when it's on a different team. This preserves the existing rule that a shooter sees through their own target, and matches what callers (`ChooseRangedAttackStage`/`OcclusionCheckStage`/`CoverCheckStage`) already assumed.
- Fall back to "attacker's player only" when no team is registered, so unit tests and headless harnesses that skip team setup keep their previous behavior.

## Outcome
`BuildModelBlockers` now respects team membership: allied units no longer block LoS for any caller. Two new test cases (ally exclusion, third-party enemy still blocks) added to `ModelBlockerTests`. Full engine test suite passes (129/129). Unblocks 041 (overlay LoS — needs the corrected gating) and 046 (`GetFirstBlockingHit`, which will reuse the same blocker list).
