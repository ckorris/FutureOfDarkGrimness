# 093 — Combat-kind condition (melee-only / shooting-only)

**Status**: todo
**Related**: #042 (Condition × Effect rule system), #015 / #016 (effect seams that want it), #030 / #051 (combat-modifier rules), #032 (weapon rules)

## Goal
Make "applies in melee only" / "applies in shooting only" a **first-class `Condition`** in the #042 data-driven `Condition × Effect` system, so a rule (or the #015/#016 effect seams) can declare its combat-kind gating as data instead of hand-wiring an `IsMelee` check. "Done" = a rule definition can carry a combat-kind condition that the `RuleEvaluator` honours, and at least one existing ad-hoc gate is migrated onto it to prove the path.

## Background
Combat kind is already known at evaluation time — `IsMelee` (and `IsCharging`) are threaded through the combat contexts (`HitRollModifierContext`, `HitRollCompleteContext`, and the metadata behind them). But every rule that cares gates on it *imperatively* and *individually*:
- Indirect: `Not(IsMelee)` — a shooting-only −1-after-moving.
- Thrust: melee + charging.
- Furious: melee gate (the "charging" half is deferred — #051).
- #051: the charging condition for Furious's extra-hits-on-6.

This scatters the same concept across rules and makes new combat-kind-specific effects (like the #015 attack-count and #016 per-hit-save seams) re-invent the gate each time.

## Notes
- 2026-06-21: Spun off from the #015/#016 close-out. The two new seams (`DetermineHitRollStage` attack count; `SuccessfulHitInfo.SaveModifier`) are the immediate customers — attack-count modifiers are typically shooting-only (Rapid Fire), and per-hit save effects can differ by combat kind.

## Decisions
- (none yet)

## Pointers for whoever picks this up
- Conditions live in the #042 dispatch under `FutureOfDarkGrimness/Rules/Dispatch/` (see `CoreRuleCatalog.cs` for how rules/conditions are declared, and the `RuleEvaluator`). Mirror the existing condition shape (e.g. the distance/`AttackerMoved` conditions already evaluated against the contexts).
- The combat-kind flag is already on the evaluation contexts — this should be a read of existing state, not new plumbing.
- Migrate one ad-hoc gate (Indirect's `Not(IsMelee)` is the cleanest) onto the new condition as the proof-of-path; consider whether Thrust/Furious/#051 follow in the same slice or later.

## Outcome
(written when closed)
