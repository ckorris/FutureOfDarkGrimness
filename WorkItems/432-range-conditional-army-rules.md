# 432 — Range-conditional army-wide rules: the "over 9"/6" when shooting/charging" family

**Status**: todo
**Related**: #415, #428 (most-models), #197 (faction rule primitives), #433 (Enhanced variants lift these conditions), #434/#435
**Branch**: `feature/opr-3.6`. Model/effort: Opus 5.5 / high.

## Goal

3.6 made most army-wide rules conditional on distance (patch notes: "Offensive army-wide rules work from
over 9"/6" when shooting or charging. Defensive rules work from over 9"/6" when shot or charged. Morale-boosting
rules work when shot or charged from over 9"/6""). Representative 3.6 texts:

- Offensive: Devout / Ferocious / Brutal: "When this model shoots/charges enemies over 9"/6" away, unmodified
  result of 6 to hit deal 1 extra hit"; Havocbound / Piercing Warrior: "...its weapons get AP(+1)"; Infected /
  Violent / Warbound: "...unmodified results of 1 to block hits from weapons deal 1 extra wound"; Versatile
  Warrior: activation pick of AP(+1) or +1 to hit, over 9"/6".
- Defensive: Changebound / Machine-Fog / Screened / Empyrean Spirit: "If most models in this unit have this
  rule, enemy units shooting/charging them from over 9"/6" away get -1 to hit rolls"; Guarded / Sturdy: "+1 to
  Defense rolls when shot/charged from over 9"/6" away"; Knightborn / Warding / Angelic Blessing: spell wounds
  ignored on 2+, other wounds on 6+ only when shot/charged from over 9"/6".
- Morale: Hive Bond / Hold the Line / Valiant: "+1 to Morale Test rolls when shot/charged from over 9"/6" away".
- Range debuffs: Darkborn / Shadowborn / Shrouded / Wild Veil: "When a unit where most models have this rule is
  targeted by an enemy unit for shooting/charging, that enemy unit gets -4"/-2" range to a min. of 6"";
  Melee Shrouding: "-2" range to a min. of 6"" when charged; Highborn / Extra Reach / Royal Legion: "+4"/+2"
  range when shooting/charging" (3.5 Highborn was a movement bonus).

"Done" = two new conditions - `AttackingFromOver(shootInches, chargeInches)` on the Actor seat (distance
from the firing model/unit to the target when shooting; charge distance when charging - define "charge
distance" as the pre-charge gap the charger closed, measured unit-to-unit, and record it) and
`AttackedFromOver(...)` on the Subject seat - plus a range/charge-reach modifier effect for the debuffs and
buffs, and every rule above re-authored on them with tests. The 9"/6" pair is the rule's two arguments in
data, not a constant.

## What changes

1. Engine: conditions above over the existing attack contexts (shooting range is known at `RollToHitStage`;
   charging needs the charge-distance recorded on the melee context - check `IsCharging` plumbing); a
   `RangeModifier` effect for shooting range and charge reach (positive and negative with the 6" floor) read
   by `RangeRuleQueries` / `ChooseActionStage.GetCanCharge`.
2. Data: re-author the family in `GdfRuleSupplement.json` / `AofRuleSupplement.json`; delete every `X Boost`
   definition (replaced by #433's Enhanced family).
3. Tests: shooting at 8" vs 10" flips Devout; charging from 5" vs 7" flips Ferocious; defender's Guarded at
   10" shot; Hive Bond morale at 10"; Darkborn shrinks a 24" gun to 20" and a 12" charge to 10" with the 6"
   floor; Highborn extends.

## Acceptance

Tests green; `--validate-rules` OK; the #325 forecast tags show the condition state.

## Code anchors (survey 2026-10-29)

- `RollToHitStage` (range available), melee charge context (`ChargeContactRules.cs`, `IsCharging`),
  `RangeRuleQueries.cs`, `MovementModifierSink.cs`, supplement entries Changebound/Devout/Highborn/Darkborn.
  See the appended survey rows below.

### Survey rows (code survey 2026-10-29)

- Shooting range is known in `RollToHitStage.cs`; `Rules/Foundation/IHasAttackOriginDistance.cs` (+
  `Tests/AttackOriginDistanceTests.cs`) is the existing attack-origin-distance capability to build the
  "over 9" away" condition on. Charging: `Rules/Dispatch/ChargeContactRules.cs`, `Condition.IsCharging`.
- Range modifier precedents (Subject seat): Aircraft -12" `CoreRuleCatalog.cs:2021-2025`, Darkborn :2130/:2135,
  Ranged Shrouding :2082, Melee Shrouding :2158; `Rules/Dispatch/RangeRuleQueries.cs`,
  `MovementModifierSink.cs` (Highborn/Lustbound were movement rules in 3.5).
- Data to re-author lives in `GdfRuleSupplement.json` / `AofRuleSupplement.json` (Changebound, Devout, Ferocious,
  Havocbound, Hive Bond, Hold the Line, Knightborn, Machine-Fog, Guarded, Infected, Warbound, Highborn,
  Lustbound, Darkborn ...).

## Notes

- 2026-10-29 (from #431): new condition/effect kinds you can use (semantics in #431 Notes and `Condition.cs` /
  `Effect.cs`). Conditions: `targetOverHalfWeaponRange` (shooting distance > half the FIRING weapon's printed range; false in melee,
  for range 0, or with no weapon), `targetMostModelsDefenseInRange {minDefense, maxDefense}` (strict majority of the
  target's living models, per-model Defense incl. a joined hero), `targetAboveHalfStrength` (complement of the morale
  "half or less"), `targetShakenOrFatigued`, `targetMostModelsWithinInchesOfTerrain {distanceInches}` (the target-side
  twin of `mostModelsWithinInchesOfTerrain`). Effects: `ignorePositiveDefenseModifiers` (author at
  `Shooting_OnHitRollComplete`), `countsAsWoundsTaken {onRollValue, woundsPerDie}` (at `Shooting_OnSaveRollComplete`;
  counted, not dealt - melee result + shooting morale trigger). Reuse rather than re-mint: "re-roll successful 5-6" is
  `reroll Save onUnmodifiedValue minValue 5`, "most models Tough(X)+" is `targetMajorityHasTough`.
  None of them is a range-from-the-attacker gate of the 9"/6" kind - that family is still yours.
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
