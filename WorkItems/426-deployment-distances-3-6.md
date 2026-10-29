# 426 — Deployment distances 3.6: Scout, Vanguard, Fanatic, Reinforcement, Ambush objective lockout

**Status**: todo
**Related**: #415, #031 (Scout/Ambush), #214/#269 (reposition placements and reach rings), #263 (off-table units), #288 (late-deploy panel)
**Branch**: `feature/opr-3.6`. Model/effort: Opus 5.5 / medium.

## Goal

Core Rules v3.6.0:
- Scout: "May be set aside before deployment. After all other units are deployed, may be deployed anywhere
  fully within 9" of their deployment zone, over 9" away from non-Scout enemy units. Players alternate in
  placing Scout units, starting with the player that activates next." (3.5: within 12" of the zone, no
  non-Scout clause.)
- Ambush: unchanged text; verify the engine already enforces "can't seize or contest objectives on the round
  they deploy" and "players alternate [...] starting with the player that activates next".
- Book rules: Vanguard and Fanatic: "After this model is deployed, it may be placed anywhere fully within 9" of
  its position, over 9" away from non-Vanguard enemy units." (new clause); Reinforcement: patch notes say
  reinforcements are "placed within 9" of table edges and over 9" from enemies" - read the 3.6 definition in
  the Ratmen books; Spawn/Split: "cannot capture objectives in the round they are placed" (patch notes) -
  read the 3.6 texts; Infiltrate/Teleport: compare 3.5 vs 3.6 text in the census before touching.

"Done" = each placement validator and its GUI reach ring/zone shading use the 3.6 distances and the
"non-Scout"/"non-Vanguard" enemy filters; the objective lockout exists for Ambush, Spawn and Split.

## What changes

1. Engine: Scout deployment validator (zone + 9" from the deployment zone edge, 9" from non-Scout enemies);
   Vanguard/Fanatic reposition validators gain the enemy-distance clause; Reinforcement placement; a
   "placed this round" token that `ReconcileObjectivesStage` treats like Ambush's lockout.
2. GUI: the placement overlay's allowed-area field (#230) and reach ring (#214) read the same validator
   constants; CLI messages.
3. Tests: Scout at 9" from zone OK, 9.5" rejected, 8" from a non-Scout enemy rejected, 8" from a Scout enemy
   OK; Vanguard enemy clause; Spawn lockout.

## Acceptance

Tests green; hand-verify (Chris): deploy a Scout unit and see the 9" band.

## Code anchors (survey 2026-10-29)

- `CoreRuleCatalog.Scout` (~line 1798), `.Ambush` (~1817), `Rules/Dispatch/ReserveRules.cs`,
  `AmbushArrivalRules.cs`, deployment stages under `StateMachine/MapSetupStage/`, `ReconcileObjectivesStage`.
  See the appended survey rows below for exact lines.

### Survey rows (code survey 2026-10-29)

Findings that shape the item: Scout places into the deployment zone extended 12" toward the centre, in list
order, with no enemy-distance check; Ambush arrivals loop armies/units in store order. Neither implements
"players alternate [...] starting with the player that activates next" (in both 3.5 and 3.6 text) - decide
whether this item implements the alternation or records it as a deliberate deferral. The Ambush objective
lockout already exists (`ArrivedFromReserve` token).

- Scout `CoreRuleCatalog.cs:1798-1808` (`DeferDeployment(AfterNormalDeployment, 12f)`); placement
  `StateMachine/DeploymentStage/DeployAllUnitsStage/PlaceDeferredUnitsStage/PlaceDeferredUnitsStage.cs:33-60`,
  `BuildForwardZone :67-88`; deferral `DeploymentTurnContext.cs:104-110`; records `RuleOperation.cs:1086-1097`,
  `Effect.cs:1392-1400`; AI `TacticianPlaceObjectsResolver.cs:24`.
- Ambush `CoreRuleCatalog.cs:1817-1827` (`DeferDeployment(LaterRound, 9f)`; MinArrivalRound `RuleOperation.cs:1089`);
  arrival `StartOfRoundExtraActionStage.cs:100-144` `BringOnReserves` (:117-121 offer, :133 token stamp),
  `PlaceFromReserve :248` (:255 keep-out discs); lockout `ReconcileObjectivesStage.cs:146`.
- Vanguard `CoreRuleCatalog.cs:1527`, Fanatic :1549, Teleport :1506 (all-models gated; #428).
- Tests: `ScoutRuleIntegrationTests.cs`, `AmbushDeploymentChoiceTests.cs`, `RuleOperationDescribeTests.cs:197`,
  `AmbushRuleIntegrationTests.cs`, `ObjectiveOwnershipTests.cs`, `AmbushArrivalConstraintTests.cs`,
  `RapidAmbushRuleIntegrationTests.cs`, `AmbushRedeployRuleIntegrationTests.cs`.

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
