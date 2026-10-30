# 424 — Assault weapons: one weapon counts as both ranged and melee

**Status**: done 2026-10-30 (built the same day the fork was signed off, plan Q12). Awaiting GUI hand-verify after the #438 re-bake; owner review asked on D2, D3.
**Related**: #415, #209 (weapon option order), #239/#379 (effect sets), #336 (melee weapon rules display), #292 (shoot panel rules), #157 (Takedown per-shot picks), #437 (importer), #441 (Forge UI), #442 (AI/calculator)
**Branch**: `feature/opr-3.6`. Model/effort: Fable 5.1 / xhigh. This is the most pervasive data change in 3.6.

## Goal

Core Rules v3.6.0, Assault: "Counts as both a ranged weapon and a melee weapon." 2740 GDF and 763 AoF weapon
references (48 + 40 books): pistols, storm guns with blades, bike guns, cavalry javelins, monster mounted
weapons, pintle weapons. Today the engine classifies a weapon by range (0 = melee) and keeps separate
ranged/melee weapon lists at every consumer.

"Done" = a weapon with Assault and range > 0 fires in the shooting phase at its range AND strikes in melee
with the same attacks/AP/rules; a unit with only Assault weapons is both a shooter and a fighter for every
gate (can shoot? can charge-and-strike? "has melee weapon" checks such as #355's); weapon special rules that
are phase-scoped ("X when shooting", "X in melee") apply per use; Deadly/Blast/Takedown/Limited behave per
use (Limited is once per game regardless of phase - decide and record); the army compiler, list importer,
Forge unit cards, hover text, the shoot and melee panels, the combat calculator and the Tactician's threat
estimates all see one weapon in both roles with no duplication in point costs.

## What changes

1. Engine: a single `IsRanged(weapon)` / `IsMelee(weapon)` classification seam (range > 0 || Assault; range == 0
   || Assault) replacing every `Range == 0` / `Range > 0` test; audit `ChooseRangedAttackStage`, the melee
   strike weapon list, `MeleeRangeUtilities` consumers, `UnitCreationRules`, `LimitedRules`, hero weapon
   handling, `TransportUtilities` (units inside may not target outside - unaffected), AI resolvers.
   `CoreRuleCatalog.Assault` = a weapon-scoped marker rule read by the seam (like Limited).
2. Importer/data (#437 implements the stamp; this item defines the model): `BookFile` weapons keep one entry;
   `WeaponData` gets nothing new if the marker rule suffices - prefer the marker over a new field.
3. App: Forge weapon line shows "12" A3, Assault" once; the shoot panel lists Assault weapons; the melee panel
   lists them; effect-set keyword tables map Assault weapons to a ranged set for shooting and a melee set for
   strikes (#239 keys are per weapon today - needs a per-phase default); `RuleHover` text.
4. Tests: a unit with one Assault pistol shoots at 12" and strikes in melee in the same round; a unit with
   only Assault weapons passes the "can charge" and "can shoot" gates; Limited Assault weapon decision pinned;
   effect-set resolution per phase; Forge compile of a 3.6 book with Assault pistols.

## Signed-off design (2026-10-30, Chris)

- Assault is NOT a name-based marker (the `LimitedRules` shape). It is an ordinary supplement JSON definition whose
  effect grants a weapon ROLE through the capability-query convention (`CapabilityRuleQueries`: "capabilities are
  asked for, not tested for"). Two new operations, `EnableMeleeUse` and `EnableRangedUse`, answered by a new
  `WeaponRoleQueries` mirroring `SightRuleQueries` / `CapabilityRuleQueries`.
- Seam: `IsMelee = RangeInches == 0 || grants EnableMeleeUse`; `IsRanged = RangeInches > 0 || grants EnableRangedUse`.
  Roles are grants only, never negations, so two rules can only widen the set (Chris's conflict concern is closed by
  construction). A future "may not be used in melee" is an ACTION restriction beside `ActionRestrictionQueries`, not a
  classification.
- Rejected: a range-override-to-zero for the melee check (re-encodes melee as range 0, cannot say "also ranged", and
  needs a synthetic hook context for a question with no target or distance).
- Roles are resolved ONCE when the weapon is built and cached on it (the `EffectKey`-at-load precedent, #239), because
  the seam runs in hot paths (AI, per-frame UI) and from places with no table (Forge card, list compiler, importer).
  No 3.6 rule grants Assault mid-game, so the load-time answer is exact; record that as a D entry. The query must
  accept a bare weapon (no unit on a table).
- B1 stands, with Chris's constraint: NO per-weapon authoring. Today `UnitData` resolves one key at load: the entry's
  optional `EffectSet`, else the army's `DefaultRangedEffectSet` / `DefaultMeleeEffectSet` picked by range. B1 adds only
  the second DERIVED key: every weapon gets `MeleeEffectKey = DefaultMeleeEffectSet` computed from the army defaults
  exactly like the first; no new field in `WeaponFileEntry` / `.fdgarmy` / `.fdgbook`, no entry in any effect-set table
  for plasma rifles or any other non-Assault weapon. A non-Assault ranged weapon carries the key and never reads it (it
  never swings). An explicit `EffectSet` on an entry applies to the ranged use only, as today. `RollToHitStage` picks by
  attack kind. Pin: an army file with no per-weapon effect sets compiles, and an Assault pistol's swing emits the melee
  default while its shot emits the ranged default.
- Everything else in "The fork" below (the audit of every direct `RangeInches` test, the rulings to pin, the order
  before #437/#438) stands as written.

## The fork (written up 2026-10-29; superseded by the signed-off design above where they differ)

Facts from the 3.6.0 snapshots and the engine survey that shape the choice:

- 3409 Assault weapons in 80 of the 90 books, every one with range > 0 (Heavy Pistol 224, Plasma Pistol
  198, Pistol 89, Flamer Pistol 85, ...). They ride with AP (1696), Blast (464), Deadly (400), Reliable
  (353), Anti-Cover (344), Rending (323), Surge (200), Limited (188), Bane (154), Unstoppable (128),
  Indirect (100), Pierce, Shred, Smash, Fracture. Army Forge stores Assault as an ordinary weapon special
  rule with no rating, so `.fdgbook` / `.fdgarmy` weapons already carry it in `SpecialRules` once a book
  is re-baked (#438); 3.5 files simply never name it.
- The engine has one classification seam, `IWeaponExtensions.IsRanged` (range > 0) / `IsMelee`
  (!IsRanged), and the two unit lists `GetRangedWeapons` / `GetMeleeWeapons` built on it, but about 25
  non-test engine sites, ~12 AI sites and ~20 app sites test `RangeInches` directly (survey rows above).
- Phase-scoped weapon rules already key on the ATTACK, not the weapon: the catalog's "in melee" / "when
  shooting" gates are `Condition.IsMelee()` (attack kind). So Indirect, Rending-in-melee variants, the
  #431 family etc. apply per use for free once the weapon reaches both pipelines.
- `Weapon.EffectKey` (#239 presentation) is resolved once at load from the weapon's range: ranged default
  set or melee default set. One consumer emits by it (`RollToHitStage`).
- `LimitedRules` spends per model per weapon NAME and is read by both the shooting and the melee flow, so an
  Assault Limited weapon fired in the shooting phase is already spent for melee.
- `ChargeContactRules.CanFightInMelee` = "has a melee weapon or acts on contact"; today an all-pistol unit
  cannot fight.

### Fork A - how the engine knows a weapon is both

- **A1 (recommended): marker rule + the existing seam.** `CoreRuleCatalog.Assault` is a weapon-scoped
  marker (the `Limited` shape: name-based detector, no hooks). `IsRanged` stays "range > 0"; `IsMelee`
  becomes "range == 0 || IsAssault". One `Weapon` object appears in both lists; every direct range test
  migrates to the seam (engine, AI, app). No data-format change, legacy files load unchanged, points are
  per unit so nothing double-counts. The cost is the audit: every consumer that assumed "exactly one list"
  (AI shooter-vs-fighter classifier, `GetWeaponNameAndStats`, Forge cards, shoot/melee panels, Combat
  Calculator) is visited once. That audit is the item and why it is xhigh.
- **A2: split at import.** The importer/compiler emits two entries per Assault weapon (the ranged one and a
  range-0 copy with the same name). Engine untouched. But the weapon shows twice on unit cards, hover,
  both panels and in saved `.fdgarmy` files; "Assault" still needs a definition or it reads as dead; and
  every later rule about "this weapon" sees two weapons. Fast, leaks everywhere. Not recommended.
- **A3: a new `WeaponFileEntry` kind field.** Same behaviour as A1 with more plumbing (importer, file
  format, Forge editor) for a fact the data already states as a rule. Not recommended.
- Recommendation: **A1**.

### Fork B - the presentation effect set for a weapon used in two phases

- **B1 (recommended): two keys resolved at load.** `UnitData` already holds both army defaults when it
  builds a `Weapon`; store `EffectKey` (ranged use; an explicit `EffectSet` on the entry applies here) and
  `MeleeEffectKey` (melee default), and `RollToHitStage` picks by attack kind. One field, one consumer.
- **B2: resolve at emission.** Needs the army defaults at emission time, which the weapon does not carry.
- **B3: ignore.** A pistol swing plays the gunshot effect. Cheap and wrong.
- Recommendation: **B1**.

### Rulings to pin as D entries when built

- Limited on an Assault weapon: once per game regardless of phase (the per-model token already does this;
  pin with a test).
- An Assault-only unit passes `CanFightInMelee`, may charge and strike back, and is offered the weapon in
  both the shoot panel and the melee weapon pick. Strafe-only exclusion still wins (no strafe Assault weapon
  exists in 3.6.0).
- Forge line prints the weapon once: `Heavy Pistol (12", A1, AP(1), Assault)`; the melee panel shows its
  melee stats without the range; `RuleHover` text is the 3.6 sentence.
- AI: `AiUnitClassifier` gives an Assault-armed unit both roles; pricing of the second role goes to #442.
- Order: this item lands before #437/#438 so the importer keeps one entry per weapon and the re-bake
  carries the marker.

### Hand-verify (Chris, GUI)

A re-baked Battle Brothers unit with Heavy Pistols shoots at 12" and then charges and strikes with the same
pistols in one round; the Forge card lists each pistol once.

## Acceptance

Tests green; `--book-to-army` on a re-baked Battle Brothers book + headless smoke exit 0 with pistols firing
and striking; hand-verify (Chris): shoot then charge with an Assault-armed unit in the GUI.

## Code anchors (survey 2026-10-29)

- Weapon model and the ranged/melee split sites (see the appended survey rows below for exact lines);
  `Rules/Dispatch/LimitedRules.cs` as the marker-rule precedent; `FdgRaylib/Rendering/ForgeUnitDetail.cs`,
  `CombatCalc/`, `Rendering/Resolvers/` shoot + melee panels.

### Survey rows (code survey 2026-10-29)

Findings that shape the item: there IS one classification seam - `GameObjects/Core/IWeapon.cs:196-204`
(`IsRanged = RangeInches > 0`, `IsMelee = !IsRanged`) - but dozens of consumers test range directly. Assault is
absent everywhere ("Piercing Assault", "Twin Assault Blaster" are unrelated substrings).

- Lists: `GameObjects/Core/IUnit.cs:175-183` `GetMeleeWeapons`/`GetRangedWeapons` (Strafe-only excluded);
  `MeleeRangeUtilities.cs:94-106`.
- Split sites (engine): `MeleeStage/CombatActionContext.cs:390/:395/:516`, `ChooseRangedAttackStage.cs:641`,
  `ChooseActionStage.cs:863`, `Rules/Dispatch/ChargeContactRules.cs:59` (`CanFightInMelee`),
  `DetermineStrikeOrderStage.cs:57/:85`, `MovementStage/WeaponSightProfileBuilder.cs:19`, `DefinePathStage.cs:201`,
  `StrafingRules.cs:18`, `Calculator/CombatCalculator.cs:374`, `GameObjects/Core/UnitData.cs:200` (effect set by
  range), `ArmyBuilding/WeaponEffectAssigner.cs:319-321`.
- Split sites (AI): `AiUnitClassifier.cs:14-15`, `TacticalAnalysis.cs:131/350/372`, `CombatMath.cs:253/571/596`,
  `TacticianPlanner.cs:808/1124/1370`, `MacroActionGenerator.cs:198`, `AiStringSelectionResolver.cs:185`.
- Split sites (app): `TableTooltipOverlay.cs:195/278/524`, `TacticalOverlayController.cs:391/1027/1037/1163/1295/1530`,
  `GuiDefineMovementResolver.cs:1545/2605/2736`, `ArmyBuilderScreen.cs:395`, `RuleTextFlow.cs:54`,
  `UnitOptionLabel.cs:37`, `GuiAssignWoundsResolver.cs:429`, `Cli/Resolvers/DefineMovementPathResolver.cs:340`.
- Import/compile: `OprBookImporter.cs:314/:498`, `OprListImporter.cs:364` (`RangeInches = w.Range ?? 0`),
  `ListCompiler.cs:859/:864` (tests `ArmyForgeCompilerTests.cs`).

## Notes

- 2026-10-30 (G, acceptance): all-90 scratch re-import with the current supplements
  (`scratchpad/424/all-after/`): Dead 563 = Parry 264 + Enhanced 299 (92 names), Assault 0 (was 3503 of 4066).
  Shipped `--rule-coverage FdgRaylib/Assets/Books`: Dead 36 (unchanged, Counter-Attack family until #438).
  `--validate-rules` GDF + AoF: OK, 335 definitions. Headless smokes (3.6 scratch armies via `--book-to-army`,
  `--trace-rules`, exit 0, `Game result:` line each): Battle Brothers reordered so Assault Brothers lead (solo AI) -
  the unit charges, "5 of 5 models in melee range", `Chose weapon: Heavy Pistol. Count: 5` in the melee, and later
  the shoot menu lists `5x Heavy Pistol - 9", A1, AP1, Assault` with a shooter in range; Blessed Sisters (tactician AI)
  - Constance's `Burst Blessed Flamer Pistol - 9", ..., Assault, ...` fires twice; AoF Havoc Warriors reordered so
  Barbarian Hunters lead (solo AI) - strike-back menu `[1] 2x Javelins - A1, AP0, Assault` with the 3.6 sentence as its
  description, `Chose weapon: Javelins. Count: 2` in melee, then `Shoot stage entered` / `Chose weapon: Javelins.
  Count: 5`. Negative controls (each removed, pins failed, restored): the `|| grants melee use` term of `IsMelee` ->
  17 failures across the three engine Assault fixtures; the melee label's `includeRange: false` -> 1 failure
  (`Melee_OffersThePistol_WithoutItsRange_AndCarriesItsRules`); `RollToHitStage`'s key pick by attack kind -> 1 failure
  (`AnAssaultPistolsSwing_EmitsTheMeleeKey_ItsShot_TheRangedKey`). Final gates: engine 4235 passed / 1 skipped (from
  4194), app 3389 (from 3378), build 0 errors, default headless smoke exit 0.
- 2026-10-30 (build, batches A-F; G follows): seven engine commits + their superproject bumps on `feature/opr-3.6`.
  A: `RuleOperation.EnableMeleeUse` / `EnableRangedUse` (CapabilityOperation), `Effect.EnableMeleeUse` / `EnableRangedUse`
  (JSON kinds `enableMeleeUse` / `enableRangedUse`), `Rules/Dispatch/WeaponRoleQueries.cs` (+ `EWeaponRoles` flags),
  `Weapon.GrantedRoles` cached (lazy, reset on rule-list change, copied by the clone ctor), `IWeaponExtensions.IsRanged`
  = "range > 0 or grants ranged use" / `IsMelee` = "range 0 or grants melee use", `RuleFireLint` consumes the two ops at
  the capability hook, `Assault` appended to `GdfRuleSupplement.json` (335 definitions; AoF reads the GDF file).
  B: `ChooseMeleeWeaponStage` labels print the melee line without the range (`GetWeaponNameAndStats(count,
  includeRange: false)`); every other consumer already went through the two pools / the seam. C (B1):
  `IWeapon.MeleeEffectKey`, set in `UnitData` from the army melee default, `RollToHitStage` picks by `IsMelee`.
  D: `TacticalAnalysis.RangedOutputWounds` / `MeleeOutputWounds` read the seam (the only AI site that tested the range).
  E: `WeaponStatFormatter.RangeLabel` ("12\"/melee" for a weapon that is both) in the table hover, the wound dialog
  and the unit picker (`UnitOptionLabel.BuildWithRangeLabels`); `TacticalOverlayController` / `TableTooltipOverlay`
  filter by `IsRanged()`. F: importers and compiler needed no change (pinned). Tests: engine `AssaultRuleIntegrationTests`
  (seam), `AssaultConsumerIntegrationTests` (pools, gates, shoot-then-strike, Limited across phases, per-use rule),
  `WeaponEffectKeyTests` (+5, B1), `AssaultAiIntegrationTests`, `CombatCalculatorTests` (+2), `AssaultImportCompileTests`;
  app `AssaultShippedDataTests`, `AssaultLegacyArmyTests`, card/glossary/formatter/picker pins.
- 2026-10-30 (survey corrections): the engine's split sites listed on 2026-10-29 were mostly already on the seam
  (`GetMeleeWeapons` / `GetRangedWeapons` / `IsMelee()`), so the audit was smaller than feared: the only direct range
  classifications were `TacticalAnalysis.cs:493/515` (AI) and the app's hover / overlay / picker labels. The rest of
  the `RangeInches` reads are reach numbers (max range, effective-range overrides, the 18\" long-range heuristic) or
  `WeaponFileEntry` display ("show the range when printed"), kept as they are (D9). `UnitCreationRules`, hero weapon
  handling and `TransportUtilities` never classified by range.
- 2026-10-30: Q12 signed off (Chris): capability-operation design + B1 without per-weapon authoring, see "Signed-off
  design". Implementation queued after #442.
- 2026-10-30 (from #442): the AI / calculator half of Assault waits on this fork (Q12); nothing was built in #442.
  Once the seam lands: (1) `CombatMath.EstimateMelee` batches `GetMeleeWeapons()` and `EstimateShooting` batches
  `GetRangedWeapons()` - under A1 ("IsMelee = range 0 or Assault", one weapon object in both lists) both pick an Assault
  weapon up with no AI change; pin a melee estimate and a shooting estimate of an Assault pistol; (2)
  `Ai/AiUnitClassifier` (Shooting / Melee / Hybrid by weapon mix) must count Assault weapons on both sides - the solo
  bot's whole move policy keys on that archetype; (3) `TacticalAnalysis.MeleeThreatReach` / the planner's
  `EnemyFacts.HasMelee` read melee weapons (and, since #442, `MayCharge`), and `MacroActionGenerator`'s `hasMelee`
  reads `ChargeContactRules.CanFightInMelee` - check an Assault-only unit reads as melee-capable there; (4) the Combat
  Calculator's melee batches go through `GetMeleeWeapons()` too. Shooting threat "at their range" follows from (1).
- 2026-10-29: fork options written up ("The fork" above); nothing built until sign-off (plan Q12).
- 2026-10-29: filed with the plan.

## Decisions

- D1 (signed off by Chris, 2026-10-30): the capability-operation design replaces fork A1's name marker; B1 as
  recommended, with no per-weapon effect-set authoring.
- D2 (OWNER REVIEW): `WeaponRoleQueries` resolves a weapon's roles by a STRUCTURAL read of its own rule definitions
  (an entry at `Lifecycle_OnCapabilityQuery` / Actor whose effect is `EnableMeleeUse` / `EnableRangedUse`), the
  `StrafingRules` / `ChargeContactRules` shape, rather than through a `RuleEvaluator` pass: at build time there is no
  unit, table or evaluator (the Forge card, the compiler, the importer, `UnitData`'s constructor), and the design says
  the load-time answer is exact. Consequences, stated rather than hidden: an entry's `Condition` is not consulted (a
  non-Always condition is granted anyway with a once-only warning), and token grants / suppression cannot reach a
  role. It mirrors `CapabilityRuleQueries` in shape (ask for the operation type, `*Source` attribution, nothing tests
  the name "Assault") but not in dispatch. Alternative: a stand-in `RuleInvocation` with a null bearer - rejected,
  any effect reading its bearer would throw.
- D3 (OWNER REVIEW): the Assault definition authors BOTH arms (`enableMeleeUse` and `enableRangedUse`) because the
  rule says "both". Every 3.6.0 Assault weapon has a printed range, so the ranged arm restates the range for the whole
  corpus; a hypothetical range-0 Assault weapon would be offered as a 0\" gun (no target ever in range). Alternative:
  melee-only grant - no behavioural difference for the corpus, less literal.
- D4: Limited on an Assault weapon is once per game regardless of phase - the per-model `LimitedSpent` token keys
  on the weapon, so the melee stage reads the shooting stage's spend with no bridge. Pinned through the real stages.
- D5: the role cache is a lazy memo on `Weapon` reset by `AttachRuleDefinition` / `RehydrateRules` (the
  `ProfileKey` precedent in the same class), not a constructor computation - rules attach after construction in every
  build path. The whole-store clone copies the resolved answer; a Newtonsoft hop rehydrates the rules then re-resolves.
  Pinned (`RolesResolved`).
- D6: `AiUnitClassifier` reads an Assault-only unit as both pools populated and archetype `Shooting` - its melee and
  ranged scores are the SAME weapons, so the "melee outscores ranged" Hybrid test ties, and a tie keeps the
  advance-and-fire policy. Pistol + sword is Hybrid. Whether a short-range pistol unit should lean Hybrid is pricing
  work, handed to #442.
- D7 (B1 detail): `MeleeEffectKey` is the army's melee default for a weapon with a printed range (an entry's explicit
  `EffectSet` stays with the ranged use, as before) and coincides with `EffectKey` for a weapon with no printed range,
  so an explicit set on a sword keeps applying to its swing. `UnitData` picks the ranged / melee DEFAULT by the printed
  range on purpose (the keys describe the shot and the swing); it is not a classification and does not use the seam.
- D8 (presentation): the melee weapon pick prints the melee line without the range (`Heavy Pistol - A1, AP1,
  Assault`); the shoot panel keeps the full line with the range; the Forge card prints the weapon ONCE as its ranged
  line with Assault among the rules (`5x Heavy Pistol (12\", A1, AP(1), Assault)` - `WeaponFileEntry` has no resolved
  rules, and "show the range when printed" is display, not classification); the in-game hover, the wound dialog and
  the unit picker print `12\"/melee` for a weapon that is both. No layout change anywhere.
- D9: direct `RangeInches` reads that are REACH numbers, not classifications, stay: `PositionEncoder.CheapThreatReach`
  / `TacticianPlaceObjectsResolver` (max range), `TacticianPlaceObjectiveResolver` (>= 18\" long-range heuristic),
  the overlays' effective-range overrides, `Condition.TargetOverHalfWeaponRange`, `ListCompiler.SameProfile`
  (equality), the importers' `RangeInches = w.Range ?? 0`, `WeaponEffectAssigner.Match` (authoring-time keyword
  policy on a `WeaponFileEntry`; one key per weapon, unchanged).
- D10: the share-link importer's 3.5.x version gate (`OprVersionMismatchException`) is #437's scope and untouched; the
  list-import pin uses a 3.5.3 stub book so the weapon mapping is what is tested.
- D11: no importer change - Army Forge stores Assault as an ordinary weapon special rule and both importers already
  map it to `SpecialRuleEntry_Core("Assault")`; the 3.6 scratch import carries it as soon as the definition exists.

## Outcome

Done 2026-10-30. Assault is an ordinary supplement definition (`GdfRuleSupplement.json`, weapon scope, Positive, the
3.6 sentence) whose two entries at `Lifecycle_OnCapabilityQuery` grant the weapon ROLES `EnableMeleeUse` /
`EnableRangedUse`; `WeaponRoleQueries` reads them off a bare weapon's own rules, `Weapon.GrantedRoles` caches the
answer, and the single seam `IWeaponExtensions.IsRanged` ("range > 0 or grants ranged use") / `IsMelee` ("range 0 or
grants melee use") puts one `Weapon` object in both of a unit's pools. Every engine, AI and app consumer reaches it
through the pools or the seam; the direct range tests that remained (two AI output estimators, the app's hover /
overlay / picker labels) were migrated. B1: `Weapon.MeleeEffectKey` (army melee default, derived at load, no new field
anywhere) and `RollToHitStage` picks the key by attack kind. Melee weapon picks print the melee line without the range;
the in-game hover / wound dialog / unit picker print `12"/melee`; the Forge card prints the weapon once with Assault
among its rules. Importers and compiler needed no change (pinned); a 3.5 army classifies exactly as before (pinned
against the shipped Battle Brothers 2k file and the engine's example army). 41 new tests (engine 41 incl. 5 B1 + 2
calculator; app 11). Acceptance: all-90 scratch import Assault 0 dead (Dead 563 = Parry + Enhanced), shipped Dead 36
unchanged, three 3.6 smokes exit 0 with a pistol / javelin both fired and swung. Decisions D2-D11; owner review on D2
(structural read instead of an evaluator pass) and D3 (Assault grants both arms). Deferred to: #437 (list importer's
3.5.x version gate), #438 (re-bake, then the GUI hand-verify), #441 (nothing required; `MeleeUseSource` available for an
explicit "also melee" card note), #442 (classifier tie-break and kite-band pricing for pistol units), #423 (follow
`WeaponRoleQueries`'s by-effect read). GUI hand-verify (Chris, after #438): a Battle Brothers unit with Heavy Pistols
shoots at 9" and then charges and strikes with the same pistols in one round; the melee menu shows the pistol without
its range; the hover shows `9"/Melee`; the Forge card lists each pistol once.

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
