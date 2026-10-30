# 435 — AoF supplement sweep to 3.6 + book overrides + AoF-specific core rules

**Status**: in-progress
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

- 2026-10-30: **Batch 5 - smokes; Ethereal's "if you do" refined (engine).** The scratch smokes (below) showed the CLI
  and AI default answer to Ethereal's placement is "stay put", and the batch-2 gate (the cost's `AbilityUsed:Ethereal`
  marker) then charged -6" for a teleport that never moved anything. New catalog token `TokenType.TeleportedThisActivation`
  (invisible, activation-end clear), stamped by `TeleportStage` only when a model actually moved; Ethereal's penalty
  entries gate on it instead (D4). Tests: engine `AofSweep36RuleIntegrationTests` +1 (an accepted stand-still placement
  pays the cost but moves nothing and costs nothing), the after-teleport cases now read the flag. Negative controls:
  stamping regardless of movement fails the stand-still case; no stamp fails the three after-teleport cases.
  **Smokes** (3.6 scratch armies: the batch-4 scratch import of each snapshot, units reordered so the carriers are the
  first four - command-group / Mind Control / Great buff rules added to a base unit where the book sells them only as
  upgrades - then `--book-to-army`, `--headless --army ... --trace-rules --seed N`; all exit 0, logs under the session
  scratchpad `435/smokes/`): Chivalrous Kingdoms (no Fearless in 3.6; Men-at-Arms + Banner / Musician / Sergeant, Pledge
  Knights, Peasant Bowmen, Foot Knights; seeds 3 and 7) - "Men-at-Arms's Banner at Morale_OnPreMoraleTest/Actor: fired ->
  ApplyRollModifier" after "Melee result: ... Men-at-Arms took more and loses the melee.", "Men-at-Arms's Hand
  Weapon's Sergeant added -1 to Save rolls.", "Pledge Knights's Spell Resistance at Shooting_OnSaveRollComplete/Subject:
  fired -> IgnoreWound" / "... ignored wounds on a 6+." (and its spell arm "condition And(IsSpell, MostModelsHaveThisRule)
  not met" on weapon hits), Vale Oath evaluated at every round start; Vampiric Undead (seeds 3, 7) - "Vampire Knights's
  Cursed Undead at Shooting_OnSaveRollComplete/Subject: fired -> IgnoreWound", "Drained Archers used Mind Control before
  attacking." after "Drained Soldiers failed the morale test." and the forced move, then "Mind Control ability ...: not
  offered (cannot pay OncePerActivation)"; Daemons of Lust (the renamed book; seeds 3, 7) - "Lust Shooters's Mind Control
  ability at Activation_OnBeforeAttackAction: offered" (the GDF 3.6 definition, `oncePerRoundKey`), "Sacred Mirror's Spell
  Resistance ... fired -> IgnoreWound", default effect sets arcane-bolt / daemon-arcane-melee; Deep-Sea Elves - "Sea
  Guard's Wave-Step at Activation_OnActivationStart/Actor: fired -> RepositionModels" / "Sea Guard may move up to 3"."
  (the move form); Ghostly Undead - Ethereal offered at every action choice, "Ghost Horde held its position." (the
  default; what prompted the refinement above); Ossified Undead - "Guardians's Ossified at Shooting_OnHitRollComplete/
  Subject: fired -> ReduceArmorPenetration"; Ogres - "Ogre Warriors used Great Banner before attacking.", "... used Great
  Musician ...", "Ogre Warriors's Dual Hand Weapons's Great Sergeant added -2 to Save rolls.". Musician never traces: the
  movement budget is a read-only projection (as for Fast).
- 2026-10-30: **Batch 4 - the per-book overrides are all moot in 3.6; the 3.6 Daemons book names (engine table).** Every
  entry of the six `AofBookOverrides/*.json` re-checked against its 3.6.0 book: Change / Lust / Plague / War Disciples'
  "Versatile Attack (Piercing)" / "(Precision)" - no 3.6 AoF book names Versatile Attack (census: removed; the four books
  took Versatile Expert, whose own helpers live in the GDF file); Lust Disciples' Melee Slayer (3.6 prints the common
  text) and Shatter (not in the 3.6 book; Kingdom of Angels / Ossified Undead print the GDF 3.6 text); Rift Daemons of
  Lust's and Vampiric Undead's Mind Control (3.6 prints the common text, once-per-round clause included). Nothing is
  left to override, so the six files are deleted (`git rm`) and the folder with them (D11) - which also settles the
  rename: renaming "Rift Daemons of Lust.json" to "Daemons of Lust.json" would have made its 3.5 Mind Control (no
  once-per-round key) shadow the 3.6 one on every 3.6 import. The mechanism stays (bake script, scratch import and the
  three app tests tolerate an absent folder; `RangeConditional36ShippedDataTests` now checks). Engine:
  `WeaponEffectAssigner`'s AoF table gains "Daemons of Change / Lust / Plague / War" (the 3.6 Faction strings; a 3.6
  import of those books had no default effect sets), the "Rift Daemons of X" rows kept for the bundled 3.5 books until
  #438. Scratch import after (all 90, the same supplements): unchanged "Dead: 4066 (4066 no-definition across 93 names)";
  the 3.6 Daemons of Lust and Vampiric Undead books embed the GDF Mind Control with `oncePerRoundKey "Mind Control"`, and
  the four Daemons books carry their effect sets. Tests: engine `WeaponEffectAssignerTests` +4 (each 3.6 Daemons name
  has the Rift row's sets); app `AofSweep36ShippedDataTests` +2 (no override ships; Mind Control in the merged universe
  is the 3.6 once-per-round move), minus the 12 `RuleSupplementLintTests` cases of the deleted entries. Counts: engine
  4107 -> 4111, app 3387 -> 3377.
- 2026-10-30: **Batch 3 - the five dead AoF names, 3.6 texts (data + a catalog text).** New in the AoF file (AoF-only
  names): the spell-phrase aliases "AP(+1) on its weapons in melee" (Giant Tribes / Human Empire Elemental Form), "Bane
  on its weapons in melee" (High Elves Magical Surge), "Unstoppable on its weapons in melee" (Ossified Undead Drain
  Spirit) - each its catalog rule ("AP(+1) in melee", "Bane in melee", "Unstoppable in melee") under the 3.6 phrase,
  #434 D5 - and "Surge on their weapons" (Chivalrous Kingdoms Curse of the Huntress, a mark) = Surge's entry with the
  weapons-only `isNotSpell` gate (D8); Piercing Charge Buff (Dark Elves, Orcs) = Thrust in Melee Buff's shape granting
  Piercing Charge. 3.6 texts on shapes that already match (D1): Cursed Undead (= catalog Protected), Unmovable / Vale
  Oath (= Steadfast), Buccaneer (= Precision Marksman), Bestial (= Mischievous), Royal Warrior (= Predator, #434 D7's
  approximation - D10), Grounded Protection (+ Aura), Reckless Piercing (+ Aura; already most-models gated), Traversal,
  Good Fighter, Melee Evasion Buff, Piercing / Precision Feat (D7), Piercing Fighting / Rapid Charge / Surge Mark (the
  mark already waits for the next friendly attack - "this activation" was a paraphrase), Takedown when Shooting,
  Bloodthirsty Fighter. GDF file: Piercing Fighter's text ("This model gets AP(+1) on its weapons in melee." - only AoF
  Goblins defines it in 3.6). Catalog (engine): "Bane in melee" carries the AoF books' 3.6 "Bane in Melee" text. Every
  AoF-file name a 3.6 AoF book defines now carries its book text except Retreating Strike (D9) and the dead 3.5 "AP"
  quirk (#436). Coverage after: all 90 "Dead: 4066 (4066 no-definition across 93 names)" (Assault 3503, Parry 264,
  Enhanced 299 / 91 names); AoF only "Dead: 1152 (1152 no-definition across 60 names)" (Assault 763, Parry 252, Enhanced
  137 / 58). Tests: app `AofSweep36ShippedDataTests` +40 (21 texts + Piercing Fighter, five text-only twins, Cursed
  Undead = Protected, the catalog Bane in Melee text, the three aliases, Surge on their weapons, Piercing Charge Buff,
  every AoF grant resolves). Counts: engine 4107 (unchanged), app 3347 -> 3387.
- 2026-10-30: **Batch 2 - faction rules on existing primitives (data + an engine test).** Ossified / Warden: 3.6 "When
  units where most models have this rule take hits, those hits count as having AP(-1), to a min. of AP(0)." - Guardian's
  (#434) unconditional shape, the 3.5 `attackedFromOverInches(9)` gate gone. Wave-Step: "you may move all models with
  this rule in it by up to D3"" - Wolfborn's `repositionAtActivation.move` entry (#434 D8: a real move through the
  triggered-move request). Ethereal: "... If you do, this model moves -6" when using Advance, and -6" when using
  Rush/Charge until the end of that activation." (3.5 moved -6" always) - each penalty entry now also gates on a
  teleport having moved the unit this activation (D4; the token and its stamp came with batch 5). Grounded Speed: "If most models in this unit are within 1" of terrain
  when activated, this model may move +2" ..." - the terrain gate alone (D5). Shadow copies dropped from the AoF file
  (3.6 prints the same text in both games, so the GDF file's #434 definitions serve AoF books): Difficult Terrain Debuff
  and Melee Slayer (shape-identical), Fortified Growth (the AoF copy was the 3.5 clock without the most-models gate) -
  D6. Tests: engine `AofSweep36RuleIntegrationTests` +5 (Ethereal through the real TeleportStage: -6" on each move
  action after an accepted teleport with an activation-end marker, nothing without a teleport, nothing after a
  backed-out one); app `AofSweep36ShippedDataTests` +12 (texts, Guardian / Wolfborn twins, Fortified's shape, the
  Ethereal shape, no AoF name shadows a GDF one), `GroundedAofShippedDataTests` (no carrier gate),
  `MeleeSlayerShippedDataTests` (the AoF case reads the merged universe). Negative control: the hand-built Ethereal
  without its marker gate fails 2 of the 3 Ethereal cases (the 3.5 always-on penalty).
- 2026-10-30: **Batch 1 - the AoF Command Groups (engine).** AoF Core Rules v3.6.0: "Sergeant: This model gets AP(+1)
  when attacking." (= the GDF file's #434 Sergeant, text and shape), "Musician: This model and its unit move +1" when
  using move actions." (shape unchanged: +1" on Advance / Rush / Charge, one entry per action), "Banner: This model and
  its unit get +1 to Morale Test rolls triggered by shooting or melee." (3.5: every morale test). The GDF and AoF texts
  are the same (Army Forge's common rules list them for both games), so the GDF file's single definitions serve both -
  no AoF copy. Banner needed the test's CAUSE, which the pre-morale context did not carry (`ShotFromInches` /
  `ChargedFromInches` are null for a charger losing its own melee, and for a shooting test of unknown distance): new
  `EMoraleTestCause { Other, Shooting, Melee }` (capability `IHasMoraleTestCause`) on `PreMoraleTestContext.Cause`
  (default Other = what old call sites and every spell / rule-forced test pass), set by
  `MoraleUtilities.ResolveWoundDrivenMorale` (Shooting - the ranged test and the strafe / Crossing Strike one, "as if it
  was shooting") and `RollForMoraleStage` (Melee), threaded through `TakeMoraleTest(..., cause)`; new condition
  `moraleTestFromShootingOrMelee` (`Condition.MoraleTestFromShootingOrMelee`); `RuleFireLint` synthesizes Shooting /
  Melee pre-morale contexts. Book rules: Great Sergeant "This model gets AP(+2) when attacking." (3.5: extra hits on
  5-6) = Sergeant's weapon-scoped body at -2 (the champion marking still applies); Great Banner grants Banner once
  (3.5 granted Courage, every test) - D3; Great Musician unchanged but for the 3.6 text. Tests: engine
  `AofCommandGroup36RuleIntegrationTests` (15: Banner through the real wound-driven, melee-loser and Spell Drain sites,
  the no-cause default, Great Banner's one-shot and its spend by a rule-forced test, Musician per action, Great Musician,
  JSON round trip, validator accept / refuse, fire-lint) and `AofSweep36RuleIntegrationTests` (2: Great Sergeant AP(+2)
  in melee and shooting, no extra hits); app `AofSweep36ShippedDataTests` (12: texts, shapes, one definition for both
  systems). Negative controls (each reverted): the wound-driven site passing Other fails 2, the melee site passing Other
  fails 1, the condition always true fails 4, the lint's Shooting / Melee contexts removed fails the lint case. Counts:
  engine 4085 -> 4102 (1 skipped), app 3326 -> 3338.
- 2026-10-30: **Baseline (before any change).** Scratch import of all 90 3.6.0 snapshots (the #434 recipe):
  "Total references: 30839 / Dead: 4074 (4074 no-definition across 98 names)" - Assault 3503, Parry 264, Enhanced 299
  refs / 91 names, and the five AoF-only names (Piercing Charge Buff 3, "AP(+1) on its weapons in melee" 2, "Bane on
  its weapons in melee" 1, "Surge on their weapons" 1, "Unstoppable on its weapons in melee" 1; "Unwieldy on its weapons
  in melee" is no longer dead - #434 authored it). AoF only (the 41 AoF imports copied to their own folder): "Total
  references: 10794 / Dead: 1160 (1160 no-definition across 65 names)" - Assault 763, Parry 252, Enhanced 137 refs / 58
  names, the same five names (8 refs). Engine 4085 passed / 1 skipped, app 3326, `--validate-rules` GDF + AoF "OK: 329",
  shipped coverage "Dead: 36 (36 no-definition across 2 names)".

- 2026-10-30 (from #434): (1) **Moved from the AoF file to the GDF file** (AoF merges GDF first, so AoF keeps them):
  Rapid Rush Buff (3.6 text) and Ravage Aura (re-authored: an aura granting the one-die helper "Ravage (Aura)", with
  Ravage's 3.6 Fatigue gate; see #434 D10). (2) **Touched in the AoF file:** Defense Buff granted "Entrenched", whose old
  GDF copy was a plain +1 Defense; 3.6 Entrenched is Artillery's 12" shooting facet, so the Buff now places a one-shot +1
  Defense token (`statModifier Save +1 NextTrigger`, Defense Debuff's mirror) with the 3.6 text - check the AoF books
  agree. (3) **Shareable GDF definitions** (AoF inherits them by name): Spell Resistance (+ Aura), Dodge, Bravery Aura /
  Buff, Piercing Charge (+ Aura), Piercing / Precision Marksman, Thrust in Melee (+ Buff), Versatile Expert, Nimble,
  Vicious, Predator (+ Aura), Grounded Defense / Evasion, the reposition family as a move (Bounding, Rapid Blink,
  Wolfborn, Dash; AoF Wave-Step and its kin should take `move: true` too), Hazardous (model takes X for Blast/Deadly),
  Mind Control's `oncePerRoundKey`, Tag / Target, the weapons-only `isNotSpell` gate on "X on its weapons" grants, the
  catalog's Slayer on a Hold and Ravage 3.6. (4) **AoF-only names still dead** in the 90-entry scratch import after #434:
  Piercing Charge Buff 3 (Thrust in Melee Buff's shape over "Piercing Charge"), and four spell phrases the AoF spells
  produce - "AP(+1) on its weapons in melee" 2, "Bane on its weapons in melee" 1, "Unstoppable on its weapons in melee" 1,
  "Surge on their weapons" 1 - author them like #434's spell-phrase aliases (the canonical rule's entries under the 3.6
  phrase) or let #439 emit the canonical names. Takedown Strike's X is 1 in all 8 AoF refs (#434 D11).
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

- **D1 - Which texts change (2026-10-30).** #434 D1's convention, applied to the AoF file: every definition the sweep
  re-authors, and every AoF-file name a 3.6.0 AoF book defines whose behaviour already matches its 3.6 text, carries
  that text verbatim (ASCII-folded) - the AoF file ends text-parity with the 3.6 books. The GDF file and the catalog are
  touched only for names this item owns (Banner, Musician, Piercing Fighter, Bane in Melee); their other paraphrases stay
  #436's.
- **D2 - Banner / Musician wording (2026-10-30) - OWNER REVIEW.** The AoF Core Rules v3.6.0 PDF (plan rule 5's source
  (a)) reads "... triggered by shooting or melee." and "This model and its unit move +1" ..."; Army Forge's common-rules
  JSON (source (b), what army lists show) reads "... triggered by shooting/melee." and "... moves +1" ...". Shipped: the
  PDF's. Alternative: the Army Forge wording (one string each to change).
- **D3 - Great Banner's one-shot Banner (2026-10-30) - OWNER REVIEW.** "... which gets +1 to Morale Test rolls
  triggered by shooting/melee once (next time the effect would apply)" is authored as a one-shot grant of Banner
  (`addRule Banner NextTrigger`), so the +1 keeps Banner's shooting-or-melee gate. The grant is spent by the unit's next
  Morale Test whatever triggered it (#101's rule: a one-shot grant is spent when its hook fires, even if its condition
  fails), so a spell- or rule-forced test (Mind Control, Spell Drain, a spell's Morale Test) taken first costs the grant
  and helps nothing. Alternatives: (a) spend a grant only when its condition passes (an engine-wide change to #101's
  policy); (b) a one-shot +1 Morale token (Bravery Buff's shape), which would wrongly help spell / rule tests.
  Recommendation: keep (the case is rare and #101's policy is deliberate).
- **D4 - Ethereal's "if you do" = a teleport that moved a model (2026-10-30).** The penalty entries gate on the new
  catalog token `TeleportedThisActivation`, which TeleportStage stamps only when at least one model was placed somewhere
  else (activation-end clear: "until the end of that activation"). An accepted placement that leaves every model where
  it stood - the CLI and AI default answer - pays the once-per-activation cost (so the offer does not loop) but is not
  "you do". (First authored on the cost's `AbilityUsed:Ethereal` marker; the scratch Ghostly Undead smoke showed the
  default resolvers accept the offer and stand still, which would have charged them -6" for nothing - changed in the
  same batch.)
- **D5 - Grounded Speed loses its carrier gate (2026-10-30).** 3.6 has no "where most models have this rule" clause
  ("If most models in this unit are within 1" of terrain when activated, this model may move ..."), so only the terrain
  gate stays - #434 D15's Grounded Precision / Speed Feat reading. The unit-level move evaluation never sees a joined
  hero's own copy, so nothing leaks.
- **D6 - Shadow copies leave the AoF file (2026-10-30).** A name whose 3.6 text is identical in both games keeps one
  definition, in the GDF file (AoF merges it first) - #422 / #434's moves of Counter in Melee, Rapid Rush Buff and Ravage
  Aura. Dropped from the AoF file: Difficult Terrain Debuff, Fortified Growth, Melee Slayer (the names still resolve, to
  the GDF definitions; nothing is deleted from the rule universe). `AofSweep36ShippedDataTests.TheAofFile_ShadowsNoGdfDefinition`
  keeps the two files disjoint.
- **D7 - Piercing / Precision Feat keep the activation-start offer (2026-10-30) - OWNER REVIEW.** 3.6: "Once per game,
  when this unit attacks, you may use this rule so that the weapons of all models with this rule in it get AP(+1)" /
  "... get +1 to hit rolls" (3.5: "at the start of its activation"). Shipped: the 3.5 offer at activation start
  (once per game, most models, the Boost helper for the activation) with the 3.6 text - GDF Speed Feat's #434
  approximation of "when this unit moves". The Boost covers every weapon of the unit, not only the carriers' (one model
  in a unit without the rule cannot be told apart at the unit-level hooks). Options: (a) keep; (b) offer it as a
  before-attack menu action (Activation_OnBeforeAttackAction, existing hook - still the unit's own activation);
  (c) a Yes/No at the attack window (Combat_OnAttackWindow, the Takedown Strike / Shot seam: the existing YesNoRequest,
  fires for shooting, charging AND strike-backs), which needs ResolveExtraAttackStage to accept an AddRule offer with an
  attack-long lifetime. Recommendation: (c) - the only reading that covers "when this unit attacks" in the enemy's
  activation too; no new request type.
- **D8 - The AoF spell-phrase aliases (2026-10-30).** #434 D5's convention, in the AoF file (no GDF book or spell names
  them). "Surge on their weapons" keeps #434 D2's weapons-only reading of "on their weapons" (`isNotSpell`, as "Shred /
  Bane on their weapons when attacking"); the "in melee" three need no gate (melee hits are never a spell's).
- **D9 - Retreating Strike unchanged (2026-10-30).** Its 3.6 text is its 3.5 text ("... roll X dice for each model with
  this rule in this unit. For each 6+ the target takes one wound."), so #381's owner ruling (trigger = end of a chosen
  move after melee; not while Shaken; once per round) stands and the description keeps the ruling's two clarifications
  ("or X dice in total for single-model units", "Cannot be used while Shaken"). #436's text parity should keep them or
  move them to the ruling's notes.
- **D10 - Royal Warrior keeps the extra-hit approximation (2026-10-30) - OWNER NOTE.** "For each unmodified roll of 6 to
  hit when attacking, this model may roll +1 attack with that weapon" is #434 D7's family (Predator, Bloodborn, Primal,
  Clan Warrior); authored as Predator's entries (one extra hit on a 6) with the 3.6 text. A true bonus attack needs a
  hit-face `AddBonusAttack` sibling (pre-existing gap, #434 D7).
- **D11 - No per-book override ships (2026-10-30).** All 14 entries across the six files were moot against the 3.6.0
  books (Notes, batch 4), so the files are deleted rather than renamed; the `AofBookOverrides/<OPR book name>.json` hook
  stays for a future book-specific text. The bundled 3.5 AoF books embed their old override definitions until #438
  re-bakes them from the 3.6 snapshots (which then need no override).

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
