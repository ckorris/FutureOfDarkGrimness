# 424 — Assault weapons: one weapon counts as both ranged and melee

**Status**: todo (design fork written up 2026-10-29; pending Chris's sign-off, plan Q12)
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

## The fork (written up 2026-10-29; pending Chris's sign-off, nothing built)

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

- 2026-10-29: fork options written up ("The fork" above); nothing built until sign-off (plan Q12).
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
