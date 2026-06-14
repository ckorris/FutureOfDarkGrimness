# 090 — Enemy-check consolidation + executor moves; Strafing fly-over exemption

**Status**: done
**Related**: engine `bf2b41b`; follows #011/#089 (enemy-aware movement validation) and #050 (base-radius); #042 (rule dispatch); #019 (consolidation)

## Goal
Close the three movement paths #011 left unchecked, plus fix the regression #011 caused for fly-over rules:
1. **Consolidation** — `ConsolidateStage` (and its GUI/CLI/AI resolvers) must run the enemy move-through / standoff check so a consolidation move can't pass through or stack on a living enemy.
2. **Vanguard triggered move** — `MovementExecutor.TryMove` (the authoritative check behind `GameOperationServices.MoveUnit`) must be enemy-aware; the `DefineMovementPathRequest` resolvers already are.
3. **Strafing / fly-over exemption** — #011's `ValidateMovingThroughEnemyUnits` has no fly-over exemption, so a unit with a move-through capability (Strafing) is wrongly blocked from pathing through an enemy in a real game (the integration test passes only because it bypasses `DefinePathStage`). Add a `canMoveThroughEnemies` capability, queried from #042 rules, that skips the pass-through block (but still forbids ending stacked on an enemy).

"Done" = all four movement entry points (DefinePathStage, ConsolidateStage, TryMove, + their resolvers) agree on the same enemy-aware validation with the fly-over flag, integration tests cover consolidation-through-enemy (blocked), Strafing-through-enemy (allowed), and Vanguard-through-enemy (blocked); suite green; headless smoke clean.

## Notes
- 2026-06-14: Implemented all three on the branch. Suite 503→511 (+6 fly-over validation, +2 DefinePathStage regression integration); full build + headless smoke clean (exit 0, consolidations firing each game). Pending commit (submodule-first + app bump).
  - **Capability (Option 1)**: new `RuleOperation.IgnoreEnemyMovementBlock` + `Effect.IgnoreEnemyMovementBlock` (mirrors `IgnoreTerrainEffects`; the doc-foreshadowed "Flying-only facet"), `[JsonDerivedType]` registered. Granted by a passive `HookEntry` on Strafing at `Movement_OnMoveThroughEnemy`. Queried by new `MovementRuleQueries.CanMoveThroughEnemies(unit, evaluator)` (non-logging, mirrors `SightRuleQueries`).
  - **Validator**: `ValidateMovingThroughEnemyUnits` gained a `canMoveThroughEnemies` flag that skips only the pass-through block (ending-stacked + standoff still apply). Threaded through a new no-charge enemy overload `ValidatePaths(moves, maxDist, footprints, canMove, terrain, out)` (consolidation/executor) and the charge overload (gained the flag; a 6-arg back-compat wrapper keeps existing callers/tests compiling).
  - **Call sites**: `DefinePathStage`, `GameOperationServices.MoveUnit`, `MovementExecutor.TryMove`, `ConsolidateStage` all compute the flag (+ footprints where needed) and pass it. `CanMoveThroughEnemies` rides `DefineMovementPathRequest` + `ConsolidationMoveRequest` so resolvers agree.
  - **Resolvers**: GUI/CLI/AI DefineMovement now pass `request.CanMoveThroughEnemies`; GUI/CLI/AI Consolidation now enemy-check (footprint helpers + flag). AI consolidation gained a validate-and-backoff (it previously never validated, so a wipeout toward another enemy could be rejected). CLI consolidation gained `ITableState` (factory updated) to validate-and-reprompt.
- 2026-06-14: Opened. Scope signed off (all three on one branch); capability model = Option 1 (#042 query op mirroring SightRuleQueries), permission granted via a passive hook on the Strafing rule. Branch `090-enemy-check-consolidation-executor` (both repos), based on synced master (#011/#089/#050 already in base).
  - Findings: `MovementExecutor.TryMove` has one caller (`GameOperationServices`, throws on invalid). Strafing's movement is the normal `DefinePathStage` path (already enemy-checked), not the executor — so the #011 note was imprecise; the real Strafing issue is the missing fly-over exemption. `DefineMovementPathRequest` resolvers (GUI/CLI/AI) are already enemy-aware; consolidation resolvers are not.

## Decisions
- **`canMoveThroughEnemies` skips only the pass-through block.** Ending stacked on an enemy and the 1" standoff still apply — a fly-over move ends clear of the enemy on the far side, so it satisfies standoff naturally.
- (capability model) **Option 1 — `IgnoreEnemyMovementBlock` passive op + `MovementRuleQueries.CanMoveThroughEnemies(unit, evaluator)`**, mirroring `IgnoreCover`/`SightRuleQueries`. Granted by a passive hook on Strafing for now; a future Flying rule can emit the same op.

## Outcome
All three movement paths #011 left unchecked are now enemy-aware, and the Strafing fly-over regression is fixed. Consolidation (`ConsolidateStage` + GUI/CLI/AI resolvers) and the Vanguard triggered move (`MovementExecutor.TryMove`) run the same move-through/standoff check as normal movement. Fly-over units (Strafing, via the new `IgnoreEnemyMovementBlock` op queried by `MovementRuleQueries.CanMoveThroughEnemies`) may path through enemy bases again — verified end-to-end through the real `DefinePathStage` — while still being barred from ending stacked on an enemy. Suite 511/0; headless-verified. Deferred: `PathTemplate`'s preview validator still uses the no-enemy overload (pre-existing, preview-only — not a committed move); the advance-vs-charge action-type distinction (#011 item (a)) remains out of scope (tied to #051).
