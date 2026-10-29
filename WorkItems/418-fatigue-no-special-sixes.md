# 418 — Fatigue 3.6: fatigued hits on unmodified 6 do not trigger "on a 6" rules

**Status**: todo
**Related**: #415, #030 (Furious), #032 (Rending), #202 (Fatigue plumbing), #431 (new 6-triggered weapon rules)
**Branch**: `feature/opr-3.6`. Model/effort: Opus 5.5 / high.

## Goal

Core Rules v3.6.0, Fatigue: "After a melee where they attacked, units hit on unmodified rolls of 6 in melee
until the end of that round, and those hits don't count as rolls of 6 for special rules." The second clause
is new. "Done" = while a unit is Fatigued, its melee hits scored on a natural 6 do not fire any
"unmodified result of 6" effect: Furious, Rending (AP+4 on 6s), Surge, Relentless (shooting only - unaffected,
Fatigue is melee), Devout/Ferocious/Brutal-style extra hits, Fracture (#431), Bloodborn/Predator/Primal
"5-6" variants (#433), and the per-hit save modifier path (`PerHitSaveModifier OnRollValue 6`). Shred
(1s to block) and Bane (re-roll 6 saves) are defender-side rolls and are NOT affected.

Note the 3.6 patch-note phrasing "Unmodified 6s while Fatigued no longer count as 6s for special rules" and
the Furious/Surge/Relentless texts' "only the original hit counts as a 6 for special rules" - the engine must
distinguish "this die is a 6" from "this hit counts as a 6 for special rules", and Fatigue clears the latter.

## What changes

1. Engine: find how Fatigue is represented (token) and where the hit-roll pipeline exposes the natural roll
   to conditions (`Condition.UnmodifiedRollEquals`, `Effect.PerHitSaveModifier(OnRollValue:6)`, the
   `HitRollComplete` context). Add one "counts as 6 for rules" flag per hit that is false when the attacking
   unit is Fatigued and the attack is melee; route every 6-trigger through it rather than patching each rule.
2. Make sure the Shaken-strike-back path ("may strike back counting as Fatigued") sets the same flag.
3. Tests: Furious + Fatigued = no extra hit; Rending + Fatigued = no AP+4; a non-Fatigued control; Shred
   unaffected; a histogram-mode (`ProbabilisticDiceRoller`) run for one rule.
4. Resolver/presentation: the dice panel's "6!" callouts (#245/#238) should not flag special-rule triggers for
   fatigued hits; the #325 forecast tags should drop the trigger when the unit is Fatigued.

## Acceptance

Integration tests above green; the shooting forecast and dice panel agree with the engine in a
hand-verified fatigued melee (Chris).

## Code anchors (survey 2026-10-29)

- `Rules/Dispatch/CoreRuleCatalog.cs` Furious/Rending/Surge/Relentless entries; `RollToHitStage`;
  `Condition.UnmodifiedRollEquals`; `Effect.PerHitSaveModifier`; Fatigue token in `Rules/Tokens/`.
- See the appended survey rows below for exact lines.

### Survey rows (code survey 2026-10-29)

Findings that shape the item: Fatigue is a unit token; `CountsAsFatiguedInMelee` already treats Shaken as
Fatigued; the hit threshold is forced to 6 in `DetermineHitRollStage`, but the `HitRollCompleteContext` the
6-trigger rules read is built from the RAW rolls with no fatigue filter. No test combines Fatigue with a
6-trigger today.

- Token: `Rules/Foundation/TokenType.cs:6, :117` (FATIGUED); definition `Rules/Tokens/TokenDefinitionCatalog.cs:78-80`
  (clears at round end); `MainUnitActionStage/FatigueUtilities.cs:15` `ApplyFatigued`, `:26-27`
  `CountsAsFatiguedInMelee`; applied in `MeleeStage/ApplyFatigueStage/ApplyFatigueStage.cs:19-40`.
- Threshold: `ShootStage/FireStage/DetermineHitRollStage/DetermineHitRollStage.cs:116` (fatiguedInMelee),
  :143-147 (HitRollNeeded = 6), :122/:133 (skips buffs), :172 ("Fatigued: 6s only" tag). Copies:
  `Ai/Tactician/CombatMath.cs:347-348`, `Calculator/CombatCalculator.cs:117-119`.
- The gap: `RollToHitStage/RollToHitStage.cs:84` builds `HitRollCompleteContext` from raw results;
  `Rules/Definitions/Condition.cs:240-244` `UnmodifiedRollEquals` reads `UnmodifiedHitRolls.At(DieValue)`;
  `Effect.cs:154` `PerHitSaveModifier(OnRollValue)`, `:199-203` `AddExtraHit`; `MainUnitActionStage/
  PerHitApSplitter.cs:47-51`; context record `Rules/Dispatch/Contexts/HitRollCompleteContext.cs:30-35`; other
  builders `SyntheticHitResolution.cs:66`, `ShootingForecast.cs:133`, `CombatMath.cs:356`, `RuleFireLint.cs:684-702`.
- 6-trigger catalog rules: Surge `CoreRuleCatalog.cs:398-409`, Relentless :412-423, Furious :426-438, Rending
  :754-769, Crack :776-787 (dropped by 3.6), "Rending in melee" :937. Bane :793 and Lacerate :814 trigger on
  DEFENSE 6s (unaffected).
- Tests: `Tests/FatigueTests.cs`, `CombatCalculatorTests.cs` (AFatiguedAttackerOnlyHitsOnSixes),
  `DiceBeatGlanceMetadataTests.cs`, `ExtraHitRuleIntegrationTests.cs`, `RendingRuleIntegrationTests.cs`.

## Notes

- 2026-10-29: filed with the plan.

## Decisions

(none yet)

## Outcome

(open)

## Rules for the agent (plan section 1, restated)

- Branch `feature/opr-3.6` in BOTH repos; check `git branch --show-current` in each before committing.
  Never commit, merge or push `master`. Submodule-first commit cadence; subjects start with `NNN:`.
- 3.6 rule text: `~/Projects/GDF Armies/Rules 3.6.0/` (core PDFs' text extracts, common-rules JSON) and the
  3.6.0 book snapshots. Never commit anything from that folder. No 3.5 fallbacks or version flags.
- Vertical slice: engine change + CLI resolver + GUI resolver/overlay + AI resolver legality + an
  integration test mirroring the nearest `*RuleIntegrationTests` + the `docs/` line if a number changed.
- Verify per CLAUDE.md (engine suite green, full build, headless smoke exit 0) and
  `FdgRaylib --rule-coverage FdgRaylib/Assets/Books` not regressing. Never commit red.
- Keep this file's Notes/Decisions current and flip the row in `docs/opr-3.6-upgrade-plan.md` section 5.
  A design fork stops for Chris's sign-off with options + a recommendation written here.
