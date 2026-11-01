# 431 — New weapon-rule family: Anti-Cover ... Crossing Strike

**Status**: done 2026-10-29 (engine `3b92c54e`; superproject commit `431: ...` bumps it with the supplement data, app
tests and docs). Owner review asked on D2, D3, D5 and D8.
**Related**: #415, #418 (Fatigue 6s), #032 (weapon rules), #100 (primitives), #164 (DealHits.WithRules), #434/#435 (consumers)
**Branch**: `feature/opr-3.6`. Model/effort: Opus 5.5 / high.

## Goal

Every 3.6 weapon-scoped rule below has a validated definition (catalog or `GdfRuleSupplement.json` - the
AoF supplement inherits GDF names) and an integration test. Texts are the 3.6.0 book definitions (identical
across books; the census has them all):

- Anti-Cover: "This weapon ignores Cover." (45 GDF / 14 AoF books; replaces Ignores Cover)
- Anti-Regen: "This weapon ignores Regeneration." (replaces Ignores Regeneration)
- Smash: "When attacking units over half of this weapon's range away, the target must re-roll unmodified
  Defense results of 6."
- Pierce: "Ignores all positive Defense modifiers on the target, and when attacking units over half of this
  weapon's range away, this weapon gets AP(+1)."
- Fracture: "For each unmodified result of 6 to hit, those hits get AP(+2)." (Rending-shaped; #418 applies)
- Fragment: "When attacking units where most models have Defense 5+ to Defense 6+, the target must re-roll
  successful unmodified Defense results of 5-6."
- Puncture: "Ignores Regeneration, and when attacking units where most models have Defense 2+ to Defense 3+,
  this weapon gets AP(+2)."
- Obliterate: "Ignores Regeneration, and when attacking units where most models have Tough(3) or higher, the
  target must re-roll successful unmodified Defense results of 5-6."
- Decimate: "When attacking units with more than half of their total size or Tough value (for single-model
  units), the target must re-roll unmodified Defense results of 6."
- Destructive (re-authored): "When attacking units with more than half of their total size or Tough value
  (for single-model units), those hits get AP(+1)."
- Disintegrate (re-authored): "Ignores Regeneration, and when attacking units where most models have Tough(3)
  or higher, this weapon gets AP(+2)."
- Demolish: "Ignores Cover, and when attacking units with most models within 1" of terrain, this weapon gets
  AP(+1)."
- Purge: "When attacking units that are Shaken or Fatigued, this weapon gets AP(+2)."
- Reap: "When attacking units that are Shaken or Fatigued, the target must re-roll successful unmodified
  Defense results of 5-6."
- Scourge: "For each unmodified result of 1 to block hits, enemies count as having taken 2 extra wounds when
  checking who won melee, or if shooting wounds would trigger Morale Tests." (feeds #419's taken-count and the
  ranged morale trigger; no real wounds)
- Unwieldy: read the 3.6 text (two GDF + two AoF books) - likely "-1 to hit" or a move penalty; record it.
- Imprecise: "Gets -1 to hit when attacking."
- Crossing Strike: "Once per activation, when this model moves through enemy units, pick one of them and
  attack it with this weapon as if it was shooting. This weapon may only be used in this way." (needs the
  Flying move-through path; a mid-move attack - nearest precedent is Strafing, which 3.6 dropped; reuse its
  stage shape)
- Cumbersome is #425; Assault is #424; Parry is #423; Brace is #422.

New engine conditions needed (write them once, name them for #432-#435): target-over-half-weapon-range,
target-most-models-Defense-in-range, target-most-models-Tough-at-least, target-above-half-strength,
target-is-Shaken-or-Fatigued, target-most-models-within-1"-of-terrain, re-roll-successful-5-6-saves (effect).

## Acceptance

Each rule has a `*RuleIntegrationTests` case and a control; `--validate-rules` OK; `--rule-coverage` on a
scratch 3.6 import drops every name above to 0.

## Code anchors (survey 2026-10-29)

- `CoreRuleCatalog.Rending` / `.Bane` / `.Lacerate` (re-roll-6 saves) / `.Blast` (ignores cover) as templates;
  `Effect.PerHitSaveModifier`, `RerollSink`, `Condition` catalogue in `Rules/Definitions/`; `StrafingStage`
  for Crossing Strike's shape. See the appended survey rows below.

### Survey rows (code survey 2026-10-29)

- Templates: Rending `CoreRuleCatalog.cs:754-769` (per-hit AP on 6s), Bane :793 / Lacerate :814 (re-roll
  defense 6s), Shred :1222 (`AddExtraWound(OnRollValue: 1)` on `Shooting_OnSaveRollComplete`), Unstoppable :735
  (`IgnoreRule("Regeneration")`), Blast (ignores cover via `SightRuleQueries.IgnoresCover :35`);
  `Rules/Dispatch/RerollSink.cs`, `MainUnitActionStage/PerHitApSplitter.cs`; Strafing (`StrafingRules.cs:18`,
  `StrafingStage`) as the mid-move attack shape for Crossing Strike. Predecessor supplement definitions:
  "Ignores Cover", "Ignores Regeneration" (+ variants) in `GdfRuleSupplement.json`.

## Notes

- 2026-10-29: **Landed.** Engine commit `3b92c54e` (submodule, `feature/opr-3.6`); the superproject commit bumps it
  with `GdfRuleSupplement.json`, the app tests, `docs/EngineNotes.md` and this ledger.
  - **Texts.** All 18 names read from the 3.6.0 snapshots' `specialRules` (identical across books). Unwieldy is
    "Strikes last when charging." (6 books: Dwarf Guilds, Alien Hives, High Elf Fleets, Eternal Wardens, Ogres,
    Dwarves) - the existing #197 P20 `StrikeLast`, now printed on WEAPONS. Crossing Strike's text is word for word
    3.5 Strafing's. Every definition carries the 3.6 text verbatim (ASCII).
  - **Engine vocabulary (new JSON kinds).** Conditions: `targetOverHalfWeaponRange`,
    `targetMostModelsDefenseInRange {minDefense, maxDefense}`, `targetAboveHalfStrength`, `targetShakenOrFatigued`,
    `targetMostModelsWithinInchesOfTerrain {distanceInches}`. Effects: `ignorePositiveDefenseModifiers`,
    `countsAsWoundsTaken {onRollValue = 1, woundsPerDie = 2}`. Reused, not rebuilt (D1): the "re-roll successful 5-6"
    effect is `reroll Save onUnmodifiedValue minValue 5`, and "most models Tough(3)+" is `targetMajorityHasTough 3`.
  - **Engine plumbing.** Pierce: `RollToHitStage.WholeAttackSaveModifier` drops positive Save deltas when the op is
    present and flags `RollToHitResults.IgnoresPositiveDefenseModifiers`, which `DetermineSaveRollsNeededStage` reads
    to drop cover's +1 and positive granted Defense buffs (`GrantedRollModifiers.ConsumeNet/PeekNet(ignorePositive)`);
    `ShootingForecast` and the AI's `CombatMath` share the same fold (D3). Scourge: `AssignWoundsStage` banks
    `AddCountedWoundsTaken` on the struck unit as a `TokenType.CountedWoundsTaken` magnitude (`CountedWounds`);
    `DetermineMeleeWinnerStage` and the wound-driven morale check (`ResolveRangedMoraleStage`,
    `ResolveStrafeMoraleStage`, `MoraleUtilities.ResolveWoundDrivenMorale(..., countedWounds)`) take it; a new
    `IUnitExtensions.GetIsAtHalfStrength(unit, countedWounds)` allocates it virtually; `ReconcileEndOfActivationStage`
    sweeps it off every unit (D4). Unwieldy: `DetermineStrikeOrderStage` now fans the charger out over its melee
    weapons (`ActorWithMeleeWeapons`), the mirror of the defender's Counter fan-out (D5). `HeroStatRules.
    GetModelDefense` (per-model Defense, joined hero included) backs the Defense gate. `RuleFireLint`: the two new
    ops are mapped as consumed, the Defense and Shaken/Fatigued gates are seeded, and a Tough-majority defender was
    added on the save side (Obliterate is the first save-side Tough gate - the lint failed it until then).
    Presentation: the re-roll beat says "Defense Re-roll" (was "Bane Re-roll" for every re-roll rule) and the cover
    log names the ignoring rule (was hard-coded "(Blast)").
  - **Data.** `GdfRuleSupplement.json`: 19 new definitions (Anti-Cover, Anti-Cover when Shooting, Anti-Cover Aura,
    Anti-Cover when Shooting Aura, Anti-Regen, Smash, Pierce, Fracture, Fragment, Puncture, Obliterate, Decimate,
    Demolish, Purge, Reap, Scourge, Unwieldy, Imprecise, Crossing Strike) and Destructive / Disintegrate re-authored
    in place to 3.6. The AoF file defines none of these names (it inherits them). Old "Ignores Cover / Ignores
    Regeneration" definitions left for #436 (D7). `.fdgbook` files untouched (#438).
  - **Tests.** Engine (new): `WeaponRules36SaveRuleIntegrationTests` (real `AssignWoundsStage`, histogram mode),
    `WeaponRules36ApRuleIntegrationTests` (real `RollToHitStage` -> `DetermineSaveRollsNeededStage`, `CoverCheckStage`,
    `DetermineHitRollStage`), `ScourgeRuleIntegrationTests` (save -> token, real `DetermineMeleeWinnerStage`, real
    `ResolveRangedMoraleStage`, real `ReconcileEndOfActivationStage` sweep, virtual allocation),
    `CrossingStrikeRuleIntegrationTests` (real `StrafingStage`, incl. a Scourge strike feeding the strafe morale test),
    `WeaponRules36ConditionTests` (JSON round-trips, edges), +2 weapon-scoped cases in `UnwieldyRuleIntegrationTests`;
    shapes shared via `WeaponRules36Definitions`. App (new): `WeaponRules36ShippedDataTests` (each shipped definition
    equals the tested shape entry for entry, with the 3.6 text; Crossing Strike = Strafing's ability and is
    strafe-only; Anti-Cover auras; AoF inherits). `RuleSupplementLintTests` gained the 19 new names automatically.
  - **Negative controls run by hand** (each reverted): over-half gate forced true fails 6 tests; the Pierce fold
    disabled fails 1; above-half gate forced true fails 4; counted wounds left out of the melee result fails 1, out of
    the morale check fails 3; the activation-end sweep removed fails 1; the charger's weapon carriers removed fails 1;
    a wrong `minValue` in the shipped Fragment fails its shape test.
  - **Counts.** Engine 3513 -> 3597 passed (1 skipped before and after). FdgRaylib.Tests 3075 -> 3118.
  - **Verification.** Engine suite green; FdgRaylib.Tests green; full `dotnet build` 0 errors / 0 warnings;
    headless smoke `printf "2\n2\n" | ... --headless` exit 0 ("Game result: outcome=Tie winner=none rounds=4
    scores=[0, 0]"); `--validate-rules GdfRuleSupplement.json AofRuleSupplement.json` "OK: 356 definitions, no
    problems." (each AofBookOverrides file on top also OK); `--rule-coverage FdgRaylib/Assets/Books` "Total
    references: 22079 / Dead: 36 (36 no-definition across 2 names)" (unchanged - the Counter-Attack family, #438).
  - **Acceptance (scratch 3.6 import, outside the repo).** Battle Brothers + Human Defense Force + High Elf Fleets
    (GDF supplement) + Dwarves (GDF + AoF supplements): before (HEAD supplements) "Total references: 1882 / Dead: 440
    (440 no-definition across 25 names)" with Anti-Cover 72, Fracture 62, Smash 24, Scourge 9, Anti-Regen 5,
    Unwieldy 3, Crossing Strike 1; after "Total references: 1882 / Dead: 264 (264 no-definition across 18 names)",
    none of this item's names left. Also all 90 snapshot entries (88 books + the 2 empty parents): before "Total
    references: 30839 / Dead: 6603 (6603 no-definition across 156 names)" (Anti-Cover 850, Smash 132, Pierce 116,
    Scourge 107, Anti-Regen 94, Fracture 85, Purge 78, Demolish 60, Fragment 54, Reap 52, Decimate 44, Obliterate 43,
    Puncture 41, Crossing Strike 32, Unwieldy 17, Imprecise 9, the three Anti-Cover variants 2 each); after "Total
    references: 30839 / Dead: 4783 (4783 no-definition across 137 names)" with none of the 18 names (nor the
    Anti-Cover variants) left. The one related name still dead is "Unwieldy on its weapons in melee" (2), the grant
    target of the reworded Unwieldy Debuff - handed to #434/#435 (D7).
  - **Deferred / handed on, named.** (1) D8: spell hits "with Smash / Pierce" never get the over-half facet (the
    spell's synthetic weapon has range 0) - #439. (2) The AI's `CombatMath` and the Combat Calculator do not model
    Scourge's counted wounds or either Unwieldy (pre-existing for the unit-scoped one) - #442. (3) "Unwieldy on its
    weapons in melee" + the Unwieldy Debuff reword - #434/#435. (4) The Strafing catalog entry, its UI strings
    ("Choosing a Strafing Target", "carries a Strafing weapon") and the old Ignores Cover / Ignores Regeneration
    definitions - #436. (5) `WeaponEffectAssigner.CarriesStrafing` keys the aerial-bomb effect set on the name
    "Strafing"; 3.6 bombs say "Crossing Strike" - #438 (effect-set keywords). (6) #419 rewrites the melee result; it
    must keep reading `CountedWounds` (noted there). No GUI resolver changed (the targeting overlays already read
    `SightRuleQueries.CoverIgnoreSource`, which now names Anti-Cover / Demolish / Pierce), so nothing awaits GUI
    hand-verify beyond a glance at the save-beat chips (Pierce's "no +Def" chip, the "Defense Re-roll" beat).

### Ledger

| Rule | Definition | Engine test (case + control) |
|------|------------|------------------------------|
| Anti-Cover (+ when Shooting, Aura, when Shooting Aura) | `GdfRuleSupplement.json` | `WeaponRules36ApRuleIntegrationTests.TheCoverIgnorers_DropTheCoverBonus` (control inside) |
| Anti-Regen | `GdfRuleSupplement.json` | `WeaponRules36SaveRuleIntegrationTests.TheRegenerationIgnorers_LandEveryWound` / `WithoutAnIgnorer_...` |
| Smash | `GdfRuleSupplement.json` | `...Save...Smash_At13InchesOfA24InchGun_RerollsTheSix` / `Smash_At11Inches..._RerollsNothing`, `Smash_InMelee_...` |
| Pierce | `GdfRuleSupplement.json` | `...Ap...Pierce_OverHalfRange_GetsApPlusOne` / `Pierce_InsideHalfRange_NoExtraAp`, `Pierce_IgnoresTheTargetsOwnDefenseBonus`, `Pierce_IgnoresCoverAndGrantedDefenseBuffs`, `Pierce_KeepsNegativeModifiers` |
| Fracture | `GdfRuleSupplement.json` | `...Ap...Fracture_OnANaturalSix_HitsGetApPlusTwo` / `Fracture_FromAFatiguedStriker_AddsNothing`, `Fracture_OffASix_AddsNothing` |
| Fragment | `GdfRuleSupplement.json` | `...Save...Fragment_VersusDefense5_...` / `Fragment_VersusDefense4_...`, `Fragment_OnlyRerollsSuccessfulFives` |
| Puncture | `GdfRuleSupplement.json` | `...Ap...Puncture_VersusDefense3_GetsApPlusTwo` / `Puncture_VersusDefense4_NoExtraAp`; Regen-ignore case |
| Obliterate | `GdfRuleSupplement.json` | `...Save...Obliterate_VersusToughThree_...` / `Obliterate_VersusToughOne_...`; Regen-ignore case |
| Decimate | `GdfRuleSupplement.json` | `...Save...Decimate_VersusAFullStrengthUnit_...` / `Decimate_VersusAUnitAtHalfStrength_...`, `Decimate_VersusASingleToughModel_ReadsItsToughValue` |
| Destructive (re-authored) | `GdfRuleSupplement.json` | `...Ap...Destructive_AboveHalfStrength_GetsApPlusOne` / `Destructive_AtHalfStrength_NoExtraAp`, `Destructive_NoLongerRendsOnSixes` |
| Disintegrate (re-authored) | `GdfRuleSupplement.json` | `...Ap...Disintegrate_VersusToughThree_...` / `Disintegrate_VersusToughOne_...`; Regen-ignore case |
| Demolish | `GdfRuleSupplement.json` | `...Ap...Demolish_VersusMostModelsNearTerrain_...` / `Demolish_VersusATargetInTheOpen_...`; cover case |
| Purge | `GdfRuleSupplement.json` | `...Ap...Purge_VersusAShakenOrFatiguedUnit_GetsApPlusTwo` (x2) / `Purge_VersusAFreshUnit_NoExtraAp` |
| Reap | `GdfRuleSupplement.json` | `...Save...Reap_VersusAShakenOrFatiguedUnit_...` (x2) / `Reap_VersusAFreshUnit_RerollsNothing` |
| Scourge | `GdfRuleSupplement.json` | `ScourgeRuleIntegrationTests` (16: save -> 4 counted, melee loser flips, morale without real wounds, sweep, histogram) |
| Unwieldy | `GdfRuleSupplement.json` | `UnwieldyRuleIntegrationTests.AChargerWhoseMeleeWeaponIsUnwieldy_...` / `AnUnwieldyWeaponOnTheDefender_DoesNothing` |
| Imprecise | `GdfRuleSupplement.json` | `...Ap...Imprecise_IsMinusOneToHit` (control inside) |
| Crossing Strike | `GdfRuleSupplement.json` | `CrossingStrikeRuleIntegrationTests` (7, incl. `Declined_NoAttack`, `ThePlainStrike_SameDice_NoMoraleTest`) |

App side, every row: `WeaponRules36ShippedDataTests` (shape + 3.6 text) and `RuleSupplementLintTests.EverySupplementRuleFires`.

- 2026-10-29 (from #418): Fatigue's "those hits don't count as rolls of 6 for special rules" is now enforced
  once, on the hit context: `UnmodifiedRollEquals`, `AddExtraHit`, `SelfWoundOnUnmodifiedRoll`,
  `PerHitSaveModifier` and `PerHitApSplitter` read `IHasUnmodifiedHitRolls.SpecialRuleHitRolls`. Fracture
  (and any other hit-face rule) built on those primitives inherits it with no extra work. A NEW primitive that
  reads hit-roll faces must read `SpecialRuleHitRolls`, never `UnmodifiedHitRolls`. Defense-roll rules (the
  5-6 re-roll family, Scourge) read the save histogram and are unaffected by Fatigue.
- 2026-10-29: filed with the plan.

## Decisions

- **D1 - two of the requested primitives already existed (2026-10-29).** "re-roll-successful-5-6-saves" is
  `Effect.Reroll(Save, RerollCondition.OnUnmodifiedValue(MinValue: 5))` (#197's Boost band): `AssignWoundsStage`
  re-rolls only the SUCCESSFUL save groups, so a natural 5 that already failed (Defense 6+, or under AP) is never
  re-rolled - exactly "successful unmodified results of 5-6" (pinned: `Fragment_OnlyRerollsSuccessfulFives`). A
  general face set was not added: no 3.6 rule names a non-contiguous set. "target-most-models-Tough-at-least" is
  `Condition.TargetMajorityHasTough` (#377), strict majority of living models. No duplicate kinds were minted.
- **D2 - "this weapon's range" (2026-10-29) - OWNER REVIEW.** `TargetOverHalfWeaponRange` compares the shooting
  distance (closest living models, base to base - the distance every other range gate reads) with HALF the weapon's
  PRINTED range, strictly over. Not the effective range after modifiers (Increased Shooting Range +6", a target's
  Aircraft -12"): those change what may be targeted, not the weapon's profile. False in melee (an Assault weapon
  swung in contact is never "over half its range away"), for a range-0 weapon, and for a weaponless (granted)
  copy. Alternative: halve the effective range from `RangeRuleQueries.EffectiveRange` - one call in the condition.
- **D3 - what Pierce's "positive Defense modifiers" are (2026-10-29) - OWNER REVIEW.** Ignored: the target's own
  positive Save deltas (every one in the corpus is a Subject-seat `rollModifier Save +X` at hit-roll-complete -
  Shielded, Grounded Protection, ...), cover's +1, and positive granted "+X to Defense rolls" buffs. Kept: every
  negative modifier, and AP reduction (Fortified "AP(-1)"), which modifies the attacker's AP rather than the target's
  Defense. An ignored one-shot "+1 to Defense once" is still SPENT by the attack it was ignored on (this roll is its
  "next time"); the other reading (it carries over) needs payload-precise token removal. Pierce also carries an
  `ignoreCover` entry purely so the targeting overlays, the shooting forecast, the AI and the calculator (which ask
  `SightRuleQueries.IgnoresCover`) price it the way the save stage resolves it.
- **D4 - Scourge's counted wounds (2026-10-29).** Banked as a magnitude token on the STRUCK unit (the save-roll stage
  cannot reach the action context, and the charger's swings and the strike-back run under different contexts),
  read-and-removed by the two checks the text names, swept off every unit at the end of each activation (the text's
  scope is "this activation"; a wipe-out skips the melee result step). Melee: added to what the opponent dealt -
  Fear(X)'s polarity, so #419's "wounds taken" rewrite keeps the same sign. Shooting: counted wounds count as wounds
  TAKEN (they alone can trigger the test, per the item's acceptance) and are allocated virtually for the
  half-strength check in the mandatory order (wounded models first, rank and file in order, joined hero last); a
  model only counts as gone when the counted wounds cover all it has left. Strafe / Crossing Strike morale reads
  them too ("as if it was shooting"). Re-rolled save dice (Bane family) do not add 1s - the Shred precedent reads the
  original histogram the same way.
- **D5 - Unwieldy on a weapon swaps the whole melee (2026-10-29) - OWNER REVIEW.** 3.6 Unwieldy is a weapon rule. The
  engine orders UNITS, not weapons, so one Unwieldy melee weapon in the charging unit lets the charged unit strike
  first - the same approximation weapon-scoped Counter has always had (one Counter spear = the whole unit strikes
  first). The exact reading (the Unwieldy weapons swing after the defender, the rest before) would split the
  charger's swing around the strike-back - a melee pipeline change well beyond this item. In the 3.6 books the axes
  and great weapons that carry it are carried by whole units, so the difference is mostly theoretical.
- **D6 - Crossing Strike is Strafing renamed, so no new stage (2026-10-29).** The 3.6 text is identical to 3.5
  Strafing's, and Strafing's whole shape is still in the engine (`StrafingStage`, `StrafingRules` detecting the
  restriction by EFFECT, so a renamed copy is restricted too, `ResolveStrafeMoraleStage`). The definition is the
  catalog's activated ability under the new name, in the GDF supplement (Crossing Strike is not a core name). No
  fly-over permission, as before: carriers need Flying or Aircraft. Not a design fork - adapting it was data only.
- **D7 - the variant names (2026-10-29).** 3.6 books still reference three variants of the renamed rule - "Anti-Cover
  Aura" (Eternal Dynasty), "Anti-Cover when Shooting" and "Anti-Cover when Shooting Aura" (Dwarf Guilds) - so they
  were authored as the renames of the Ignores Cover variants (same shapes). No 3.6 book references any "Ignores
  Cover" / "Ignores Regeneration" name or an Anti-Regen variant (census + both coverage lists), so the old
  definitions stay for #436 to remove. "Unwieldy on its weapons in melee" (the grant target of the reworded Unwieldy
  Debuff and High Elf Fleets' Creator of Illusions spell) is not one of this item's names and only makes sense together with the
  Debuff's reword, so it is handed to #434/#435.
- **D8 - spell hits "with Smash / Pierce" (2026-10-29) - OWNER NOTE, handed to #439.** Seventeen 3.6 spells name Smash or
  Pierce, most as "takes N hits with AP(1) and Smash/Pierce" (Cerebral Trauma, Dark Wound, Godly Smash, ...). `CastSpellStage`'s synthetic spell weapon has range
  0, so the over-half-range facet never fires for them (Pierce's positive-modifier facet does). If the owner reads
  "this weapon's range" as the spell's range, #439 can give the synthetic weapon the spell's range.
- **D9 - presentation touch-ups (2026-10-29).** The save re-roll beat was labelled "Bane Re-roll" for every re-roll
  rule; with five new ones it now reads "Defense Re-roll". The cover log hard-coded "(Blast)"; it now names the rule
  that ignored the cover. Pierce's ignored Defense bonus shows no chip, and a "Pierce: no +Def" chip says why.

## Outcome

Done 2026-10-29 on `feature/opr-3.6`. Every OPR 3.6 weapon rule in the item (Anti-Cover, Anti-Regen, Smash, Pierce,
Fracture, Fragment, Puncture, Obliterate, Decimate, Destructive, Disintegrate, Demolish, Purge, Reap, Scourge,
Unwieldy, Imprecise, Crossing Strike), plus the three Anti-Cover variants the books still name, has a validated
definition in `GdfRuleSupplement.json`, the 3.6 text, and engine + app tests; a scratch import of all 88 3.6 books
leaves none of them undefined. The engine gained five target conditions, Pierce's positive-modifier ignore and
Scourge's counted-wounds feed, all named for reuse. Engine `3b92c54e`. Open: D2, D3, D5 and D8 for the owner;
hand-offs to #419, #434/#435, #436, #438, #439, #442 recorded in Notes.

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
