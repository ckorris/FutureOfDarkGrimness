# 426 — Deployment distances 3.6: Scout, Vanguard, Fanatic, Reinforcement, Ambush objective lockout

**Status**: done 2026-10-30 (owner review asked on D1, D2, D3, D4, D5, D6; GUI hand-verify pending)
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

- 2026-10-30: done. Engine commits on `feature/opr-3.6`: `59d55cb8` (Scout 3.6), `70a3d642` (Scout and Ambush
  alternation), `bf1ba0a7` (Vanguard and Fanatic 3.6), `5e9ddb4b` (Reinforcement 3.6, Spawn/Split lockout),
  `a135f61f` (Ambush 3.6 wording); one superproject commit bumps the pointer with the app side (CLI/GUI keep-out
  wording via `PlacementKeepOutText`, the AoF Drakesworn re-author, the GDF Reinforcement/Spawn/Split texts, two app
  test fixtures, docs, ledger). Engine suite 3805 -> 3829 passed (1 skipped); app suite 3086 -> 3090;
  `--validate-rules` 294 definitions OK; `--rule-coverage` unchanged (Dead 36, the two Counter-Attack names); headless
  smoke exit 0 (the built-in army's Vanguard Warriors and Scout Heavy Gunners both exercise the new paths). Every new
  test was broken on purpose and seen failing (exemption set, clearance, zone range, the AI's disc check, the side
  check, alternation parking and turn-ending, the lockout stamp, the reinforcement band and enemy minimum, the AI
  reposition policy and its keep-out check, the CLI wording and auto-placer check, the Drakesworn data), then restored.
- 2026-10-30: 3.6 text read (core text extract, common-rules JSON, 50 GDF + 40 AoF snapshots). Scout, Ambush:
  common rules. Vanguard (33 books), Fanatic (Soul-Snatcher Cults), Drakesworn (Volcanic Dwarves, AoF) carry the SAME
  text, "non-Vanguard" in all three. Reinforcement (Ratmen Clans, Ratmen): "fully within 9" of any table edge, over 9"
  away from enemy units" - the patch notes are right. Spawn (10 books) / Split (Daemons of Change): the objective
  clause is new. Teleport and Infiltrate: no 3.6 change in the census, not touched (see D11). Pre-existing, not changed:
  deployment zones are 9" deep (`DEPLOYMENT_DISTANCE_INCHES`, rules say 12", deferred since #001), so the Scout band is
  0-18" here, still short of the midline clamp (24").
- 2026-10-29: filed with the plan.

## Decisions

- **D1 (OWNER REVIEW) - Vanguard is now a placement.** The text has said "placed anywhere fully within 9" of its
  position" since 3.5, but the engine moved the unit up to 9" (a triggered move through the movement subsystem), and a
  move request has no way to carry 3.6's "over 9" away from non-Vanguard enemy units". Vanguard now shares Fanatic's
  `RepositionOnDeploy` placement (per-model 9" radius, Yes/No then a cancellable placement, keep-out discs). Visible
  consequences: nothing is asked of the path (a placement may cross terrain/enemies, like Fanatic and Teleport), and the
  GUI shows reach rings instead of the movement overlay. Alternative: keep the move and teach `DefineMovementPathRequest`
  and its four resolvers and validators a destination keep-out.
- **D2 (OWNER REVIEW) - Fanatic's "non-Vanguard" taken literally.** Fanatic's and Drakesworn's 3.6 text is word for
  word Vanguard's, so an enemy unit WITH VANGUARD does not push a Fanatic unit back, and an enemy Fanatic unit does
  (`ExemptEnemyRule: "Vanguard"`). It may be an OPR copy-paste for "non-Fanatic"; the alternative is one data change
  (exempt the bearer's own rule, or both).
- **D3 (OWNER REVIEW) - who counts as a "non-Scout" / "non-Vanguard" enemy.** Scout: an enemy unit set aside by the
  same Scout pass (the `AfterNormalDeployment` deferred set) is exempt - i.e. units that deployed AS Scouts. A unit whose
  joined hero has Scout deploys normally in this engine and so pushes back. Vanguard: there is no pass to read, so it is
  by rule name on the unit, any of its models (a joined hero) or a rule grant (aura), canonical or book-requested name
  (`EnemyKeepOutRules.CarriesRuleNamed`). Alternative for Scout: the same by-name test.
- **D4 (OWNER REVIEW) - keep-outs measure centre to centre.** The discs use the convention every placement keep-out
  already uses (`PlacementDisc`, Ambush's "over 9" from enemies"), exclusive at the boundary (exactly 9.0" is legal), so
  Scout, Vanguard, Reinforcement and Ambush agree. OPR measures base to base, which would push every one of them out by
  the two base radii; changing the convention is engine-wide (Ambush, Repel, Beacon too). Recommendation: its own item
  if wanted.
- **D5 (OWNER REVIEW) - the Scout band is the zone extended toward the centre only.** Exact for the 2-team full-width
  zones; for the split 3-4 team zones it omits the lateral 9" and the rounded corners "within 9" of the zone" would add
  (a conservative subset - never an illegal spot). The pre-existing midline clamp stays (it never binds with 9" zones).
  Alternative: a rounded-rectangle zone type (new serializable zone, renderer case, containment test).
- **D6 (OWNER REVIEW) - how the alternation runs.** One unit per turn, players in activation order with the same
  `TeamPlayerAlternationCursor` the round uses (teams alternate, players round-robin within a team, anyone out of units
  skipped): the deployment roll order for Scout (round 1 activates in it), this round's `TeamActivateOrder` for Ambush.
  Within a player, units come in army-list order: the player picks WHERE, not WHICH. For Ambush a turn walks the
  player's eligible units with the existing Yes/No: the first accepted (or mandatory) one is placed and ends the turn, a
  declined one stays in reserve for the round and is not asked again, declining everything left ends the player's round.
  No new request shapes, so CLI/GUI/AI are unchanged. Alternative: a per-turn "which unit" pick
  (`SelectionRequest<UnitData>`, which every front end and the AI already answer). The Ambush alternation shipped in
  this item (same mechanism), so no new number is needed.
- **D7 - the Spawn/Split lockout reuses `ArrivedFromReserve`.** Same semantics exactly ("can't seize or contest
  objectives on the round they are placed", cleared by the RoundEnd sweep after the objective check), already read by
  `ReconcileObjectivesStage` and by the Tactician's scoring (`TacticalAnalysis`). Stamped in `SpawnUnit`, the service
  both rules (and only they) use. Side effect: "Ambushing Piercing Shot" keys its bonus on the same token; no unit spawns
  with that rule in the corpus.
- **D8 - Reinforcement: 3.6 numbers, triggers unchanged.** The arrival is "fully within 9" of any table edge"
  (`ReinforcementEdgeBandInches`) and "over 9" away from enemy units" (the flat `MinDistanceFromEnemiesInches`, every
  enemy, no exemption). The 3.6 rewording of the trigger (Shaken -> may remove as destroyed; destroyed -> may place a copy
  next round) behaves as the engine's two arms already did. Seen, not changed: the 3.6 text still says "a unit where ALL
  models have this rule", while the shipped gate is #428's most-models (handed to #436).
- **D9 - AI parity for the deploy reposition.** Declining it (the solo bot's stance for every reposition) would have
  been a quiet regression, since the bot used to march its Vanguard move at the nearest enemy. A deploy-time reposition
  (`RepositionPlacement.DeployRepositionTaskName`) is now answered by a rigid shift toward the nearest enemy model (the far
  edge when none is on the table), the longest legal one in 0.5" steps (on the table, no overlap, no impassible terrain,
  outside the keep-outs, over the 1" standoff), else standing still. The Tactician inherits it. Smarter, objective-aware
  placement goes to #442 (hand-off there). Activation-time repositions are still declined.
- **D10 - Drakesworn re-authored here.** It is on #435's AoF list, but its 3.6 text is Vanguard's word for word, so it
  became Vanguard's exact effect (`AofRuleSupplement.json`; pinned equal to the catalog's). The bundled Volcanic Dwarves
  book embeds the 3.5 move until #438 re-bakes; saved armies keep theirs. Hand-off written in #435.
- **D11 - Teleport and Infiltrate untouched.** The census lists no 3.6 change for either. Seen while reading: the book
  Teleport is "fully within 3" ... when using Advance/Charge actions, or fully within 6" ... when using Rush actions",
  while the catalog Teleport is a flat 6" (pre-existing, not a 3.6 change; handed to #436).

## Ledger (facet -> code anchor -> test)

| Facet | Code anchor | Test |
|---|---|---|
| Scout 3.6 text, 9" zone + 9" enemy clause | `CoreRuleCatalog.Scout`; `Effect/RuleOperation.DeferDeployment.EnemyClearanceInches` | `ScoutPlacementRuleIntegrationTests.Catalog_Scout_CarriesThe36Wording_AndBothNineInchFigures`, `RuleOperationDescribeTests` (2 DeferDeployment cases) |
| Fully within 9" of the zone (9" OK, 9.5" refused) | `PlaceDeferredUnitsStage.BuildRequest/BuildForwardZone`; `PlacementUtilities.IsBaseWithinZone` | `ZoneHalf_BaseEdgeAtNineInches_Accepted_AtNinePointFive_Rejected`, `ScoutRuleIntegrationTests.ScoutUnits_AllPlacedForward_AfterNormalDeployment` |
| Over 9" from non-Scout enemies; enemy Scout exempt; own units don't count | `EnemyKeepOutRules.Discs`; request `EnemyKeepOutDiscs` / `EnemyKeepOutNote`; `PlacementDistanceRules` | `EnemyHalf_NonScoutEnemyAtEightInches_Rejected_EnemyScoutAtEightInches_Accepted`, `OwnNonScoutUnits_DoNotPushBack` |
| AI Scout placement legal | `AiPlaceObjectsResolver.BlockPenalty` (discs); `TacticianPlaceObjectsResolver.ScoutTaskName` | `AiPlacement_FindsTheLegalStrip_BehindAnEnemyLine` |
| Scout alternation from the player that activates next | `PlaceDeferredUnitsStage.Enter/PlaceOne`; `PlacementAlternation` | `PlacementAlternationRuleIntegrationTests.Scouts_Alternate_StartingWithThePlayerThatActivatesNext`, `Scouts_TheOtherRollOrder_FlipsWhoGoesFirst` |
| Ambush alternation; a decline is not a turn | `StartOfRoundExtraActionStage.BringOnReserves/TakeArrivalTurn` | `AmbushArrivals_Alternate_StartingWithThePlayerThatActivatesNext`, `AmbushArrivals_ADeclineIsNotATurn_AndIsNotAskedAgainThisRound` |
| Vanguard / Fanatic 3.6: placement, 9" radius, non-Vanguard clause, most models | `CoreRuleCatalog.Vanguard/.Fanatic`; `Effect.RepositionOnDeploy`; `RuleOperation.RepositionModels`; `RepositionPlacement.Offer/OfferFromOperations`; `DeployUnitStage` | `VanguardRuleIntegrationTests.Catalog_IsADeployPlacement_WithTheNonVanguardClause` (x2), `Reposition_NonVanguardEnemyAtEightInches_Rejected_EnemyVanguardAtEightInches_Accepted` (x2), `Reposition_ExemptionReadsAGrantedVanguard_Too`, `RuleOperationDescribeTests` (RepositionModels clause case) |
| AI takes the deploy reposition, stays legal | `AiPlaceObjectsResolver.AdvanceAfterDeploying`; `RepositionPlacement.DeployRepositionTaskName` | `Ai_TakesTheReposition_AndStopsOverNineInchesFromTheEnemy` |
| Drakesworn = Vanguard (AoF data) | `AofRuleSupplement.json` "Drakesworn" | `DeploymentDistancesShippedDataTests.Drakesworn_IsThe36VanguardPlacement` |
| Reinforcement 3.6 arrival band + enemy minimum | `StartOfRoundExtraActionStage.ReinforcementEdgeBandInches/ReinforcementEnemyClearanceInches`, `PlaceReinforcements` | `ReinforcementRuleIntegrationTests.TheCopy_ArrivesMandatorily_InTheEdgeBand_AfterAmbushers`, `TheCopy_AiPlacement_StaysInTheNineInchBand_AndOverNineInchesFromEnemies` |
| Spawn / Split can't seize or contest the round they are placed | `GameOperationServices.SpawnUnit` (`ArrivedFromReserve`); `ReconcileObjectivesStage.PlayersNearObjective` | `SpawnRuleIntegrationTests.ASpawnedUnitOnAnObjective_DoesNotSeizeItThisRound_DoesNextRound`, `SplitRuleIntegrationTests.AKillerlessDeath_StillOffersTheSplit_AndPlacesTheSuccessor` |
| Reinforcement / Spawn / Split 3.6 text | `GdfRuleSupplement.json` | `DeploymentDistancesShippedDataTests.Reinforcement_Spawn_Split_CarryThe36Text` |
| Ambush 3.6 text; lockout and over-9" arrival unchanged | `CoreRuleCatalog.Ambush`; `StartOfRoundExtraActionStage.PlaceFromReserve` | `AmbushRuleIntegrationTests.Catalog_Ambush_CarriesThe36Wording_AndItsShape` (+ the existing arrival/lockout tests, `ObjectiveOwnershipTests`) |
| CLI wording reads the stage's note; EOF auto-placer avoids keep-outs | `PlacementKeepOutText.Reason`; `PlaceObjectsResolver` (CLI) | `PlaceObjectsResolverKeepOutTests.ATypedSpotInsideAKeepOut_IsRefusedWithTheStagesWording`, `EofAutoPlacement_StaysOutOfEveryKeepOut` |
| GUI band, keep-out blob, reach ring, refusal reason | `GuiPlaceObjectsResolver.IsPlacementValid/TryGetEnemyExclusion/DrawReachRings/DrawZone` (all read the request) | (GUI hand-verify) |

## GUI hand-verify (Chris)

No scenario probe: scenarios start in a round, and these rules all live in deployment. Play a normal game; the built-in
test army (5x Warriors with Vanguard, 3x Heavy Gunners with Scout) covers both, or an Army Forge list with Shadow
Leapers (Alien Hives, Scout) and Errant Knights (Chivalrous Kingdoms, Vanguard) against an opponent with a few units.

1. **Scout band and enemy keep-out**: after normal deployment, the Scout placement shows the blue zone reaching 9" past
   your deployment zone (18" from your edge), and red keep-out blobs 9" around every enemy model that deployed normally,
   none around an enemy Scout unit already placed. A click inside a blob is refused with "Too close to a non-Scout
   enemy unit - Scout units must be set up over 9" away from them."
2. **Alternation**: with Scouts on both sides, the Scout placements alternate one unit at a time, starting with whoever
   won the deployment roll-off (the player who activates first in round 1).
3. **Vanguard**: after deploying a Vanguard unit, "Use Vanguard on X?" then a placement with green 9" reach rings and red
   blobs around non-Vanguard enemies (none around an enemy Vanguard unit); a spot inside a blob is refused with "Too close
   to a non-Vanguard enemy unit - must be placed over 9" away from them." Back leaves the unit where it deployed.
4. **Ambush (round 2+)**: with reserves on both sides, the "Deploy X from Ambush this round?" prompts alternate between
   the players, starting with the player who activates first that round.

## Outcome

Done 2026-10-30. Core Rules v3.6.0 deployment distances are implemented end to end: Scout (9" band, over 9" from
non-Scout enemies), Vanguard and Fanatic (and AoF Drakesworn) as placements with the non-Vanguard clause, Reinforcement's
9" edge band and 9" enemy minimum, and the Spawn/Split objective lockout; the long-missing "players alternate, starting
with the player that activates next" now runs for both Scout and Ambush. Each facet has an integration test; the CLI,
GUI and AI resolvers read the same request. Deferred / recorded: the lateral part of the Scout band for split zones
(D5), base-to-base keep-out measurement (D4), a "which unit" pick in the alternation (D6), objective-aware AI
repositions (#442), Teleport's 3"/6" text and Reinforcement's "all models" wording (#436). Awaiting Chris's GUI
hand-verify (above) and his answers on D1-D6.

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
