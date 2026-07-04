# 069 — Clean removal of the legacy `Special Rules/` system

**Status**: done
**Related**: engine `19e4b83` (branch `069-remove-legacy-special-rules`), #059 (deleted the bulk of the legacy subsystem 2026-06-13), #042 (the live rule system), audit §8 (`Audit-6-10-2026.md`)

## Goal
Remove the dead legacy special-rules scaffolding the 2026-06-10 audit catalogued, so nothing load-bearing-looking remains to mislead a future reader. Done = the legacy corpse is gone, the engine + app build, the full test suite is green, and a headless game still plays end to end.

## Notes

- 2026-07-03: **Implemented + verified (submodule).** The audit's premise had substantially decayed since 2026-06-10 — #059 already deleted the `SpecialRule`/`ISpecialRule_*` classes, the `IUnit`/`IModel`/`UnitData`/`ModelData` `SpecialRules` properties, `GetRealSpecialRulesFromArmyList`, and `Regeneration.cs`. Re-surveyed the whole repo against every symbol the audit named; the actual residue was smaller and clean to remove. Coordination precondition ("do this after the active special-rules branch merges") was satisfied: every special-rules remote branch (`027`/`033`/`034`/`059`/`093`/`100*`/`feature-026`) is 0 commits ahead of master.
  - **Deleted 7 files.** Three fully-dead corpse files (100% inside `/* */`, referencing types deleted under #059): `ISingleAttackContext.cs`, `ShootStage/FireStage/SingleRangedAttackContext.cs`, `MeleeStage/SwingMeleeWeaponStage/SingleMeleeAttacKContext.cs`. The `ICombatEffect` machinery: `ICombatEffect.cs` + `ICombatEffectsSink.cs` (the sink was implemented only by `CombatStage`, and its effect list was never populated anywhere). The vestige `Rules/Dispatch/RuleHookBus.cs` + `IRuleHookBus.cs` (self-described stub whose `Dispatch` always returned an empty queue).
  - **Simplified `CombatStage.cs`**: dropped the `ICombatEffectsSink<TResult>` base, the `_effects` field/property/`#region`, the `_effects.Clear()` in `Exit()`, and the two always-empty pre/post-execute loops; inlined the live `RunStage -> AddResult -> Finish` path (removing the now-misnamed `RunPostExecuteEffects`). Dropped the now-unused `using System.Collections.Generic;`.
  - **`TestRuleHarness.cs`**: removed the `Bus` property; `Fire()` keeps its real `UnitDestroyedContext` token-cleanup and now returns `Array.Empty<RuleOperation>()`. Refreshed the class + `Fire` doc comments that named the deleted bus.
  - **`SpecialRuleTests.cs`**: refreshed the stale Phase-6 top-of-file comment that described the tests as RED-against-`RuleHookBus` (they've been green against `RuleEvaluator` since Phase 7).
  - **Verify:** full `dotnet build` 0 errors; engine suite **1113/1113**; headless smoke (`printf "2\n2\n" ... --headless`) exit 0, plays 4 rounds to `VictoryCalculationStage`. No new tests added — this is pure dead-code removal with no new behavior; the existing suite (esp. the two `TestRuleHarness.Fire` tests) pins that the surviving `Fire` cleanup path is unchanged.

## Decisions

- **No "Awaiting verification" hold.** This removes only unreachable/never-populated code; there is no runtime behavior change to eyeball in the GUI (mirrors how #024/#079 closed). The headless smoke exercising real combat through the trimmed `CombatStage` is sufficient.
- **Left one comment as historical lineage.** `Rules/Foundation/ERuleSeat.cs` keeps its `<c>ISpecialRule_Attacker</c> / ISpecialRule_Defender` note ("Generalizes the old ...") — it is `<c>` code-formatting, not a `<see cref>`, so it does not break the build, and it accurately documents why `ERuleSeat` exists. Comments are exempt from the cleanup.
- **`RuleHookBus` removal included** (the one test-only piece). It was the audit's named vestige; leaving it would keep #069 partially open. The 7g owner-destroyed cleanup test asserts on token state, not on the bus's (empty) return, so it is unaffected.

## Outcome
Engine `19e4b83` (branch `069-remove-legacy-special-rules`), superproject bump alongside. Removed the last of the legacy special-rules scaffolding: 7 files deleted, `CombatStage` trimmed to its live path, the test harness's vestigial `Bus` retired. Engine 1113/1113, full build clean, headless exit 0. The live special-rule system is entirely the #042 `RuleEvaluator`/`ResolvedRule`/`SpecialRuleDefinition` dispatch; #069's audit §8 corpse is gone.
