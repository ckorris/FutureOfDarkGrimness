# 434 — GDF supplement sweep to 3.6

**Status**: todo
**Related**: #415, #431/#432/#433 (families, land first), #197 (the 3.5 sweep this mirrors), #436 (drops + text parity), #438 (bake)
**Branch**: `feature/opr-3.6`. Model/effort: Opus 5.5 / high. Work book-family by book-family and commit per batch.

## Goal

After the family items, every rule name a 3.6.0 GDF book references resolves to a definition that matches
its 3.6 text, including the new book Voidspace Stalkers. Inputs: the census's "changed definitions" (144),
"new definitions" and "NEW names" lists for GDF, and a fresh scratch import + `--rule-coverage` (plan
section 3) as the burn-down list. Highlights not covered by #431-#433:

- Phrasing "gets X on its weapons when shooting / in melee / when attacking": grants are weapon-scoped now
  (Bane/Shred/Rending/Unstoppable/Indirect/Thrust/Counter "in Melee"/"when Shooting" + Aura/Buff/Mark
  variants). The 3.5 definitions granted at unit scope; re-author on the weapon-scoped grant path and make
  sure the spell parser (#439) emits the same names.
- Markers -> tokens wording (Defensive/Fortified/Piercing/Precision Frenzy and Growth): text only.
- Bounding / Dash / Nimble: "move all models [...] by up to D3+1"" (3.5: "place ... anywhere fully within") -
  a real move (terrain, coherency) instead of a placement; Rapid Blink / Wolfborn likewise if changed.
- Highborn: "+4"/+2" range when shooting/charging" (#432 primitive); Lustbound / Scurry / Agile: "+1" Advance,
  +2" Rush/Charge" (was +2"/+2" or a range bonus).
- Entrenched: "most models [...] haven't moved [...] over 12" away get -1" (was 9"/-2); Guardian: unconditional
  AP(-1) on hits taken; Grounded Precision: "If most models in this unit are within 1" of terrain, this model
  gets +1 to hit".
- Hazardous: "takes one wound, or X wounds if this weapon has Blast(X)/Deadly(X)"; Regenerative Strength: deals
  wounds on 4+ (read text); Mind Control: "Units may only be moved once per round in this way"; Spawn / Split:
  no objectives the round placed (#426's token).
- Precision/Piercing Tag: "places X markers on one unit or one marker on X units; only one marker is removed
  when attacking"; Precision/Piercing Target: "markers on X units; marked units get +1 to hit when attacked"
  (patch notes - read the 3.6 texts).
- Bravery Aura / Bravery Buff (replace Courage Aura / Buff): "+1 to Morale Test rolls"; Unique Hero (#430);
  Spell Resistance (+Aura): spell wounds ignored on 2+, others on 6+; Spell Drain (#429); Dodge: "If most
  models [...] as long as they aren't Fatigued, enemy units get -1 to hit rolls in melee"; Grounded Defense /
  Grounded Evasion; Predator (+Aura); Quick Throw (#425); Thrust in Melee (+Buff); Counter in Melee (+Aura/Buff,
  #422); Piercing/Precision Charge, Piercing Marksman (+Aura), Precision Marksman, Precision Fighter;
  Versatile Expert: "When this unit is activated, pick one effect: until the end of the activation all models
  with this rule [...] either get AP(+1) when attacking, or +1 to hit rolls" (an activation-start pick - the
  #408 activation-start choice path); Rapid Rush Buff; Infiltrating Precision Strike; Vicious, Violent,
  Valiant, Warding, Brutal, Shrouded, Shadowborn, Extra Reach, Piercing Warrior (#432 shapes).
- Vanguard / Fanatic enemy clause (#426). Flying/Unstoppable/etc. core wording (#427).

"Done" = `--rule-coverage` over a scratch import of all 48 GDF 3.6.0 books (against the updated
`GdfRuleSupplement.json`) reports Dead: 0, every re-authored definition has a test or shares a tested shape
(say which in a ledger table in this file), and `--validate-rules GdfRuleSupplement.json` passes.

## Acceptance

The ledger table in this file lists every GDF name with: definition location, test, or "deferred: reason".
Deferred names also get a player-visible diagnostic (#168 path).

## Code anchors (survey 2026-10-29)

- `FdgRaylib/Assets/Books/GdfRuleSupplement.json` (251 definitions today), `Rules/Serialization/` JSON kinds,
  `Program.cs --validate-rules` / `--rule-coverage`. See the appended survey rows below.

### Survey rows (code survey 2026-10-29)

- Phrase rules the spells/grants name verbatim (`CoreRuleCatalog.cs`): :918 "Bane in melee", :965-967 "+1 to hit
  rolls in melee", :979-981 "+1 to hit rolls when shooting", :993-995 "AP(+1) in melee", :1007-1009 "AP(1) when
  shooting", :1021-1023 "Bane when attacking", :1040-1042 "Shred when attacking", :1061 "Slayer", :1077-1079
  "+6" range when shooting" - 3.6 rephrases these "on its weapons ..."; rename/alias in step with #439.
- `OprBookImporter.cs:355-361` `AmbiguousRuleNames` keyed by (book name, rule): Dark / Dark Prime Brothers
  "Darkborn", Lust / War Disciples "Dangerous Terrain Debuff" - re-check against the 3.6 books.
- The ~45 `FutureOfDarkGrimness/Tests/*ShippedDataTests.cs` pin 3.5 rule counts and unit names (see #440).

## Notes

- 2026-10-30 (from #447): Ravage, when you re-author it (3.6 book rule in 13 GDF + 9 AoF 3.6.0 books: "Once per round,
  when it's this model's turn to strike in melee, roll X dice before attacking, unless this unit is Fatigued. For each
  6+ the target takes one wound."), has the per-model gaps #447 fixed for Impact: `ResolveRavageWoundsStage` evaluates
  the Actor without its models, so a joined hero's Ravage (relocated onto its model) never fires; and
  `Effect.DealAutoWounds` counts every living model for a unit-attached copy (a joined hero included) and counts a
  weapon-carried copy by definition rather than by attachment (a resumed game, which rehydrates one attachment per
  weapon, would multiply it - #422 D3). Template: `Effect.ChargeImpactHits.CountCarriers` and
  `ResolveImpactHitsStage.ChargerParticipant`. Not changed in #447; the impact stage now narrates only impact ops
  (#447 D4), so a hero's Ravage no longer shows up there either.
- 2026-10-30 (orchestrator, relaying #429's findings that were recorded on #436 only): Mind Control's 3.6 text adds
  "Units may only be moved once per round in this way." (a behaviour change - needs a per-round moved-by-Mind-Control
  gate, not just text); Spell Resistance is NEW in 3.6 with no definition anywhere; Casting Debuff appears in no
  3.6.0 book (drop candidate for #436). The first two belong to this sweep.
- 2026-10-29 (from #432): the range-conditional family is DONE in `GdfRuleSupplement.json` (Devout, Ferocious, Brutal,
  Havocbound, Piercing Warrior, Infected, Warbound, Violent, Versatile Warrior + helpers / Aura / Buff, Watchborn,
  Changebound, Machine-Fog, Screened, Guarded, Sturdy, Knightborn, Warding, Darkborn, Shadowborn (moved here from the
  AoF file), Shrouded, Hive Bond, Hold the Line, Valiant, Highborn, Extra Reach, Lustbound, Scurry), and every `X Boost`
  / `X Boost Aura` / `X Boost Buff` is deleted. Hive Bond / Hold the Line now carry the most-models gate #428 handed you
  (its note 5) - nothing left there. Left for you: (1) Versatile Defense - 3.6 makes the pick per attack ("you may pick
  a different effect each time the unit is attacked"), a defender decision inside the attack; its helpers still use
  the 3.5 `attackedFromOverInches(9)` - when re-authoring, use `attackedFromOver {9, 6}` (#432 D8). (2) The shooting-only
  "over 9"" rules (Precision Marksman, Piercing Marksman, Targeting Visor's reworded text) need no new primitive:
  `distanceGreaterThan 9` with `not(isMelee)`, or `attackingFromOver {9, 1000}` if you prefer one vocabulary.
  (3) Versatile Reach's text (not range-conditional; numbers unchanged). (4) Primeborn and Guardian (GDF; Ossified and
  Warden on the AoF side) still carry the 3.5 `attackedFromOverInches(9)` gate, but 3.6 made them unconditional
  ("When units where most models have this rule take hits, those hits count as having AP(-1), to a min. of AP(0)" -
  Fortified's shape), so they are not range rules any more and were left to you. New kinds: conditions `attackingFromOver
  {shootInches, chargeInches}` / `attackedFromOver {...}`, effect field `rangeModifier.chargeDelta` (semantics in #432
  Notes and `Condition.cs` / `Effect.cs`).
- 2026-10-29 (from #431): the weapon-rule family is DONE in `GdfRuleSupplement.json` (Anti-Cover + its three
  variants, Anti-Regen, Smash, Pierce, Fracture, Fragment, Puncture, Obliterate, Decimate, Demolish, Purge, Reap,
  Scourge, Unwieldy, Imprecise, Crossing Strike; Destructive and Disintegrate re-authored to 3.6). Left for you: the
  reworded "Unwieldy Debuff" ("... which gets Unwieldy on its weapons in melee once") and its new grant target
  "Unwieldy on its weapons in melee" (2 dead refs in a full 3.6 scratch import; also granted by the High Elf Fleets
  Creator of Illusions spell) - a unit-scoped clone of today's "Unwieldy in melee" works, since `DetermineStrikeOrderStage`
  reads both the charger's unit and its melee weapons. New kinds available: Conditions: `targetOverHalfWeaponRange` (shooting distance > half the FIRING weapon's printed range; false in melee,
  for range 0, or with no weapon), `targetMostModelsDefenseInRange {minDefense, maxDefense}` (strict majority of the
  target's living models, per-model Defense incl. a joined hero), `targetAboveHalfStrength` (complement of the morale
  "half or less"), `targetShakenOrFatigued`, `targetMostModelsWithinInchesOfTerrain {distanceInches}` (the target-side
  twin of `mostModelsWithinInchesOfTerrain`). Effects: `ignorePositiveDefenseModifiers` (author at
  `Shooting_OnHitRollComplete`), `countsAsWoundsTaken {onRollValue, woundsPerDie}` (at `Shooting_OnSaveRollComplete`;
  counted, not dealt - melee result + shooting morale trigger). Reuse rather than re-mint: "re-roll successful 5-6" is
  `reroll Save onUnmodifiedValue minValue 5`, "most models Tough(X)+" is `targetMajorityHasTough`.
- 2026-10-29 (from #422): "Counter in Melee", "Counter in Melee Aura" and "Counter in Melee Buff" are DONE in
  `GdfRuleSupplement.json` (3.6 text; Counter in Melee moved here from the AoF file because GDF books name it too).
  The Buff grants "Counter in Melee" NextTrigger - its 3.6 text omits the usual "once (next time ...)"; owner
  review asked in #422 D4. Heavy Impact now carries Impact's Fatigue gate and its 3.6 text. Skip all four here.
- 2026-10-29 (from #428): Hive Bond and Hold the Line (GDF, `Morale_OnPreMoraleTest`, "units where all/most
  models have this rule get +1 to morale test rolls") were never on the all-models gate - they are ungated -
  so #428's switch did not touch them. The pre-morale evaluation passes no models (`MoraleUtilities.cs:88`),
  so a joined hero's own copy is invisible (no leak). The only divergence from 3.6 is a host down to one model
  beside a joined hero lacking the rule (1 of 2, still +1). Add `mostModelsHaveThisRule` when rewording them
  (and pass `HeroStatRules.LivingModels` there if a hero's own copy should count toward the majority).
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
