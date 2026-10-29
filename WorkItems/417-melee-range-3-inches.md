# 417 — Melee ranges 3.6: 3" strike range, 3" pile-in and consolidation

**Status**: todo
**Related**: #415, #022 (vertical melee cylinder), #018 (pile-in), #339 (consolidation), #442 (AI/calculator parity)
**Branch**: `feature/opr-3.6`. Model/effort: Opus 5.5 / medium. Open question: plan Q4 (vertical).

## Goal

Melee geometry matches Core Rules v3.6.0: "Charging models must move into base contact with targets, or as
close as possible, and then defenders must do the same by moving up to 3". Models within 3" of enemies must
strike with all melee weapons [...]. If either unit is destroyed, the other may move by up to 3", else
chargers must move back and separate by 1" (if possible)." In 3.5.1 the strike range was 2".

"Done" = the single strike-range definition (`MeleeRangeUtilities.AreModelsInMeleeRange`, consumed by charge
availability, defender eligibility and the strike check per #022) reads 3" horizontally; the defender
pile-in cap, the post-melee consolidation move and the 1" separation match the text; every overlay that
draws the strike reach (charge/melee rings, the #325 forecast's "models in range" count, the measurement
overlay's melee hints) uses the same constant; the AI threat model (#442) is told.

## What changes

1. Engine: the horizontal strike radius 2" -> 3" in one place; grep for other `2f`/`2.0f`/`2"` melee literals
   (`MeleeRangeUtilities`, `PileInUtilities`, `ConsolidateStage`, `ChooseMeleeDefenderStage`,
   `ChooseActionStage.GetCanCharge`) and route them through the one definition.
2. Vertical: plan Q4 default keeps the 4" vertical convention from #022 (OPR gives a single 3" figure and no
   vertical rule). Record Chris's answer here before changing the vertical number.
3. Pile-in: defenders "move up to 3"" - confirm the existing cap; consolidation "up to 3"" and the "move back
   and separate by 1"" step - confirm both exist and match (#339 added the defender-kills-charger case).
4. Resolvers: GUI rings/overlays and the CLI prompt text that mention 2"; `docs/ResolverGuide.md` and
   `docs/EngineNotes.md` lines that state the 2" cylinder.
5. Tests: update `VerticalMeleeRangeTests`, `MeleeRangeUtilities` tests and any pile-in/consolidate test
   that encodes 2"; add one test that a model at 2.5" strikes and at 3.5" does not.

## Acceptance

A unit with models 2.5" from the enemy (horizontal) is offered the charge/strike and the enemy at 3.5" is
not; consolidation offers up to 3"; suite green; headless smoke exit 0; GUI hand-verify by Chris: charge a
unit and see the 3" reach ring.

## Code anchors (survey 2026-10-29)

- `FutureOfDarkGrimness/.../MeleeStage/MeleeRangeUtilities.cs` (strike cylinder; #022), `.../PileInStage/PileInUtilities.cs`
  (defender pile-in), `ChooseActionStage.GetCanCharge`, `ChooseMeleeDefenderStage`, `ConsolidateStage`.
- See the appended survey rows below for exact lines.

### Survey rows (code survey 2026-10-29)

Findings that shape the item: pile-in (3") and consolidation (3" survivor / 1" fall-back) ALREADY match
3.6; the real change is the 2" strike constant and everything derived from it (charge availability, the
1"-2" "may charge" band, `ValidateChargeReach`, AI threat reach, GUI rings).

- Constants `FutureOfDarkGrimness/Utilities/GameWideConstants.cs:16` `MELEE_RANGE_INCHES_HORIZONTAL = 2`,
  `:22` `MELEE_RANGE_INCHES_VERTICAL = 4` (#022), `:41` `ENEMY_STANDOFF_DISTANCE_INCHES = 1`.
- One shared check: `.../MeleeStage/MeleeRangeUtilities.cs:19-31` `AreModelsInMeleeRange` (:25 horizontal,
  :28 vertical); unit wrapper :41; helpers :64, :77; melee weapon pool :94.
- Callers: `ChooseMeleeDefenderStage.cs:65`, `DetermineInRangeAttackersStage.cs:33`,
  `DetermineInRangeDefendersStage.cs:33`, `ChooseActionStage.cs:657` (charge availability; band comments
  :648/:699), `RollToHitStage/AttackBeatPositions.cs:155`, `Ai/Tactician/Search/TacticianActionSpace.cs:148`.
- Other readers of the constant: `MovementStage/MovementUtilities.cs:436` (`ValidateChargeReach`, error text
  :1517), `FdgRaylib/Rendering/Resolvers/GuiDefineMovementResolver.cs:2528`, `Ai/Tactician/TacticalAnalysis.cs:96`
  (`MeleeThreatReach`), `TacticianPlanner.cs:1363`, `Learning/PositionEncoder.cs:491`,
  `Rules/Foundation/IHasAttackOriginDistance.cs:9` (doc).
- Pile-in `PileInStage/PileInUtilities.cs:21` `MAX_PILE_IN_DISTANCE_INCHES = 3f` (:142, :290; header :6-8 cites
  v3.5.1). Consolidation `ConsolidateStage/ConsolidateStage.cs:11-12` (3f wipe-out, 1f disengage; :39-42 either
  seat per #339); `Ai/Resolvers/AiConsolidationMoveResolver.cs:57`, `GuiConsolidationMoveResolver.cs:569`.
- Tests: `Tests/MeleeInRangeIntegrationTests.cs`, `VerticalMeleeRangeTests.cs`, `PileInTests.cs`,
  `ConsolidateStageTests.cs`, `StrikeBackConsolidationTests.cs`, `ChargeReachValidationTests.cs`,
  `AttackOriginDistanceTests.cs:19`, `ForcedChargeUtilitiesTests.cs`, `FdgRaylib.Tests/ForcedChargeBandTests.cs`,
  `AiConsolidationMoveResolverTests.cs`.
- Docs: `docs/EngineNotes.md:52` ("2"/4" range gating"), `docs/ResolverGuide.md:259-260`.

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
