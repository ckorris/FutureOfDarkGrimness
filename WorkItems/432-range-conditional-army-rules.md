# 432 — Range-conditional army-wide rules: the "over 9"/6" when shooting/charging" family

**Status**: done 2026-10-29 (engine `ed3eb10c`; the superproject commit `432: ...` bumps it with the supplement data,
app tests, docs and this ledger). Owner review asked on D1, D2, D4 and D5.
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

- 2026-10-29: **Landed.** Engine commit `ed3eb10c` (submodule, `feature/opr-3.6`); the superproject commit bumps it with
  the supplement data, the app tests, `docs/EngineNotes.md` and this ledger.
  - **Texts.** Every re-authored definition carries its 3.6.0 book text verbatim (ASCII), read from the census
    "description text changed" / "New book-defined special rules" sections and cross-checked against the snapshots'
    `specialRules` (identical across books for every name here). Four of them keep OPR's stray space in "-1 to hit
    rolls ." (D12).
  - **Engine vocabulary (new JSON kinds).** Conditions: `attackingFromOver {shootInches, chargeInches}` (Actor seat:
    the bearer shoots enemies over `shootInches` away or charges enemies over `chargeInches` away) and
    `attackedFromOver {shootInches, chargeInches}` (Subject seat / the tested unit: the bearer was shot from over
    `shootInches` or charged from over `chargeInches`). Both read a new capability, `IHasShootChargeDistance`
    (`ShootingDistanceInches` / `ChargeDistanceInches`, each null when that kind of attack is not what happened),
    provided by the hit-roll-modifier, hit-roll-complete and save-roll-complete contexts and by
    `PreMoraleTestContext`. Effect: `rangeModifier` gained `chargeDelta` (`{delta, chargeDelta, minResultInches}`;
    old data without it parses as shooting-only). The 9"/6" pair is each rule's data.
  - **Charge distance (defined and captured).** The pre-charge gap the charger closed: closest living models, base to
    base (3D), snapshotted when the charger's activation begins, before its charge move -
    `UnitActionContext.Reset` -> `SnapshotDistancesToEnemies` (measured from the transport for an embarked unit) ->
    `CombatActionContext.ChargeOriginDistanceAgainstDefender` -> `CombatMetadata.ChargeOriginDistanceInches` -> the
    hit/save contexts. This is #197's existing plumbing (the engine's Charge is the activation's move plus the melee
    attack); #432 only reads it, and only on the charger's swings (`IsCharging`; the save context reads a non-zero
    origin, which only the charger's swings carry). D1.
  - **Engine plumbing.** `AttackedDefender.ShotFromInches` (captured in `RegisterAttackedDefender`, before any of the
    action's shots land) -> `ResolveRangedMoraleStage` -> `MoraleUtilities.ResolveWoundDrivenMorale(..., shotFromInches)`
    -> `TakeMoraleTest(..., shotFromInches, chargedFromInches)` -> `PreMoraleTestContext`; `RollForMoraleStage` passes
    `ICombatActionContext.ChargedFromInchesFor(loser)` (the charge distance when the loser is the charged unit, null
    for a losing charger). Range family: `RangeRuleQueries.EffectiveRange` folds through a shared `RangeFold` (D4);
    `RangeRuleQueries.OwnChargeRangeDelta` adds the bearer's Actor-seat `chargeDelta` to every charge budget
    (`MovementActionContext` unit + per-model, `MovementRuleQueries.PerModelMoveBudgets`, the AI's
    `TacticalAnalysis.ChargeBudget`); `MovementRuleQueries.EffectiveChargeDistanceAgainst` adds the target's
    Subject-seat `chargeDelta` (`TargetChargeRangeModifiers`) beside the legacy `Movement_OnChargeDeclared` movement
    bonus (D3). `RuleOperation.ApplyRangeModifier.Describe` names both halves. `RuleFireLint` seeds two new
    `PreMoraleTestContext` variants (shot from far, charged from far); the hit/save variants already carried far
    shots and far charge origins. Catalog: Ranged Shrouding -4" (was -6"), Melee Shrouding the charge half of the
    range family (-2", was -3" movement), both with the 3.6 text; Agile's description to the 3.6 text (numbers were
    already +1"/+2"). Importer: the Darkborn disambiguation is gone (D6).
  - **Data.** `GdfRuleSupplement.json`: re-authored Devout, Ferocious, Havocbound, Infected, Warbound, Watchborn (+ its
    two helpers), Changebound, Machine-Fog, Screened, Guarded, Sturdy, Knightborn, Hive Bond, Hold the Line,
    Highborn, Lustbound, Scurry; new Brutal, Piercing Warrior, Violent, Versatile Warrior (+ "(Piercing)" /
    "(Precision)" helpers, Aura, Buff), Warding, Darkborn, Shadowborn (moved from the AoF file), Shrouded, Valiant,
    Extra Reach. `AofRuleSupplement.json`: re-authored Lucky, Destroyer, Vinci Tech (+ helpers), Empyrean Spirit,
    Angelic Blessing, Wild Veil, Royal Legion; Shadowborn removed (now GDF). 77 Boost definitions deleted (44 GDF,
    33 AoF - every `X Boost` / `X Boost Aura` / `X Boost Buff` on both census REMOVED lists; none is named by any
    3.6.0 snapshot), keeping the three Feat helpers (D9). `AofBookOverrides`: Melee Shrouding (Chivalrous Kingdoms,
    Dark Elves), Lustbound + Lustbound Boost (Giant Tribes Lust Disciples, Rift Daemons of Lust) and Warbound Boost
    (War Disciples) removed - 3.6 AoF prints the common texts; the three files left empty were deleted. `.fdgbook`
    files untouched (#438).
  - **Tests.** Engine (new): `RangeConditional36ConditionTests` (contexts, strict edges, separate arms, strike-back /
    spell exclusions, morale trigger, JSON round trips, the effect's charge half, `Describe`, validator capability),
    `RangeConditionalOffensiveRuleIntegrationTests` (real `RollToHitStage` / `DetermineSaveRollsNeededStage` /
    `AssignWoundsStage`, the real pick offer, and the charge distance through the real `UnitActionContext` ->
    `CombatActionContext` path; histogram-mode Devout case), `RangeConditionalDefensiveRuleIntegrationTests` (real
    `DetermineHitRollStage`, `RollToHitStage` -> save stage, `AssignWoundsStage` in histogram mode),
    `RangeConditionalMoraleRuleIntegrationTests` (real `ResolveRangedMoraleStage` and `RollForMoraleStage`),
    `RangeFamily36RuleIntegrationTests` (range, target eligibility, charge budget in every reader, per-target charge),
    two forecast / range-tag cases in `ChooseRangedAttackStageTests`, one `RuleFireLintSelfTests` case; shapes shared
    via `RangeConditional36Definitions`. Re-pinned to 3.6 numbers: `RangeModifierRuleIntegrationTests` (Ranged
    Shrouding -4; a 4" gun is not raised to 6"), `MeleeShroudingRuleIntegrationTests` (-2), the Melee Shrouding gate
    case in `MostModelsRuleGateIntegrationTests` (now at the range check), `OprBookImporterTests` (bare Darkborn kept).
    App (new): `RangeConditional36ShippedDataTests` (each shipped definition = the tested shape with the 3.6 text; the
    three picks; Versatile Warrior Aura / Buff; no Boost ships but the Feat helpers; overrides no longer shadow; a list
    naming "Devout Boost" / "Devout Boost Aura" loads with two `Unimplemented` drops; shipped Changebound / Hive Bond /
    Darkborn / Highborn through the real evaluator and queries). Deleted with the family: `BoostRuleCompositionTests`
    (its two non-Boost Changebound cases moved into the new fixture in 3.6 form), `MischievousScrapperBoostShippedDataTests`;
    `ValeOathShippedDataTests` keeps only the base-rule case.
  - **Negative controls run by hand** (each reverted): the shared predicate forced true fails 33 of the family's tests
    (every 8" / 5" / strike-back / spell control); the morale trigger dropped from `PreMoraleTestContext` fails both
    "+1" morale cases; the own charge range dropped from `MovementActionContext` fails the Highborn budget cases; the
    old floor (`max(floor, reduced)`) fails the 4" gun / 5" charge cases and the Ranged Shrouding floor test; the two
    new lint variants removed fail the lint self-test on Hive Bond.
  - **Counts.** Engine 3597 -> 3679 passed (1 skipped before and after). FdgRaylib.Tests 3118 -> 3066 (the deleted
    Boost fixtures and the 64 per-definition lint cases of the deleted definitions, against 43 new cases).
  - **Verification.** Engine suite green; FdgRaylib.Tests green; full `dotnet build` 0 errors (no warnings in touched
    files); headless smoke `printf "2\n2\n" | ... --headless` exit 0 ("Game result: outcome=Tie winner=none rounds=4
    scores=[0, 0]"); `--validate-rules GdfRuleSupplement.json AofRuleSupplement.json` "OK: 292 definitions, no
    problems." (356 - 77 deleted + 13 new; each remaining AofBookOverrides file on top also OK);
    `--rule-coverage FdgRaylib/Assets/Books` "Total references: 22079 / Dead: 36 (36 no-definition across 2 names)"
    - unchanged (the Counter-Attack family): the bundled 3.5 books embed their own Boost definitions, so deleting the
    supplement copies kills no bundled reference until #438 re-bakes. Extra smokes: four 3.6 scratch books compiled with
    `--book-to-army` (Dark Brothers, Human Defense Force, Havoc Brothers, AoF High Elves) played headless with
    `--trace-rules` to exit 0; the traces show e.g. "Havoc Champion's Havocbound at Shooting_OnHitRollComplete/Actor:
    fired -> ApplyRollModifier" and "... condition AttackingFromOver not met", and Hold the Line evaluating
    `And(AttackedFromOver, MostModelsHaveThisRule)` at the pre-morale hook.
  - **Acceptance (scratch 3.6 import of all 90 snapshot entries, outside the repo; recipe as #431: GDF books with the
    GDF supplement, AoF books with GDF + AoF + `AofBookOverrides/<name>.json` when present).** Before (#431's HEAD):
    "Total references: 30839 / Dead: 4783 (4783 no-definition across 137 names)"; after: "Total references: 30839 /
    Dead: 4747 (4747 no-definition across 133 names)". Gone: Shadowborn 22 (GDF books; it lived only in the AoF file),
    Versatile Warrior 11, Versatile Warrior Buff 2, Versatile Warrior Aura 1. None of this item's names remains; the
    other family names (Brutal, Piercing Warrior, Violent, Valiant, Shrouded, Extra Reach, Warding) are defined by
    3.6 books but carried by no unit, so they never counted. Dark Brothers and Dark Prime Brothers now keep the bare
    "Darkborn" (30 / 31 refs) and resolve it to the 3.6 definition. What remains of the family's neighbourhood is the
    Enhanced family (#433).
  - **Deferred / handed on, named.** (1) Versatile Defense: 3.6 turned it into a per-attack defender pick ("you may
    pick a different effect each time the unit is attacked") - a new defender-pick mechanism; its helpers still carry
    the 3.5 `attackedFromOverInches(9)` -> #434/#435 (D8). (2) The shooting-only "over 9"" rules (Precision Marksman,
    Piercing Marksman, Buccaneer, Targeting Visor text) need no new primitive (`distanceGreaterThan`) -> #434/#435;
    so do Primeborn / Guardian / Ossified / Warden, which still carry the 3.5 "over 9"" gate but are unconditional in
    3.6 (Fortified's shape), i.e. no longer part of this family.
    (3) Enhanced X -> #433 (the base rules it lifts are these; the Boost shapes and the composition guard are gone).
    (4) AI / calculator pricing of the range conditions -> #442. (5) The catalog's "Darkborn (Offensive)" /
    "Darkborn (Defensive)" and the 3.5 `attackedFromOverInches` users in old data -> #436. (6) Impact / Ravage hits
    carry no charge distance (D2). No GUI resolver changed: the reach rings read the movement request's budgets and the
    range tag reads the #387 stamp, both engine-computed, so the GUI only needs a glance (below).
  - **GUI glance (Chris, optional).** No ImGui code changed. With a 3.6 army compiled from a scratch import: (1) a
    Darkborn target's range band on the shoot panel reads "range 20" (-4")" for a 24" gun; (2) a Highborn unit's
    Charge reach ring is 2" longer than its neighbours'; (3) the shoot forecast shows "Changebound -1" against a
    Changebound unit 10" away and no chip at 8".
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

- **D1 - the charge distance (2026-10-29) - OWNER REVIEW.** "Charges enemies over 6" away" reads the gap between the
  charger and its target when the charger's activation began (closest living models, base to base), the snapshot
  #197 already takes (`UnitActionContext.SnapshotDistancesToEnemies`), because this engine's charge is the
  activation's one move followed by the melee attack, and by the time the defender is known the pre-move geometry is
  gone. Consequence: an activation-start move that happens BEFORE the charge move (Bounding "When this unit is
  activated, you may move ... by up to D3+1"", Rapid Blink) counts toward the charge distance. Alternative: re-take
  the snapshot when `ActivationStartStage` finishes (one call), which measures from where the charge move itself
  starts. Recommendation: keep (simple, already pinned by `AttackOriginDistanceTests`), revisit if a table reads it
  the other way.
- **D2 - one predicate, two names; what counts as "shot" or "charged" (2026-10-29) - OWNER REVIEW.** The attack
  context is the same object for both seats, so `attackingFromOver` and `attackedFromOver` evaluate the same test;
  two kinds exist so the data reads like the rule text and a future validator can check seats. Spell damage is not
  shooting (null shooting distance): a caster's Devout never procs on spell hits, and a Guarded unit gets no +1
  against a spell from 15". A strike-back is not a charge. Impact and Ravage hits are built as synthetic melee
  attacks with no charge distance (pre-existing; impact also never runs the hit-complete hook), so a Guarded or
  Knightborn unit gets nothing against Impact hits - arguably "charged from over 6"" covers them. Infected / Warbound
  correctly ignore them ("hits from weapons"). Fixing the defensive half means giving the impact metadata the charge
  origin AND a "from weapons" flag the offensive half can read; left for the owner's call (candidate small item).
- **D3 - the range family is one effect (2026-10-29).** The item asks for "a RangeModifier effect for shooting range
  and charge reach", and 3.6 writes the family as one effect over both attack kinds ("-4"/-2" range to a min. of 6"",
  "+4"/+2" range"), so `Effect.RangeModifier` gained `ChargeDelta` rather than authoring each rule as a range entry
  plus a movement entry (the old Shadowborn / Wild Veil / Royal Legion shape). One entry per rule also gives #433 one
  thing to upgrade. Charge range is the charge move (Core Rules 3.6: "may only charge if at least one charging model
  is within charge distance"), so the Actor half joins the charge budget and the Subject half the per-target charge
  query. `ChooseActionStage.GetCanCharge` and `MovementUtilities.ValidateChargeReach`, named in the brief, do not read
  reach: the first gates the post-move melee attack on 3" contact, the second checks that a move beyond the Rush
  allowance ends in melee reach against the budget it is handed. The budget (`MovementActionContext.MaxChargeDistance`
  and DefinePathStage's worst case, which feed the request's hard cap and per-model budgets, hence the GUI rings) is
  where reach lives, so neither needed a change. The legacy `Movement_OnChargeDeclared` movement-bonus shape still
  folds (old army files, bundled books).
- **D4 - "to a min. of 6"" only limits a reduction (2026-10-29) - OWNER REVIEW.** The floor is the largest
  `minResultInches` among the REDUCING ops (a zero or positive delta contributes none, so Melee Shrouding's charge-only
  op never floors shooting), and it never lengthens a range or charge that was already shorter: a 9" gun against
  Darkborn reads 6" (not 5"), a 4" gun stays 4", a 5" charge stays 5". Buffs apply first: a 4" gun with Highborn
  (+4) against Darkborn (-4) reads 6". Before #432 the floor was `max(floor, base + delta)`, which raised a sub-6"
  gun to 6" against Ranged Shrouding. One line in `RangeRuleQueries.RangeFold.ApplyTo` if overruled.
- **D5 - what triggers the morale arm (2026-10-29) - OWNER REVIEW.** Shooting: the distance the shooter was from the
  unit when it first targeted it this action (one shooting position per action; captured before any shot lands, so a
  casualty among the closest models does not move it). Melee: only the CHARGED unit, when it is the loser, reads the
  charge it received; a losing charger was not charged and gets nothing. Spell morale tests, rule-driven tests (Mind
  Control, Fatigue Debuff) and Strafe / Crossing Strike morale ("as if it was shooting" from directly overhead) carry
  no distance, so the rules stay silent there. The last one could be read as "shot from 0"", which gives the same
  answer.
- **D6 - one Darkborn (2026-10-29).** 3.6 prints the same Darkborn in Dark Brothers and Dark Prime Brothers, so the
  importer's army-keyed rewrite to "Darkborn (Defensive)" / "Darkborn (Offensive)" is gone and the bare name is
  defined in the GDF supplement. The two catalog entries stay for old army files and the bundled 3.5 books (#436).
- **D7 - family members beyond the brief's list (2026-10-29).** Lucky (= Devout's text), Destroyer (= Infected's),
  Watchborn and Vinci Tech (= Versatile Warrior's) are the same rule under other names and were re-authored with
  their siblings. Versatile Warrior Aura / Buff were authored (Screened Aura / Guarded Buff shapes) because the 3.6
  books name them and the acceptance asks for none of the family left dead. Shadowborn moved from the AoF to the GDF
  file (GDF books name it; GDF bakes against its own file alone). Ranged Shrouding / Melee Shrouding are catalog
  rules and were re-authored there; Agile only needed its text.
- **D8 - Versatile Defense not re-authored (2026-10-29).** Its 3.6 text makes the pick per attack ("you may pick a
  different effect each time the unit is attacked") instead of at deployment / activation start - a defender
  decision inside the attack, the #423 Parry kind of mechanism, not a range condition. Re-authoring only its range
  half would leave a half-3.6 rule, so it is handed whole to #434/#435.
- **D9 - the Boost deletions (2026-10-29).** Deleted: every supplement definition on either census REMOVED list whose
  name ends in "Boost", "Boost Aura" or "Boost Buff" (77; no 3.6.0 snapshot names one - the only hit is a child
  book's `upgrade/removeOption` label "Lord of Havoc (Havocbound Boost Aura)"). Kept: "Speed Feat Boost", "Precision
  Feat Boost", "Piercing Feat Boost" - one-activation helpers their Feat rules grant, not the 3.5 Boost mechanic.
  The bundled `.fdgbook` files embed their own copies, so bundled armies are unaffected until #438. A list that names
  a Boost with no copy loads with the #168 "not implemented" drop (pinned app-side). `BoostRuleCompositionTests` went
  with the family; its lesson (author an upgrade as the increment, never the boosted total, because every sink adds)
  is handed to #433.
- **D10 - scopes kept (2026-10-29).** The extra-wound rules (Infected, Warbound, Violent, Destroyer) keep the Weapon
  scope their 3.5 copies had ("hits from weapons"; a unit-level mention is re-homed onto the weapons); everything else
  is Unit scope.
- **D11 - Knightborn's two arms (2026-10-29).** Spell wounds: ignored on 2+ (the 3.5 copy fired its 6+ AND its 4+ on a
  spell). Other wounds: 6+ only when shot / charged from over 9"/6". The second arm needs no explicit `not(isSpell)`:
  spell damage has no shooting distance, so `attackedFromOver` is already false for it.
- **D12 - verbatim text with OPR's typo (2026-10-29) - OWNER NOTE.** Changebound, Machine-Fog, Screened and Empyrean
  Spirit read "... get -1 to hit rolls ." in all 3.6.0 books; the definitions keep it, like every other verbatim text.
  #436's parity pass can normalise it if the owner prefers.

### Ledger

| Rule | Definition | Engine test (case + control) |
|------|------------|------------------------------|
| Devout, Ferocious, Brutal / Lucky | GDF / AoF supplement | `RangeConditionalOffensiveRuleIntegrationTests.Devout_ShootingAt10Inches_*` / `Devout_ShootingAt8Inches_*`, `Ferocious_ChargingFrom7Inches_*` / `..From5Inches_*`, `Ferocious_StrikingBack_*`, `Devout_HistogramMode_*`, `TheChargeDistance_IsTheActivationStartGap_*` |
| Havocbound, Piercing Warrior | GDF supplement | `...Offensive...Havocbound_GetsApPlusOne_OnlyOverRange` (10"/8", charge 7"/5") |
| Infected, Warbound, Violent / Destroyer | GDF / AoF supplement | `...Offensive...Infected_OnesToBlockDealAnExtraWound_OnlyOverRange` (4 cases), `Infected_IgnoresSpellDamage_*` |
| Versatile Warrior (+ helpers, Aura, Buff), Watchborn / Vinci Tech | GDF / AoF supplement | `...Offensive...VersatileWarrior_ThePickApplies_OnlyOverRange` (4 cases), `VersatileWarrior_TheChargeArm_ReadsSixInches`, `VersatileWarrior_WithoutAPick_GrantsNothing` |
| Changebound, Machine-Fog, Screened / Empyrean Spirit | GDF / AoF supplement | `RangeConditionalDefensiveRuleIntegrationTests.Changebound_EnemiesGetMinusOneToHit_OnlyFromOverRange` (4), `Changebound_OnAMinority_DoesNothing`; forecast `ChooseRangedAttackStageTests.Compute_ChangeboundDefender_ShowsTheRangeConditionState` |
| Guarded, Sturdy | GDF supplement | `...Defensive...Guarded_DefenderGetsPlusOneDefense_*` (4), `Guarded_OnTheCharger_DoesNotGuardItAgainstTheStrikeBack` |
| Knightborn, Warding / Angelic Blessing | GDF / AoF supplement | `...Defensive...Knightborn_SpellWound_IsIgnoredOnTwoPlus`, `..ShootingWoundFrom10Inches_*` / `..From8Inches_IsNotIgnored`, `Knightborn_ChargeWound_*` (7"/5") |
| Darkborn, Shadowborn, Shrouded / Wild Veil | GDF / AoF supplement | `RangeFamily36RuleIntegrationTests.Darkborn_ShrinksTheShootersRange_*` (24->20, 12->8, 9->6, 4->4), `Darkborn_PushesA21InchTarget_*`, `Darkborn_ShrinksTheChargersReach_*` (12->10, 7->6, 5->5), `Darkborn_OnAMinority_*`; range tag `ChooseRangedAttackStageTests.Enter_StampsEffectiveRange_DarkbornDefender*` |
| Ranged Shrouding, Melee Shrouding (catalog) | `CoreRuleCatalog` | `RangeModifierRuleIntegrationTests` (re-pinned), `MeleeShroudingRuleIntegrationTests` (re-pinned), `RangeFamily36...MeleeShrouding_TheChargeOnlyMember_LeavesShootingAlone` |
| Hive Bond, Hold the Line, Valiant | GDF supplement | `RangeConditionalMoraleRuleIntegrationTests.HiveBond_ShotFrom10Inches_*` / `..From8Inches_*` / `WithoutHiveBond_*`, `HiveBond_ChargedFromOverSixInches_*` (7"/5"), `HiveBond_OnALosingCharger_GetsNothing` |
| Highborn, Extra Reach / Royal Legion | GDF / AoF supplement | `RangeFamily36...Highborn_AddsFourToShootingRange`, `Highborn_AddsTwoToTheChargeBudget_EveryReaderAgrees`, `Highborn_ChargingDarkborn_NetsTwelve` |
| Lustbound, Scurry (Agile: catalog) | GDF supplement | `RangeFamily36...Lustbound_MovesPlusOneOnAdvance_PlusTwoOnRushAndCharge` |
| The 77 Boost definitions | deleted | app `RangeConditional36ShippedDataTests.NoBoostDefinitionShips_ButTheFeatHelpers`, `AListStillNamingABoost_LoadsWithANotImplementedDiagnostic` |

Condition / effect semantics: `RangeConditional36ConditionTests`; lint: `RuleFireLintSelfTests.RangeConditionalFamily_LintsClean`.
App side, every row: `RangeConditional36ShippedDataTests` (shape + 3.6 text) and `RuleSupplementLintTests.EverySupplementRuleFires`.

## Outcome

Done 2026-10-29 on `feature/opr-3.6`. Two engine conditions, `attackingFromOver` / `attackedFromOver {shootInches,
chargeInches}`, read the shooting distance and the charge distance (the pre-charge gap from the charger's activation
start) on every attack hook and, through a new trigger on the pre-morale context, on morale tests; `rangeModifier`
gained a charge half that every charge budget and the per-target charge query fold, with a floor that only limits
reductions. Every rule of the family (offensive, defensive, morale, range, movement: 39 rule names plus 6 pick
helpers, across both supplements and the catalog) carries its 3.6 text on those primitives, and the 77 Boost
definitions are gone. A scratch import of all 90 3.6.0 snapshots leaves none of the family dead (Dead 4783 -> 4747).
Engine `ed3eb10c`. Open for the owner: D1, D2, D4, D5 (and the D12 note); hand-offs to #433, #434, #435, #436, #442
recorded there.

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
