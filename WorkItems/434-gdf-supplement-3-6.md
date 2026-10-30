# 434 — GDF supplement sweep to 3.6

**Status**: done 2026-10-30 (Enhanced family deferred to #433; Assault / Parry are #424 / #423; owner review asked on
D2, D3, D4, D7, D12, D13). Engine commits `98a005ec`, `7ae790c8`, `f1dffde5`, `087157b8`, `c6e28725`, `f856ec1f`,
`b29a5fa4`, `d885d3cb` (submodule `feature/opr-3.6`); superproject batch commits `434: GDF sweep batch 1` ... `batch 8`
plus the records commit.
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

- 2026-10-30: **Done - coverage, verification, hand-offs.**
  - **Coverage (scratch imports outside the repo, the #431 / #432 recipe).** GDF only (the 49 GDF 3.6.0 snapshots, GDF
    supplement): before "Total references: 20045 / Dead: 3163 (3163 no-definition across 92 names)" - Enhanced 162
    refs / 60 names, other 3001 refs / 32 names (Assault 2740, Parry 12, this item's 249 across 30 names); after
    "Total references: 20045 / Dead: 2914 (2914 no-definition across 62 names)" - Enhanced 162 / 60, other 2752 / 2
    (Assault 2740 -> #424, Parry 12 -> #423). **Dead excluding Enhanced = 0 for this item's names.** All 90 entries (GDF
    books with the GDF supplement, AoF books with GDF + AoF + override): before (the parent commit's supplements, today's
    engine) "Total references: 30839 / Dead: 4439 (4439 no-definition across 128 names)" - Enhanced 299 / 91, other
    4140 / 37; after "Total references: 30839 / Dead: 4074 (4074 no-definition across 98 names)" - Enhanced 299 / 91,
    other 3775 / 7: Assault 3503, Parry 264, and five AoF-only names handed to #435 (Piercing Charge Buff 3, "AP(+1) on
    its weapons in melee" 2, "Bane on its weapons in melee" 1, "Surge on their weapons" 1, "Unstoppable on its weapons
    in melee" 1).
  - **Bundled 3.5 books** `--rule-coverage FdgRaylib/Assets/Books`: "Total references: 22079 / Dead: 36 (36 no-definition
    across 2 names)" before and after every batch (unchanged - the Counter-Attack family, #438).
  - **`--validate-rules GdfRuleSupplement.json AofRuleSupplement.json`**: "OK: 294 definitions" -> "OK: 329 definitions,
    no problems." (GDF alone "OK: 282"; each AofBookOverrides file on top also OK).
  - **Tests.** Engine 4004 -> 4085 passed (1 skipped before and after). FdgRaylib.Tests 3107 -> 3326.
  - **Smokes.** `printf "2\n2\n" | ... --headless` exit 0 after every batch. 3.6 scratch armies (`--import-opr` +
    `--book-to-army`, units reordered so the rule carriers are the first four, `--headless --army ... --trace-rules`), all
    exit 0: Wolf Brothers ("Wolf Master Destroyer's Wolfborn at Activation_OnActivationStart/Actor: fired ->
    RepositionModels" / "... may move up to 3""), Voidspace Stalkers ("Butchers: Ravage 3 dice at 6+ dealt ...", "Butcher
    Champion already rolled its once-per-round strike dice this round."), Ratmen Clans ("Quiprie's Hazardous at
    Shooting_OnHitRollComplete/Actor: fired -> InflictSelfWounds"), Custodian Brothers ("Veteran Master Custodian's
    Guardian at Shooting_OnHitRollComplete/Subject: fired -> ReduceArmorPenetration", "Versatile Expert - chose AP(+1)."),
    Alien Hives ("Synapse Beasts's Spell Resistance at Shooting_OnSaveRollComplete/Subject: fired -> IgnoreWound").
  - **App tests touched and why:** `GdfSweep36ShippedDataTests` (new, 180 cases), `GroundedShippedDataTests` (Grounded
    Precision lost its carrier gate; Defense / Evasion twins), `QuickShotAndUnwieldyShippedDataTests` (the Debuff's 3.6 grant
    target), `SergeantShippedDataTests` (AP body), `SpeedFeatShippedDataTests` (no gate), `TerrainDebuffShippedDataTests`
    (Difficult in line of sight), `RavageAuraShippedDataTests` (GDF file, aura + helper), `RegenerativeStrengthShippedDataTests`
    (the 3.6 consumer), `HazardousShippedDataTests` (Blast/Deadly flag + text), `MoraleTestThenShippedDataTests` (Mind
    Control's key). No count-pinning `*ShippedDataTests` needed a change.
  - **Engine tests touched:** new `GdfSweep36RuleIntegrationTests` / `GdfSweep36Definitions`, `ActivationMoveRuleIntegrationTests`,
    `MindControlOncePerRoundRuleIntegrationTests`, `TagTarget36RuleIntegrationTests`; extended `RavageRuleIntegrationTests`,
    `HazardousRuleIntegrationTests`, `RuleFireLintSelfTests`; re-pinned to 3.6 `RegenerativeStrengthRuleIntegrationTests`
    (consumer half), `ThrustRuleIntegrationTests` (Slayer), `OprBookImporterTests` (no rename), `SpellPhraseCloneTests`.

- 2026-10-30: **Batch 8 - the "X on its weapons" grants are weapons only; catalog 3.6 texts; Versatile Defense interim
  (engine).** 3.6 words every grant "gets X ON ITS WEAPONS when shooting / in melee / when attacking"; the engine has no
  separate weapon-scoped grant path (D2) - a token grant (aura / buff / mark) is evaluated for every attack the unit
  makes, and the spell-damage pipeline fires the CASTER's rules at the hit/save hooks with `IsMelee` false, so "AP(+1)
  when shooting" / "Bane when attacking" reached a caster's spell hits. The catalog's "when shooting" / "when attacking"
  phrase rules (Unstoppable / Shred / Bane when shooting, AP(1) when shooting, Bane / Shred when attacking) now also gate
  on `isNotSpell` at those hooks (the melee ones already excluded spells); the supplement twins (Rending when shooting,
  Piercing Shooter / Shooting, Piercing Frenzy / Growth, Piercing Debuff Effect, Ambushing Piercing Shot) likewise, and
  the batch-1 spell-phrase aliases were re-synced to the catalog. `SpellPhraseCloneTests` records the conscious
  divergence (the "when attacking" clones gate on IsNotSpell, the weapon-borne Bane / Shred - which a spell's own "with
  Bane" weapon carries - stay Always). Catalog texts (C#; the census changed-definition names the catalog holds that
  are not core rules): Guerrilla, Harassing, Hit & Run (+ Fighter / Shooter), Lacerate, Martial Prowess, Evasive, Melee
  Evasion, Fortified, Protected, Shielded, Increased Shooting Range (+ Aura), Unstoppable / Bane / Shred when Shooting,
  Shred / Rending / Unstoppable in Melee, and the seven "in Melee / when Shooting" Auras (`UnitAura` gained an optional
  description). Versatile Defense: OWNER REVIEW (D3); interim helpers on `attackedFromOver {9, 6}` (#432 D8's advice)
  with an honest description of the interim pick. Tests: engine `GdfSweep36RuleIntegrationTests` +3 (a caster's "AP(1)
  when shooting" misses its spell hits; "Shred when attacking" is weapons only, both ways), `SpellPhraseCloneTests`
  updated; app `GdfSweep36ShippedDataTests` +38 (29 catalog texts, 9 weapons-only twins, the Versatile Defense interim
  gate). Negative control: the AP / Shred gates reverted fail both new engine cases. Counts: engine 4082 -> 4085, app
  3284 -> 3324.

- 2026-10-30: **Batch 7 - Piercing / Precision Tag and Target 3.6 (engine).** Tag(X): "place X tokens on one enemy unit
  within 24" and in line of sight, or one token on X enemy units ... When attacking, friendly units may remove ONE
  token from their target ... to get AP(+1) / +1 to hit." Target(X): "pick X enemy units within 18" ... and place one
  token on them. Friendly units get AP(+1) / +1 to hit rolls when attacking them." (3.5: X markers on one unit, +1 per
  marker, any number spent.) New vocabulary: `TargetSelector.maxCountFromArgument` (pick up to the rule's X units;
  `EffectiveMaxCount`, read by `BeforeAttackActionStage`'s target loop), `ValueSource.argIfSoleTarget` (X on a lone
  target, 1 each when spread; reads the new `RuleInvocation.TargetCount` that `RuleEvaluator.ResolveAbility` fills;
  `RuleArgumentArity` counts it), and the single-spend Tag tokens `SingleSpendHitBonus` / `SingleSpendApBonus` (catalog
  tokens) that `TargetMarkerSpend` offers one per attack through the existing StringSelection prompt (spend-first
  default) - the Spotters keep their spend-any-number markers. `ShootingForecast` notes the Tag tokens. The four
  definitions use them, with the 3.6 texts. No new request type: the target loop and the spend menu already exist on
  CLI, GUI and AI. Tests: engine `TagTarget36RuleIntegrationTests` (10: X tokens on one unit, one each on X units with at
  most X picks, Target one each, a flagless selector keeps its max, a Tag token spends one for +1, declining keeps it,
  JSON round trips + legacy selector, ArgIfSoleTarget, arity and fire-lint); app `GdfSweep36ShippedDataTests` +8 (shapes,
  texts). Negative controls: the stage on the static max fails 2, ArgIfSoleTarget always X fails 2, the Tag spend off
  fails 1.

- 2026-10-30: **Batch 6 - Mind Control's once-per-round clause (engine).** 3.6 adds "Units may only be moved once per
  round in this way." (relayed from #429): a per-TARGET gate across every Mind Control on the table, not just text.
  `Effect.TriggeredMove` gained `OncePerRoundKey` (JSON `oncePerRoundKey`, default null = unrestricted, what old data
  parses to); `RuleOperation.InvokeTriggeredMove` refuses a unit already stamped this round under the key (logged
  through a new default `IOperationServices.Log`) and stamps a unit that actually moved
  (`MovedOncePerRound:<key>`, RoundEnd clear) - a declined or zero-length move spends nothing; the morale test itself
  still happens. The supplement's Mind Control keys its move "Mind Control" and carries the 3.6 text. Tests: engine
  `MindControlOncePerRoundRuleIntegrationTests` (5: first move stamps with a RoundEnd clear, a second Mind Control this
  round cannot move the unit, a declined move spends nothing, an unkeyed forced move is unrestricted, JSON round trip +
  legacy parse) through the real MoraleTestThen -> GameOperationServices -> MovementExecutor path; app
  `MoraleTestThenShippedDataTests` (the key), `GdfSweep36ShippedDataTests` (+ Mind Control / Hazardous texts). Negative
  control: the stamp check off fails the second-Mind-Control case.

- 2026-10-30: **Batch 5 - Hazardous 3.6 (engine).** "Gets AP(4), but on unmodified results of 1 to hit this model takes
  one wound, or X wounds if this weapon has the Blast(X)/Deadly(X) rule." (3.5: "this weapon's unit takes one wound".)
  `Effect.SelfWoundOnUnmodifiedRoll` gained `CountFromBlastOrDeadly` (JSON `countFromBlastOrDeadly`, default false): a
  natural 1 is worth the FIRING weapon's Blast(X) / Deadly(X) (the larger if both), else `Count`; `InflictSelfWounds`
  carries `WoundsPerDie` and `RollToHitResults.SelfWoundsPerDie` takes it to `ApplyWoundsStage`, which now calls the new
  `CasualtyPresentation.ApplyCarrierWounds`: the wounds land on the living models that fired the volley's profile, one
  die's worth per model in turn (wounded carrier first), overflow on a model lost (a Deadly clump's rule), never spilled
  onto a model that did not fire; no carrier found falls back to the unit's order. Still unsaveable and after the attack
  (the #197 rulings). The supplement's Hazardous carries the flag and the 3.6 text. Tests: engine
  `HazardousRuleIntegrationTests` +6 (the gunner, not the front model, takes it; Blast(3) = 3 wounds; Deadly(3) on a
  1-wound gunner kills only the gunner; two 1s from two gunners land one each; the planner's fractional last die; JSON
  round trip + legacy parse), its hand-built shape on the 3.6 flag; app `HazardousShippedDataTests` (the flag and text).
  Negative controls: back to the unit's order fails 2, the Blast/Deadly read off fails 2 (Blast and two-gunner cases).

- 2026-10-30: **Batch 4 - "Hold actions" at the AP hook; Slayer / Ranged Slayer; the importer's stale rename; texts
  (engine).** 3.6 Slayer: "When using Hold actions and shooting, or when charging, this model's weapons get AP(+2) if
  most models in the target have Tough(3) or higher." (3.5: shooting from over 9"); Ranged Slayer the shooting half.
  AP folds at `Shooting_OnHitRollComplete`, whose context could not say whether the attacker moved (`--validate-rules`
  refused `afterMoving` there), so `HitRollCompleteContext` gained `AttackerMoved` (implements `IHasAttackerMoved`; the
  `afterMoving` condition Artillery / Indirect use) - filled by `RollToHitStage`, `ShootingForecast` and the AI's
  `CombatMath` from the attack's own moved flag, false for synthetic hits. Catalog Slayer re-authored (Hold arm, never a
  spell's hits; 3.6 text), supplement Ranged Slayer likewise. `OprBookImporter`: the Lust / War Disciples rename to
  "Dangerous Terrain Debuff (Immediate)" is gone - 3.6 prints the deferred text in all six GDF books (the survey row's
  re-check); the map stays empty for a future reuse, the "(Immediate)" definition stays for 3.5 data (#436). Texts:
  Takedown Shot / Strike (X is 1 in every 3.6.0 book, D11), Vengeance (the owner-signed literal 1 token still fits: one
  Honor-Bound model per unit), Versatile Reach. Tests: engine `ThrustRuleIntegrationTests` Slayer cases re-pinned to 3.6
  (Hold at 20" and 1" applies, after moving does not, soft target does not, charge arm unchanged),
  `GdfSweep36RuleIntegrationTests` +3 (Ranged Slayer Hold / moved / soft), `OprBookImporterTests` (no army renamed);
  app `GdfSweep36ShippedDataTests` +8 (Ranged Slayer shape, the batch's texts). Negative control: `RollToHitStage`
  passing `AttackerMoved: false` fails the after-moving Slayer case. Counts: engine 4057 -> 4061, app 3266 -> 3274.

- 2026-10-30: **Batch 3 - Ravage per model and 3.6; Regenerative Strength 3.6; Ravage Aura (engine).** 3.6 Ravage: "Once
  per round, when it's this model's turn to strike in melee, roll X dice before attacking, unless this unit is Fatigued.
  For each 6+ the target takes one wound." Regenerative Strength: "... Once per round, when it's this model's turn to
  strike in melee, roll as many dice as tokens on it before attacking, unless this unit is Fatigued. For each 4+ the
  target takes one wound." Ravage Aura: "This model and its unit get Ravage(+1)."
  - **#447's per-model gaps fixed** with its template: `ResolveRavageWoundsStage` now evaluates the striker WITH its
    living models (`ResolveImpactHitsStage.ChargerParticipant`), so a joined hero's own Ravage fires; `Effect.
    DealAutoWounds` counts carriers per ATTACHMENT through #447's `ChargeImpactHits.CountCarriers` (now `internal
    static`, shared): the host's copy counts its native models, the hero's copy the hero, a token grant every living
    model, a weapon copy the wielders of THAT attachment (no multiplication on a resumed game).
  - **Once per round** is the stage's gate (every rule it resolves says it): a unit whose pre-strike pool was rolled is
    stamped `TokenType.StrikeDiceUsed` (catalog token, RoundEnd clear) and skipped at its next strike this round; a
    strike that rolls nothing (no tokens yet, Fatigued) spends nothing. **Fatigue** is each rule's condition
    (`not(countsAsFatiguedInMelee)`): catalog Ravage (C#) with the 3.6 text, Regenerative Strength and the aura helper.
    Each rule rolls and is named on its own pool ("Butchers: Ravage 3 dice at 6+ dealt ..."), the "(Aura)" helper
    folding into the Ravage pool.
  - **New JSON vocabulary:** value source `tokenTotal {tType}` (the bearer's token magnitude, else count, rounded to a
    whole die count); `dealAutoWounds.perModel` (default true = the old meaning; false for a pool the bearer holds as a
    whole). `RuleFireLint` seeds the tokens a `tokenTotal` pool reads (`SatisfyEffectInputs`).
  - **Regenerative Strength:** the 3.5 read side (`RegenerativeStrengthAttacks`, stage code that offered +X attacks on
    one melee weapon from `DetermineHitRollStage`) is DELETED - no dual paths (plan rule 6); the 3.6 consumer is the
    supplement entry `Melee_OnChargeContact: not(Fatigued) -> dealAutoWounds(tokenTotal(RegenerativeStrengthMarker), 4,
    perModel false)` beside the unchanged marker producer. D9 (bundled 3.5 books / old lists keep the marker but lose
    the +X attacks until #438 re-bakes).
  - **Ravage Aura** moved from the AoF file to the GDF file (Daemons of Plague names it; AoF merges GDF first) and became
    a real aura granting "Ravage (Aura)" (a literal one-die Ravage with the Fatigue gate) - a token grant is the one
    attachment the per-model count reads as "this model and its unit".
  - **Tests.** Engine: `RavageRuleIntegrationTests` +6 (a joined hero's own Ravage fires and counts the hero; the host's
    counts its native models; a granted copy counts every model; once per round; Fatigued rolls nothing; the catalog
    text and gate), `RegenerativeStrengthRuleIntegrationTests` consumer half rewritten for 3.6 (8: dice = tokens at 4+,
    3s miss, fractional total rounds, no tokens = no roll and no use spent, once per round, Fatigued, no extra attacks
    any more, JSON round trip + legacy `dealAutoWounds` parses per-model), `RuleFireLintSelfTests` +1 (token-sized pool
    lints clean). App: `RavageAuraShippedDataTests` (aura + helper, GDF file, dice through the charger participant),
    `RegenerativeStrengthShippedDataTests` (+ the 3.6 consumer). Negative controls (each reverted): the striker without
    its models fails the two hero cases; the once-per-round check off fails both once-per-round cases; the old
    all-living carrier count fails the host case; the lint seeding off fails its self-test.
  - **Smoke.** Voidspace Stalkers scratch army (Butchers + Butcher Champion first), seeds 3/5/8, `--trace-rules`: exit 0,
    "Butchers: Ravage 3 dice at 6+ dealt 0 unsaveable wound(s) to Butchers." and "Butcher Champion already rolled its
    once-per-round strike dice this round."
  - **Coverage after batch 3:** "Total references: 20045 / Dead: 2914 (2914 no-definition across 62 names)" - only the
    60 Enhanced names (#433), Assault (#424) and Parry (#423) remain.

- 2026-10-30: **Batch 2 - the activation-time reposition family is a real move (engine).** 3.6 Bounding / Dash "you may
  move all models with this rule in it by up to D3+1"", Nimble / Rapid Blink / Wolfborn "by up to D3"" (3.5: "place ...
  anywhere fully within"). `Effect.RepositionAtActivation` gained `Move` (JSON `move`, default false - an old book or
  saved list parses to the 3.5 placement); the op `RepositionModels` carries `AsMove`; `RepositionPlacement.
  OfferFromOperations` turns a fold containing a move into ONE optional triggered move for the summed distance through
  `GameOperationServices.MoveUnit` - the movement request the post-combat family (Harassing / Hit & Run) already raises,
  so terrain, other units, coherency and the Dangerous terrain test apply, and Cancel / an over-budget path declines.
  Both folding stages (ActivationStartStage, ReconcileEndOfActivationStage for Dash) needed no change; Dash keeps its
  "no Yes/No in front of a cancellable reposition" (`IsCancellablePlacement` covers the move form). The supplement's
  Bounding, Rapid Blink, Wolfborn, Nimble and Dash carry `move: true` and the 3.6 text. No resolver changed: CLI, GUI and
  AI already answer that movement request (GUI glance below). Tests: engine `ActivationMoveRuleIntegrationTests` (8:
  move not placement, budget D3(+1), over-budget path declined, cancel, the placement form unchanged, a move + placement
  fold sums into one move, Dash at activation end with no Yes/No, JSON round trip + legacy parse, Describe); app
  `GdfSweep36ShippedDataTests` +9 (the family's shapes and texts). Negative control: the fold's move branch disabled
  fails 5 of the 8. Headless: a 3.6 Wolf Brothers scratch army (`--import-opr` + `--book-to-army`, `--trace-rules`,
  seed 7) exits 0 and logs "Wolf Master Destroyer's Wolfborn at Activation_OnActivationStart/Actor: fired ->
  RepositionModels" / "Wolf Master Destroyer may move up to 3"." Counts: engine 4041 -> 4049, app 3255 -> 3264.
  **GUI glance (Chris):** a Wolfborn / Bounding unit's activation now opens the ordinary move tool with one ring of the
  rolled D3(+1)" instead of the "Repositioning" placement panel; Cancel declines.

- 2026-10-30: **Batch 1 - new names and re-authors on existing primitives (data + tests, no engine code).**
  - **Coverage baseline (scratch import of the 49 GDF 3.6.0 snapshots against HEAD's `GdfRuleSupplement.json`,
    `--rule-coverage`):** "Total references: 20045 / Dead: 3163 (3163 no-definition across 92 names)": Enhanced 162 refs
    across 60 names (#433), Assault 2740 (#424) and Parry 12 (#423), and 249 refs across 30 names for this item.
  - **After batch 1:** "Total references: 20045 / Dead: 2915 (2915 no-definition across 63 names)" - Enhanced 60 names,
    Assault, Parry, and Ravage Aura (1, left for the Ravage batch).
  - **New definitions** (GDF supplement, 3.6.0 text verbatim): Bravery Aura / Bravery Buff, Spell Resistance (+ Aura),
    Dodge, Grounded Defense (+ Aura), Grounded Evasion, Piercing Charge (+ Aura), Piercing Marksman (+ Aura), Precision
    Marksman, Predator (+ Aura), Thrust in Melee (+ Buff), Versatile Expert (+ two helpers), Nimble, Vicious,
    Infiltrating Precision Strike, "Unwieldy on its weapons in melee", Rapid Rush Buff (moved from the AoF file), and
    ten spell-phrase aliases ("Unstoppable on their weapons when shooting", "Shred on its weapons when shooting", "AP(1) on
    their weapons when shooting", "Indirect on their weapons", "Rending on their weapons in melee", "+6\" range on its /
    their weapons when shooting", "Bane on their weapons when attacking", "Shred on its weapons in melee", "Shred on
    their weapons when attacking").
  - **Re-authored behaviour:** Entrenched (Artillery's defensive facet: most models, unmoved this round, shot from over
    12", -1; the old copy was a plain +1 Defense), Fortified Growth (Defensive Growth's round-start clock, AP(-1) per two
    tokens; the old copy ticked at round end per token and cleared on Shaken), Grounded Precision (terrain gate only),
    Guardian / Primeborn (unconditional, Fortified's shape), Sergeant (AP(+1) when attacking, still weapon-scoped for the
    champion marking), Shatter (AP(+3) on natural 6s), Speed Feat (no ownership gate), Difficult Terrain Debuff (now in
    line of sight), Thrust in Melee Aura (grants Thrust in Melee), Unwieldy Debuff (grants "Unwieldy on its weapons in
    melee"). AoF Defense Buff granted Entrenched (whose old copy was +1 Defense); it now places a +1 Defense token
    (Defense Debuff's mirror) so the Entrenched rewrite does not break it.
  - **Text only** (behaviour already 3.6): Battleborn, Honor Code, Steadfast, Dangerous / Defense / Fatigue / Morale /
    Piercing Debuff, Defensive Frenzy / Growth, Inquisitorial Agent, Melee Slayer, No Retreat, Piercing / Precision
    Frenzy, Growth and Spotter, Plaguebound, Self-Repair, Rending / Shred / Unstoppable / Bane / Indirect / Piercing
    Shooting Mark, Increased Shooting Range Buff / Mark, Bane in Melee Buff, Rending when Shooting Aura, Indirect when
    Shooting Aura, Piercing Fighter / Shooter Aura, Targeting Visor, Speed Debuff.
  - **Tests.** Engine (new): `GdfSweep36RuleIntegrationTests` (37 cases through the real AssignWounds / DetermineHitRoll /
    RollToHit -> DetermineSaveRollsNeeded stages, SyntheticHitResolution for the spell exclusion, the evaluator for
    the activation pick, round-start clock and terrain gate), shapes in `GdfSweep36Definitions`. App (new):
    `GdfSweep36ShippedDataTests` (111 cases: every touched name's 3.6 text, each new shape, twins, grants, aliases).
    App tests updated for 3.6: `GroundedShippedDataTests` (Grounded Precision lost its carrier gate; Defense / Evasion
    cases), `QuickShotAndUnwieldyShippedDataTests` (the Debuff's new grant target), `SergeantShippedDataTests` (AP body),
    `SpeedFeatShippedDataTests` (no gate), `TerrainDebuffShippedDataTests` (Difficult needs line of sight).
    Mutation checks on the new engine cases (each reverted): Dodge without its Fatigue gate fails 2, Entrenched without
    its moved gate fails 1, Piercing Marksman without its spell/melee gate fails 1.
  - **Counts.** Engine 4004 -> 4041 passed (1 skipped). FdgRaylib.Tests 3107 -> 3255.
  - **Verification.** Full build 0 errors; `--validate-rules GdfRuleSupplement.json AofRuleSupplement.json` "OK: 328
    definitions, no problems." (each AofBookOverrides file on top also OK); `--rule-coverage FdgRaylib/Assets/Books`
    "Dead: 36 (36 no-definition across 2 names)" (unchanged); headless smoke exit 0 ("Game result: outcome=Win ...").

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

- **D1 - Which texts change (2026-10-30).** Every name on the census's GDF "changed definitions" list that this
  supplement or the catalog holds (core rules excepted - their items own them), every new 3.6 name, and every
  definition re-authored here carries its 3.6.0 book text verbatim (ASCII-folded). Definitions whose 3.6 text equals
  their 3.5 text but whose shipped description is a paraphrase are left for #436's text parity (52 GDF / 57 catalog
  names, listed there).
- **D2 - "gets X on its weapons ..." (2026-10-30) - OWNER REVIEW.** The engine has no separate weapon-scoped grant path:
  a token grant (aura / buff / mark) is evaluated for every attack the unit makes, and a Weapon-scope rule named at unit
  level is re-homed onto the unit's weapons. "This model gets X on its weapons" rules therefore stay Unit scope (a model
  grant can only carry unit-scoped rules) - Sergeant keeps Weapon scope for its champion marking. The one observable
  3.6 difference is a caster's SPELL hits, which the spell pipeline evaluates with the caster's rules at the same hooks
  (IsMelee false): the "when shooting" / "when attacking" grants (catalog and supplement twins) now gate on
  `isNotSpell`. Alternative: leave spells as before (one condition per entry to revert).
- **D3 - Versatile Defense's per-attack pick (2026-10-30) - OWNER REVIEW, deferred: needs sign-off.** 3.6: "When a unit
  where most models have this rule is shot/charged from over 9"/6" away, pick one effect: +1 to Defense rolls, or enemy
  units get -1 to hit rolls against it. Note that you may pick a different effect each time the unit is attacked."
  That is a DEFENDER decision inside every attack (#432 D8). Options: (a) a new defender request at the hit step (the
  #423 Parry kind: new `IStageTaskRequest`, CLI/GUI/AI resolvers, a GUI prompt in the attacker's turn); (b) no prompt -
  the engine picks the better effect per attack (the expected-wounds comparison is exact: -1 to hit vs +1 Defense given
  Quality, Defense and AP); (c) keep the interim. Recommendation: (b) - it is a pure optimisation with no information
  the defender has and the engine lacks, so a prompt per attack would only slow play; (a) if Chris wants the agency.
  Shipped now: the interim (c) - the 3.5 deploy / activation pick, helpers on `attackedFromOver {9, 6}`, an honest
  description of the interim. 0 unit refs in the 3.6 books; 5 via Versatile Defense Aura.
- **D4 - Protection Feat's "you may use this rule" when wounds arrive (2026-10-30) - OWNER REVIEW, deferred: needs
  sign-off.** 3.6 only changed all -> most models (already the gate). The #197 shape is an approximation: the unit
  "braces" at activation start until its next activation, because "when this unit takes wounds ... you may use this rule"
  is a defender decision mid-attack. Options: (a) a Yes/No (the existing YesNoRequest) raised inside AssignWoundsStage
  when wounds land, defender's player, AI accepting at 2+ wounds; (b) auto-use on the first wounds taken; (c) keep.
  Recommendation: (a) - the request type already exists on every resolver, only the seam is new. Shipped: (c), unchanged
  description.
- **D5 - Spell-phrase aliases are duplicate definitions (2026-10-30).** The 3.6 spell text produces names like "Shred on
  its weapons when shooting"; there is no data-level alias, so each is authored as its canonical rule under the 3.6 name
  (entries pinned equal to the canonical's). #439 may emit either name.
- **D6 - Bravery and the AoF Defense Buff (2026-10-30).** Bravery Aura grants the catalog's Courage ("+1 to this unit's
  morale test rolls" - #436 must keep Courage); Bravery Buff places a one-shot +1 Morale token (Morale Debuff's mirror).
  AoF Defense Buff granted "Entrenched", whose old GDF copy was a plain +1 Defense; with Entrenched re-authored it now
  places a one-shot +1 Defense token (Defense Debuff's mirror) - the one AoF definition touched, recorded for #435.
- **D7 - Predator shares Bloodborn's shape (2026-10-30) - OWNER NOTE.** 3.6 Predator, Bloodborn, Clan Warrior, Primal and
  Predator Shooter all read "For each unmodified roll of 6 to hit ..., this model may roll +1 attack with that weapon";
  the 3.5 copies (unchanged text) approximate it as one extra HIT on a 6. Predator is authored on the same shape so the
  family stays consistent; a true bonus attack needs a hit-face `AddBonusAttack` sibling (pre-existing gap).
- **D8 - The reposition family moves (2026-10-30).** Bounding / Dash / Nimble / Rapid Blink / Wolfborn use the
  triggered-move machinery Harassing uses (path, terrain, coherency, Dangerous terrain, optional); the most-models gate
  stays as the 3.5 copies' reading of "all models with this rule in it". The move does not stamp MovedThisRound (it is
  not the unit's action move), so a Hold after Wolfborn is still a Hold. Enhanced 2D3 (#433) sums with a second D3 entry.
- **D9 - Regenerative Strength's 3.5 read side deleted (2026-10-30).** No dual paths (plan rule 6): the +X attacks stage
  code is gone; old lists and the bundled 3.5 books still place markers but no longer spend them until #438 re-bakes /
  the list is rebuilt.
- **D10 - Ravage's "once per round" is the stage's gate (2026-10-30).** Every rule `ResolveRavageWoundsStage` resolves
  says it, so one token per striking unit (rolled once per round; a strike that rolls nothing spends nothing). Ravage
  Aura is a real aura of a one-die helper because the per-model count reads a token grant as "this model and its unit".
- **D11 - Takedown Shot / Strike keep one attack (2026-10-30).** The 3.6 text says X, and X is 1 in every 3.6.0 book (3 /
  5 GDF and 8 AoF refs); making the count argument-driven would drop rating-less 3.5 references. Revisit if a book prints
  X > 1.
- **D12 - Hazardous: which model takes it (2026-10-30) - OWNER REVIEW.** The dice of a volley are pooled, so "the model
  that rolled the 1" is unknowable; each natural 1's wounds land on a carrier of the fired profile in turn (an
  already-wounded carrier first, then unit order), overflow on that model lost (a Deadly clump's rule), never spilled
  onto a model that did not fire. Alternatives: let the attacking player pick the carrier per 1 (a prompt), or pour into
  the carriers like a unit pool.
- **D13 - Tag's two modes in one selector (2026-10-30) - OWNER REVIEW.** "Place X tokens on one enemy unit, or one token
  on X enemy units": the player picks 1..X units; one pick gets X tokens, more get one each. Picking 2..X-1 units is
  allowed and only costs the player tokens. Tag tokens are claimed one per attack ("remove ONE token"); the Spotters
  keep spend-any-number. Alternative: two labelled abilities (needs the action menu to show labels for same-rule offers).
- **D14 - Mind Control's once per round (2026-10-30).** Keyed per rule on the moved unit; a refused move still takes the
  Morale Test (the text limits the move, not the test); a declined or zero-length move is not "moved".
- **D15 - Smaller calls (2026-10-30).** Entrenched = Artillery's defensive facet (same 3.6 sentence); Grounded Precision
  gates on terrain only (no carrier clause in 3.6); Speed Feat lost its ownership gate (3.6 text); Guardian / Primeborn
  are Fortified's shape; Fortified Growth re-authored to its text (the 3.5 copy ticked at round end per token and
  cleared on Shaken - it did not match its 3.5 text either); Speed Debuff's "enemy model" uses Spell Drain's selector
  convention (the model's unit); Vengeance keeps the owner-signed literal 1 token (#197); the importer's Lust / War
  Disciples rename is gone (3.6 prints one Dangerous Terrain Debuff); Slayer / Ranged Slayer read "Hold" as not after
  moving (Artillery / Indirect's convention), which needed `HitRollCompleteContext.AttackerMoved`.

## Ledger

Every GDF name this sweep touched or left dead. "Engine test" names the pinning case(s); "shape" means the definition is
entry-for-entry another rule's tested shape (pinned so by `GdfSweep36ShippedDataTests`). App side, every GDF row: the
3.6 text / shape in `GdfSweep36ShippedDataTests` and `RuleSupplementLintTests.EverySupplementRuleFires`.

| Rule(s) | Definition | Test / tested shape / deferral |
|---------|------------|--------------------------------|
| Spell Resistance (+ Aura) | GDF supplement (new) | `GdfSweep36RuleIntegrationTests.SpellResistance_*` (spell 2+, other 6+ at any range and in melee, minority) |
| Dodge | GDF (new) | `...Dodge_*` (melee -1, shooting / Fatigued / Shaken / minority nothing) |
| Bravery Aura, Bravery Buff | GDF (new) | Courage's tested aura; the Buff = Morale Debuff's StatModifier mirror (shape) |
| Grounded Defense (+ Aura), Grounded Evasion | GDF (new) | shape: Grounded Reinforcement / Grounded Stealth (`GroundedTerrainRuleIntegrationTests`) |
| Grounded Precision | GDF (re-authored) | `...GroundedPrecision_AModelCarriedCopy_*` |
| Piercing Charge (+ Aura) | GDF (new) | `...PiercingCharge_ApPlusOne_OnlyWhenCharging` |
| Piercing Marksman (+ Aura) | GDF (new) | `...PiercingMarksman_*` (10" / 8" / melee, spell excluded) |
| Precision Marksman | GDF (new) | shape: Targeting Visor |
| Predator (+ Aura) | GDF (new) | shape: Bloodborn (D7) |
| Thrust in Melee (+ Buff), Thrust in Melee Aura | GDF (new / re-authored) | shape: catalog Thrust (`ThrustRuleIntegrationTests`) |
| Versatile Expert (+ helpers) | GDF (new) | `...VersatileExpert_*` (two labelled picks, AP ungated, +1 to hit) |
| Nimble | GDF (new) | shape: Wolfborn (move) |
| Vicious | GDF (new) | shape: Mischievous |
| Infiltrating Precision Strike | GDF (new) | `...InfiltratingPrecisionStrike_*` |
| Unwieldy on its weapons in melee; Unwieldy Debuff | GDF (new / re-authored) | shape: Unwieldy in melee (`UnwieldyRuleIntegrationTests`) |
| Rapid Rush Buff | GDF (moved from AoF) | shape: Rapid Advance Buff |
| 10 spell-phrase aliases (D5) | GDF (new) | shape: their catalog / supplement canonical rules |
| Entrenched | GDF (re-authored) | `...Entrenched_*` (13" unmoved, 11", moved) |
| Fortified Growth | GDF (re-authored) | `...FortifiedGrowth_*` (round-start clock, max 4, Shaken; AP per two tokens) |
| Guardian, Primeborn | GDF (re-authored) | `...Guardian_*` (2" and 20", control) |
| Sergeant | GDF (re-authored) | `...Sergeant_TheCarryingWeapon_*`; champion marking `SergeantRuleIntegrationTests` |
| Shatter | GDF (re-authored) | `...Shatter_*` |
| Speed Feat | GDF (re-authored) | `SpeedFeatShippedDataTests` (offer shape) |
| Difficult Terrain Debuff | GDF (LoS) | `TerrainDebuffShippedDataTests` |
| Bounding, Dash, Rapid Blink, Wolfborn, Nimble | GDF (move) | `ActivationMoveRuleIntegrationTests` (8) |
| Ravage | catalog (C#) | `RavageRuleIntegrationTests` (+6: hero, host, grant, once per round, Fatigue, text) |
| Regenerative Strength | GDF (re-authored) | `RegenerativeStrengthRuleIntegrationTests` (consumer half, 8) |
| Ravage Aura, Ravage (Aura) | GDF (moved from AoF, re-authored) | `RavageAuraShippedDataTests`; `RavageRuleIntegrationTests.AGrantedCopy_*` |
| Slayer | catalog | `ThrustRuleIntegrationTests.Slayer_*` (re-pinned) |
| Ranged Slayer | GDF | `GdfSweep36RuleIntegrationTests.RangedSlayer_*` |
| Hazardous | GDF | `HazardousRuleIntegrationTests` (+6) |
| Mind Control | GDF | `MindControlOncePerRoundRuleIntegrationTests` (5) |
| Piercing Tag, Precision Tag, Piercing Target, Precision Target | GDF | `TagTarget36RuleIntegrationTests` (10) |
| Rending when shooting, Piercing Shooter / Shooting, Piercing Frenzy / Growth, Piercing Debuff Effect, Ambushing Piercing Shot (spell gate) | GDF | `GdfSweep36RuleIntegrationTests.AnApWhenShootingGrant_*` / `AShredWhenAttackingGrant_*` (the catalog twins), shape pinned |
| Unstoppable / Shred / Bane when shooting, AP(1) when shooting, Bane / Shred when attacking (spell gate) | catalog | same two cases; `SpellPhraseCloneTests` |
| Takedown Shot, Takedown Strike, Vengeance, Versatile Reach | GDF (text) | existing `ExtraAttack` / `Vengeance` / `VersatileReach` tests (D11, D15) |
| Text only (behaviour already 3.6): Battleborn, Honor Code, Steadfast, Dangerous / Defense / Fatigue / Morale / Piercing Debuff, Defensive Frenzy / Growth, Inquisitorial Agent, Melee Slayer, No Retreat, Piercing / Precision Frenzy / Growth / Spotter, Plaguebound, Self-Repair, Rending / Shred / Unstoppable / Bane / Indirect / Piercing Shooting Mark, Increased Shooting Range Buff / Mark, Bane in Melee Buff, Rending when Shooting Aura, Indirect when Shooting Aura, Piercing Fighter / Shooter Aura, Targeting Visor, Speed Debuff, Precision Charge, Precision Fighter | GDF | their existing #197 tests |
| Text only, catalog: Guerrilla, Harassing, Hit & Run (+ Fighter / Shooter), Lacerate, Martial Prowess, Evasive, Melee Evasion, Fortified, Protected, Shielded, Increased Shooting Range (+ Aura), Unstoppable / Bane / Shred when Shooting, Shred / Rending / Unstoppable in Melee, Bane / Rending / Shred / Unstoppable in Melee Aura, Bane / Shred / Unstoppable when Shooting Aura | catalog (C#) | existing tests; text pinned in `GdfSweep36ShippedDataTests.TheCatalogDefinition_CarriesThe36Text` |
| Versatile Defense (+ helpers, Aura) | GDF (interim) | deferred: needs sign-off (D3); interim helpers pinned (`VersatileDefense_Interim_*`, `VersatileDefenseRuleIntegrationTests`) |
| Protection Feat (+ Aura) | GDF (unchanged approximation) | deferred: needs sign-off (D4) |
| The 60 "Enhanced X" names (162 refs in the GDF books) | none | deferred to #433 (design fork) |
| Assault (2740 refs) | none | deferred to #424 (design fork, Q12) |
| Parry (12 refs) | none | deferred to #423 (design fork, Q11) |

Already finished by other items and only checked here: the #431 weapon family, the #432 range family (Changebound,
Darkborn, Devout, Ferocious, Guarded, Havocbound, Highborn, Hive Bond, Hold the Line, Infected, Knightborn, Lustbound,
Machine-Fog, Screened, Scurry, Sturdy, Warbound, Watchborn, Brutal, Piercing Warrior, Violent, Valiant, Shrouded,
Shadowborn, Extra Reach, Warding, Versatile Warrior), Counter in Melee (+ Aura / Buff, #422), Quick Throw (#425),
Vanguard / Fanatic / Spawn / Split / Reinforcement (#426), Spell Drain (#429), Unique Hero (#430).

## Outcome

Done 2026-10-30 on `feature/opr-3.6`. Every rule name the 49 GDF 3.6.0 books reference now resolves to a definition with
its 3.6 text and behaviour, except the 60 "Enhanced X" names (#433's design fork) and the core forks Assault (#424) and
Parry (#423): a scratch import goes from 92 dead names (3163 refs) to 62 (2914 refs), none of them this item's. New
engine vocabulary, each with JSON round trips, lint awareness and integration tests: the reposition family as a real
move (`repositionAtActivation.move`), per-model Ravage on #447's template with a once-per-round strike-dice gate
(`tokenTotal`, `dealAutoWounds.perModel`), `HitRollCompleteContext.AttackerMoved` (Hold at the AP hook), Hazardous on the
firing model (`countFromBlastOrDeadly`), Mind Control's per-target once-per-round (`triggeredMove.oncePerRoundKey`),
Tag / Target (`maxCountFromArgument`, `argIfSoleTarget`, single-spend Tag tokens), and the spell exclusion on the "X on
its weapons" grants. Open for the owner: D2, D3, D4, D7, D12, D13. Hand-offs recorded on #433, #435, #436, #438, #439,
#442.

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
