# 417 — Melee ranges 3.6: 3" strike range, 3" pile-in and consolidation

**Status**: done 2026-10-29 (engine `ee15a968`; superproject commit `417: ...` bumps it with the app test, docs and
ledger) - awaiting Chris's GUI hand-verify (charge line/count at 3", the 1"-3" optional-charge band; list in Notes).
Owner questions: D5 (learned Strategist leaf, handed to #442), D6 (no drawn 3" ring exists - add one?).
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

- 2026-10-29: **Landed.** Engine commit `ee15a968` (submodule, `feature/opr-3.6`); the superproject commit bumps it
  with the app test, docs and this ledger.
  - **Engine, one number.** `GameWideConstants.MELEE_RANGE_INCHES_HORIZONTAL` 2 -> 3; `MELEE_RANGE_INCHES_VERTICAL`
    stays 4 (Q4). The audit found every reader already on the constant, none on a literal: the single definition
    `MeleeRangeUtilities.AreModelsInMeleeRange` (-> `ChooseActionStage.GetCanCharge`, `ChooseMeleeDefenderStage`,
    `DetermineInRange{Attackers,Defenders}Stage`, `AttackBeatPositions.MeleeFiringModels`,
    `TacticianActionSpace`), `MovementUtilities.ValidateChargeReach` and its error text, the AI's
    `TacticalAnalysis.MeleeThreatReach`, `TacticianPlanner.MeleeReachOf`, `PositionEncoder.CheapThreatReach`, and
    app-side `GuiDefineMovementResolver` (the per-enemy "N can charge" count and the yellow charge line from the
    ghost). Only comments said 2"; they now say 3" (MeleeRangeUtilities, ChooseActionStage x2,
    ChooseMeleeDefenderStage, ChooseMeleeWeaponStage, AttackBeatPositions, IHasAttackOriginDistance,
    CoreRuleCatalog Artillery, TacticalAnalysis, TacticianPlanner x2, TacticianActionSpace) and the quoted rule
    text is v3.6.0 (MeleeRangeUtilities, PileInUtilities, ChooseMeleeWeaponStage). The `ENEMY_STANDOFF` doc comment
    predated #206 (it said a move may not end in the band); rewritten to the current consequence semantics.
  - **Pile-in and consolidation confirmed, not changed.** `PileInUtilities.MAX_PILE_IN_DISTANCE_INCHES = 3`
    (`PileInTests.DefenderBeyondPileInRange_MovesExactlyThreeInches`) and `ConsolidateStage` 3" wipeout / 1"
    disengage (`ConsolidateStageTests`, `StrikeBackConsolidationTests`, `AiConsolidationMoveResolverTests`) all
    green untouched; the 3.6 text is now quoted at both constants.
  - **Text.** The greyed Charge reason names the range from the constant: "No enemies within the 3" melee range."
    (was "No enemies within melee range."; reworded once so the CLI's "Charge (reason)" doesn't nest brackets).
    The charge-reach refusal already read the constant ("... no model ends within 3" of an enemy"). Forced-charge
    texts (GUI panel warning, CLI standoff lines, Pass/Shoot reasons) name the 1" standoff and are unchanged by
    design (D1). The CLI movement prompt never stated the strike range, so nothing there changed.
  - **Docs.** `docs/EngineNotes.md` (melee range-gating bullet; pile-in/consolidation figures),
    `docs/ResolverGuide.md` (charge availability; forced-charge band now 1"-3").
  - **Tests.** New `MeleeRangeRuleIntegrationTests` (13): constants against the 3.6 text; the boundary through
    the single definition (contact, 2.5", exactly 3.0" in; 3.05", 3.5" out); Choose Action at 2.5" offers Charge
    AND Pass with no shot withheld, at 3.5" greys Charge with the reason text; Choose Melee Defender at 2.5" valid,
    at 3.5" not offered; the real DetermineInRangeAttackers/Defenders stages count only the 2.5" model of a
    2.5"/3.5" pair; `MeleeThreatReach` = 12" + 3". Negative control: with the constant set back to 2, 9 of the
    13 fail (the 4 that pass are the contact / out-of-range controls). Re-pinned: `VerticalMeleeRangeTests` (+2:
    2.5" across + 4" up in, + 4.5" up out), `ChargeReachValidationTests` (the accepted beyond-Rush case moved
    from a 1.9" to a 2.5" gap, a rejection under 3.5.1; +1 error-text pin), `ForcedChargeUtilitiesTests` (+1: a
    2.5" gap is chargeable and outside the forced band), `FdgRaylib.Tests/ForcedChargeBandTests` (+1: the panel
    warning names 1", never 3"). Comment-only 2" -> 3": MeleeInRangeIntegrationTests, AttackOriginDistanceTests,
    ChooseActionPassDisableTests, TransportEmbarkTests, OffTableTargetingTests, ActivationBackOutTests,
    BaseShapeCollisionTests, AttackBeatPositionsTests, CompelClosestTargetRuleIntegrationTests,
    TacticianActionSpaceTests, TacticianPlannerTests, MeleeLimitedTests, ScreenedShippedDataTests. AI pins: two
    `TacticianCoverHabitTests` corridor cases failed and were re-pinned (D3); `CombatMathPinTests` and every
    other Tactician pin stayed green untouched.
  - **Counts.** Engine 3469 -> 3486 passed (1 skipped before and after). FdgRaylib.Tests 3063 -> 3064.
  - **Verification.** Engine suite green; FdgRaylib.Tests green; full `dotnet build` 0 errors (incremental: 0
    warnings; a clean build's warnings are all pre-existing nullable ones, none on a touched line); headless smoke
    `printf "2\n2\n" | ... --headless` exit 0 ("Game result: outcome=Tie winner=none rounds=4 scores=[0, 0]", and
    the log shows "[Charge: No enemies within the 3" melee range.]"); `--rule-coverage FdgRaylib/Assets/Books`
    "Total references: 22079 / Dead: 0". (One full-suite run hit `LobbyBotGreetingTests.MultipleBots_NeverPickTheSameLine`
    - a 5 s lobby-chat timing wait, unrelated; it passed 3/3 alone and the rerun was fully green.)
  - **Awaiting GUI hand-verify (Chris).** No unit test covers ImGui drawing. In a game with a melee unit:
    1. *Charge reach (the item's "3" reach ring").* Movement panel, single mode: drag a model's ghost toward an
       enemy. The yellow charge line from the ghost to the nearest enemy model appears once the ghost is within
       3" base-to-base (it was 2"), and the yellow "N can charge" count beside the enemy includes that model; both
       go away beyond 3". (There is no drawn 3" ring - see D6.)
    2. *Optional-charge band.* Commit a move ending about 2"-3" from an enemy: no burnt-orange forced-charge
       highlight or panel warning, and the action menu then offers Charge AND Pass (and Shoot, for a unit with
       guns). Then a move ending inside 1": the 1" band lights up hot, the "FORCED CHARGE" warning shows, and Pass
       is greyed ("Within 1" of an enemy - must charge ...").
    3. *Out of range.* End a move just beyond 3": Charge is greyed with "No enemies within the 3" melee range."
    4. *The melee itself.* Charge from about 2.5": the defenders pile in (up to 3") and every model within 3"
       strikes / may strike back.
  - **Deferred / handed on, named.** (1) D5: the learned Strategist leaf was trained on 2"-reach features - noted
    in #442. (2) D6: a drawn 1"-3" band outline is an owner question, not built. (3) D7: the tactical overlay's
    threat frontier excludes the strike range (pre-existing, never read the constant) - owner note.
- 2026-10-29: filed with the plan. Q4 answered the same day: horizontal 3", the 4" vertical convention stays.

## Decisions

- **D1 - the forced-charge band on a 3" strike range (2026-10-29).** The standoff stays 1": the 3.6 movement text
  is unchanged ("Units may only be within 1" of other units when charging"), so `ENEMY_STANDOFF_DISTANCE_INCHES`
  and the #206/#334/#390 obligation are untouched (3D base-to-base, strict < 1"; inside it Pass is greyed, and
  Shoot too while a charge is on offer). Only the band's OUTER edge moves with the strike range: a unit 1"-3"
  from an enemy may Charge but is not forced, may Pass, and keeps its shot (was 1"-2"). The GUI's drawn band is
  the 1" standoff and stays 1"; the "N can charge" count and the charge line read the strike constant, so they
  are now 3". Texts follow: the forced-charge warnings keep naming 1", the greyed Charge reason now names 3".
  Pinned by `MeleeRangeRuleIntegrationTests.ChooseAction_EnemyAtTwoAndAHalfInches_OffersCharge_AndStillPass`,
  `ForcedChargeUtilitiesTests.FindContacts_EnemyAtTwoAndAHalfInches_ChargeableButNotForced` and
  `ForcedChargeBandTests.ForcedChargeWarning_NamesTheOneInchStandoff_NotTheStrikeRange`.
- **D2 - vertical stays 4" (2026-10-29).** Owner's answer to plan Q4. Pinned at the new horizontal edge
  (`VerticalMeleeRangeTests`: 2.5" across + 4" up in, + 4.5" up out).
- **D3 - Tactician corridor pin re-pinned (2026-10-29).** `TacticianCoverHabitTests` cases 14 and 17 (covered side
  in sword reach must lose to the open side out of it) failed: the open endpoint was 15.4" from where the
  projected-melee forecast puts the swordsmen (one rush, onto our start), 0.4" outside the old 15" edge (charge
  12 + strike 2 + the planner's 1" slack) and 0.6" inside the new 16". So under 3.6 the "open" side really was
  in reach next round, and the scene, not the planner, had to move. `CorridorScene` now starts us 2" straight
  back, at (24,8): the open endpoint is 17.3" away again; swordsmen, endpoints and walls unchanged, and the start
  stays on x=24 so the two endpoints' objective progress is still identical. All 11 cases green. No planner
  weight or code changed.
- **D4 - the greyed Charge reason names the range (2026-10-29).** A small addition beyond the item text: the
  number a player has to close to changed, and "No enemies within melee range." did not say what it is. Reads the
  constant, ASCII, nothing parses the string (grepped).
- **D5 - the learned Strategist leaf (2026-10-29) - OWNER NOTE, handed to #442.** `PositionEncoder.CheapThreatReach`
  (charge budget + strike range) feeds the shipped `MlpPositionEvaluator` net's threat features (and
  `RoundEndProjection`). The net was trained when that read +2"; it now reads +3" for melee units. Same schema,
  so `StrategistLeafAssetTests` is green and nothing throws, but the inputs drifted. Only the Strategist profile
  uses the net (the plain Tactician and the benchmark opponent are hand-weighted). Retrain / re-measure belongs
  with #442's 3.6 bench; recorded there.
- **D6 - there is no drawn 3" reach ring (2026-10-29) - OWNER QUESTION, not built.** The Acceptance asks to "see the
  3" reach ring", and the survey row calls `GuiDefineMovementResolver.cs:2528` "the GUI reach rings", but what the
  movement overlay draws for the strike reach is the yellow charge line and the "N can charge" count, both on the
  constant. The only ring around enemies is the 1" forced-charge band. Options: (a) leave it - the line and count
  already say "you can charge this" exactly where the gate does; (b) add a faint outline at 3" around reachable
  enemies with `ModelBaseRenderer.DrawBandOutlineImGui`, mirroring the 1" band (about 20 lines; needs a hue that
  does not collide with the charge-distance orange or the standoff burnt orange). Recommendation: (a), and (b)
  only if the hand-verify finds the 1"-3" band hard to read. A new UI element needs sign-off per CLAUDE.md, so it
  was not built.
- **D7 - the tactical overlay's threat frontier (2026-10-29) - OWNER NOTE.** The app's F-key threat frontier
  (`RulesProbe.ThreatReach` -> `ThreatDisc.ChargeRadius`) draws an enemy's charge distance + base radii, without the
  strike range, while the AI's `MeleeThreatReach` (and the engine's beyond-Rush rule) let a charge end up to 3"
  short of contact. Pre-existing (it understated by 2" before), by spec (`docs/tactical-overlay-plan.md` C2), and
  it never read the constant, so #417 left it alone. If the owner wants the frontier to mean "can fight me from
  here", it is one `+ GameWideConstants.MELEE_RANGE_INCHES_HORIZONTAL` in `RulesProbe.ThreatReach`.
- **Observation (no action).** The engine still has no charger-into-contact step: a Charge is a Move that ends
  within strike range, then the melee, then the defenders pile in. Under 3.6 that gap can be up to 3", which is
  exactly the pile-in cap, so on open ground the defenders still close it. Unchanged by this item.

## Outcome

Done 2026-10-29 on `feature/opr-3.6` (engine `ee15a968`; see the Notes entry). Models within 3" (horizontal,
base-to-base; 4" vertical unchanged) strike, may be charged and may strike back, through the one constant; the
1" standoff and the pile-in / consolidation figures were already 3.6's. The optional-charge band is 1"-3". Open:
Chris's GUI hand-verify; D5 handed to #442; D6 and D7 for the owner.

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
