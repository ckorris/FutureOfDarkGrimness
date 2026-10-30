# 435 — AoF supplement sweep to 3.6 + book overrides + AoF-specific core rules

**Status**: todo
**Related**: #415, #375/#376 (the 3.5 AoF rules work), #378, #381 (Retreating Strike), #422 (Brace), #423 (Parry), #431-#434, #438
**Branch**: `feature/opr-3.6`. Model/effort: Opus 5.5 / high. Run after #434 so shared names are already done.

## Goal

Every rule name a 3.6.0 AoF book references resolves to a 3.6-correct definition, and the AoF-only core
differences are implemented. Inputs: census AoF lists (149 changed, 82 new, 67 removed) and a scratch import
+ `--rule-coverage` of the 40 AoF books against GDF + AoF supplements + overrides.

AoF-specific core (AoF Core Rules v3.6.0, special rules reference):
- Sergeant: "This model gets AP(+1) when attacking" (read the full line in the AoF text; 3.5 Sergeant was a
  hero-style morale rule). Great Sergeant likewise (read).
- Banner: "This model and its unit get +1 to Morale Test rolls triggered by shooting/melee" (3.5: all morale
  tests). Great Banner likewise.
- Musician: "This model and its unit move +1" when using move actions". Great Musician likewise.
- Parry(X) on Hand Weapons (data; #423 implements the rule), Brace(X) in the AoF reference (#422), "Brace in
  Melee": "This model gets Brace(1) on its weapons in melee" (+ "Counter in Melee Aura").
- Points levels (#430).

AoF faction rules to re-author (3.6 texts in the census): Angelic Blessing (Knightborn shape), Vinci Tech /
Royal Warrior / Royal Legion / Wave-Step / Wild Veil / Vale Oath / Ossified / Buccaneer / Lucky / Cursed
Undead / Empyrean Spirit / Destroyer / Bestial / Warden / Drakesworn / Ethereal / Unmovable / Steadfast /
Grounded Protection / Grounded Speed / Piercing Feat / Precision Feat / Reckless Piercing / Traversal /
Retreating Strike (re-check text vs #381's ruling) / Melee Evasion (kept in AoF) / Guarded (dropped in AoF,
kept in GDF) / Shred in Melee (dropped) / Indirect when Shooting (dropped) / Courageous, Reach Hunt,
Reinforced, Precision Hunter (dropped). The Enhanced variants come from #433; the range-conditional shapes
from #432; the weapon rules from #431.

Overrides: `FdgRaylib/Assets/Books/AofBookOverrides/<OPR book name>.json` are keyed by book name - rename
`Rift Daemons of Change/Lust/Plague/War.json` to `Daemons of X.json` (the 3.6 names) and re-check each
override against the 3.6 book (the C9 redefinitions may no longer be needed).

"Done" = `--rule-coverage` over a scratch import of the 40 AoF 3.6.0 books reports Dead: 0 (or a ledger
entry per deferral), `--validate-rules` passes for GDF + AoF + each override alone and merged, and the
AoF core deltas have integration tests.

## Acceptance

Ledger table in this file (name -> definition/test/deferred); Sergeant/Banner/Musician tests; a Chivalrous
Kingdoms (Fearless removed, Spell Resistance added) and a Vampiric Undead headless smoke exit 0.

## Code anchors (survey 2026-10-29)

- `FdgRaylib/Assets/Books/AofRuleSupplement.json` (86 definitions), `AofBookOverrides/`, `scripts/bake-aof-books.sh`,
  `GdfRuleSupplement.json` Banner/Musician/Sergeant entries. See the appended survey rows below.

### Survey rows (code survey 2026-10-29)

- `FdgRaylib/Assets/Books/AofRuleSupplement.json` (86 definitions; "Counter in Melee" :2332 carries the old
  impact facet, #422). Overrides `AofBookOverrides/` (9 files by OPR book name: Change Disciples, Chivalrous
  Kingdoms, Dark Elves, Giant Tribes Lust Disciples, Lust Disciples, Plague Disciples, Rift Daemons of Lust,
  Vampiric Undead, War Disciples). Merge order `FdgRaylib/Import/RuleSupplementSet.cs:13-22`.
- Tests: `RuleSupplementLintTests.cs:39-48`, `BundledBookRulebookTests.cs:80-90` (Change Disciples / Musician
  collision), `GroundedAofShippedDataTests.cs:40`, `SergeantShippedDataTests.cs:32, :94, :128`.
- AoF faction effect-set table `ArmyBuilding/WeaponEffectAssigner.cs:255-296` (by book name; the Daemons
  renames need new rows).

## Notes

- 2026-10-30 (from #426): Drakesworn is DONE - its 3.6 text is Vanguard's word for word ("...over 9" away from
  non-Vanguard enemy units"), so `AofRuleSupplement.json` now carries Vanguard's exact effect (`repositionOnDeploy`,
  `maxInches` 9, `enemyClearanceInches` 9, `exemptEnemyRule` "Vanguard", most models), pinned by
  `DeploymentDistancesShippedDataTests.Drakesworn_IsThe36VanguardPlacement`. Strike it from your list. The bundled
  Volcanic Dwarves book embeds the old 9" move until #438 re-bakes.
- 2026-10-30 (orchestrator): #429 found Mind Control gained a 3.6 clause ("Units may only be moved once per round in
  this way.") and Spell Resistance is new with no definition; #434 owns the GDF side. Check the AoF 3.6.0 snapshots for
  the same names (and Casting Debuff, which no GDF book names any more) and share #434's definitions.
- 2026-10-29 (from #432): the range-conditional family is DONE for AoF: Lucky, Destroyer, Vinci Tech (+ helpers),
  Empyrean Spirit, Angelic Blessing, Wild Veil and Royal Legion re-authored in `AofRuleSupplement.json`; the shared
  names (Brutal, Ferocious, Havocbound, Piercing Warrior, Violent, Warbound, Changebound, Screened, Guarded, Sturdy,
  Warding, Shrouded, Hold the Line, Valiant, Highborn, Extra Reach, Lustbound, Scurry, Shadowborn) are inherited from
  the GDF file (Shadowborn moved there). Every AoF Boost is deleted. `AofBookOverrides`: Melee Shrouding (Chivalrous
  Kingdoms, Dark Elves), Lustbound + Lustbound Boost (Giant Tribes Lust Disciples, Rift Daemons of Lust) and Warbound
  Boost (War Disciples) removed - 3.6 AoF prints the common texts - and the three files left empty were deleted; the
  remaining override files still use 3.5 book names ("Rift Daemons of Lust" is "Daemons of Lust" in 3.6), so their
  Mind Control / Versatile Attack entries do not reach a 3.6 import until you rename them. Versatile Defense (AoF has
  it too) is yours, see #434's note; so are Ossified and Warden (3.6: unconditional AP(-1), Fortified's shape - they
  still carry the 3.5 `attackedFromOverInches(9)` gate).
- 2026-10-29 (from #431): the weapon-rule family lives in `GdfRuleSupplement.json` and the AoF file inherits it
  (no AoF copies - `WeaponRules36ShippedDataTests` pins that). AoF-relevant leftovers: "Unwieldy on its weapons in
  melee" (Ogres' Unwieldy Debuff; GDF High Elf Fleets has the same Debuff and a spell granting it) - see #434's note; whoever lands first takes it.
  New kinds available: Conditions: `targetOverHalfWeaponRange` (shooting distance > half the FIRING weapon's printed range; false in melee,
  for range 0, or with no weapon), `targetMostModelsDefenseInRange {minDefense, maxDefense}` (strict majority of the
  target's living models, per-model Defense incl. a joined hero), `targetAboveHalfStrength` (complement of the morale
  "half or less"), `targetShakenOrFatigued`, `targetMostModelsWithinInchesOfTerrain {distanceInches}` (the target-side
  twin of `mostModelsWithinInchesOfTerrain`). Effects: `ignorePositiveDefenseModifiers` (author at
  `Shooting_OnHitRollComplete`), `countsAsWoundsTaken {onRollValue, woundsPerDie}` (at `Shooting_OnSaveRollComplete`;
  counted, not dealt - melee result + shooting morale trigger). Reuse rather than re-mint: "re-roll successful 5-6" is
  `reroll Save onUnmodifiedValue minValue 5`, "most models Tough(X)+" is `targetMajorityHasTough`.
- 2026-10-29 (from #422): "Brace in Melee" is DONE in `AofRuleSupplement.json` (weapon scope, Brace(1) as a literal,
  the Fatigue gate, 3.6 text), and "Counter in Melee" left the AoF file for the GDF one (one definition serves
  both; the old copy's `reduceImpactDicePerModel` facet is gone). No 3.6 book names a "Brace in Melee Aura" or
  "Buff" (grepped both snapshot folders). "Counter in Melee Aura" is in the GDF file. Nothing left here for them.
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
