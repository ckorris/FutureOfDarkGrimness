# 424 — Assault weapons: one weapon counts as both ranged and melee

**Status**: todo
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
