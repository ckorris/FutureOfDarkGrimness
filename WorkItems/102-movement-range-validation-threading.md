# 102 — Movement & range modifier validation threading (Strider, RangeModifier family)

**Status**: todo
**Related**: #100 (deferred from here — the invasive Part-1 #4 items), #029 (movement-modifier rules umbrella — Strider/Aircraft/Flying live there), #027 (weapon-scoped rules)

## Goal
Wire the two special-rule effects that change how far a unit may move/shoot through **validation**, both of which the engine declares but no stage consumes today, and both of which need a per-unit flag threaded through the movement/range validation path across **both repos**:

- **Strider** (`Effect.IgnoreTerrainEffects`) — the unit ignores the **Difficult-terrain movement cap**. The engine already *enforces* that cap (`MovementUtilities.ValidateMovingThroughDifficultTerrain` adds `ExceededDifficultTerrainMoveLimit` when a move crossing Difficult terrain exceeds `GameWideConstants.DIFFICULT_TERRAIN_MOVE_CAP_INCHES`); Strider should waive it. Also unblocks **Aircraft/Flying** (#029), which ignore all terrain.
- **RangeModifier family** (`Effect.RangeModifier`) — **Increased Shooting Range** (+6"), **Ranged Shrouding** (enemies get −X" range vs this unit), **Darkborn** (+range). The op (`RuleOperation.ApplyRangeModifier`) has no consumer; the shooting target-eligibility / range checks read the weapon's base range only.

Deferred out of #100 because these are **not "finish-a-seam" slices** — they thread a flag through core movement/range validation across ~14 files in both repos, with an open architectural question (below). Doing them well needs a design pass, not an inline continuation.

## The shape (and why it's invasive)
There's a proven precedent for the pattern: **`MovementRuleQueries.CanMoveThroughEnemies`** (Strafing's fly-over). It does one non-logging `EvaluateAllNamed(MoveThroughEnemyContext, …)` read for a rule-derived flag, and that flag:
- rides **`DefineMovementPathRequest.CanMoveThroughEnemies`** — computed once in `DefinePathStage` and read by the resolvers (so engine + GUI/CLI/AI agree),
- is a param on the **`MovementUtilities.ValidatePaths`** overloads, threaded into the per-validator (`ValidateMovingThroughEnemyUnits`).

Strider would parallel this exactly: add `MovementRuleQueries.IgnoresDifficultTerrain(unit, evaluator)`, an `IgnoresDifficultTerrain` flag on the move (and consolidation) request, an `ignoresDifficultTerrain` param on `ValidatePaths` + `ValidateMovingThroughDifficultTerrain` (skip the cap when set), and have every caller compute + pass it. RangeModifier is the same idea on the **shooting** side (a per-unit range delta read once and threaded into `ChooseRangedAttackStage`'s target-eligibility / `IsTargetWithinRange` and the GUI targeting overlay).

**Call sites that thread `canMoveThroughEnemies` today** (the surface a parallel flag touches):
- Engine: `DefinePathStage`, `PathTemplate`, `ConsolidateStage`, `MovementExecutor`, `GameOperationServices`, `AiDefineMovementResolver`, `AiConsolidationMoveResolver`.
- App (FdgRaylib): `GuiDefineMovementResolver`, `GuiConsolidationMoveResolver`, `DefineMovementPathResolver` (CLI), `ConsolidationMoveResolver` (CLI).
- Plus the movement-validation test files.

## Open question to settle before building
**Where is Difficult terrain authoritatively validated?** `PathTemplate.Validate` calls the **no-terrain** `ValidatePaths` overload (`terrain: null`), so it does NOT enforce the Difficult cap — that enforcement appears to live in the **resolvers'** own `ValidatePaths(…, terrain, …)` calls (the gray-out/preview), with the engine trusting the submitted path. If so, Strider only needs to be honoured in the resolver previews + wherever the engine re-validates; if the engine has an authoritative terrain check elsewhere, that's the must-wire point. Confirm this before wiring, or Strider risks being cosmetic (preview-only) or inconsistent.

## Notes
- 2026-06-22: Opened to hold the invasive movement/range-modifier work deferred out of #100 (where #1, #3, #4-Shred/Heal/RestrictActions, and all of #2 landed). Number **102** chosen at the user's request — free on `origin/master` (top item there is 098); confirm no collision at merge per the never-reuse rule (another in-flight branch could have claimed it). Engine effects (`IgnoreTerrainEffects`, `RangeModifier`) are already declared with `.Apply`; the work is the consumer + the cross-repo flag threading, not new effect types.

## Decisions
_(none yet — pending the design pass + the open question above)_

## Outcome
_(written when closed)_
