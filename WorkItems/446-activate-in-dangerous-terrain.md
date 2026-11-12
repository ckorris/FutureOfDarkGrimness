# 446 — Units that activate in Dangerous terrain take the test

**Status**: done 2026-10-30 (owner review asked on D1, D2, D3, D4; GUI hand-verify pending). Engine `0b233b0a`; the
superproject commit `446: ...` bumps it with the app text, the probe scenario, docs and this ledger. Filed 2026-10-29 from
#420's D5 (Chris agreed via plan Q10 that it gets its own number).
**Related**: #420 (dice count; landed), #419 (wounds taken this activation), #427 (Flying ignores Dangerous), #248/#408
(pristine back-out), #415, #002 (terrain), #426 (deployment placements into terrain), #442 (AI parity), #443 (terrain 3.6)
**Branch**: `feature/opr-3.6`. Model/effort: Opus 5.5 / medium. Schedule after phase 2.

## Goal

Core Rules v3.6.0, Dangerous Terrain: "Units with models moving across Dangerous terrain, OR THAT ACTIVATE
IN IT, must roll as many dice as the number of wounds it would take to fully destroy them, and for each roll
of 1 the unit takes one wound." The Beginner's Guide: "If any model in a unit moves into or through Dangerous
terrain, or activates in it, then the entire unit must take a Dangerous Terrain Test." Today only a move
whose path crosses Dangerous terrain (and the "counts as Dangerous" debuff) rolls; a unit that Holds, or
shoots in place, while any model stands in Dangerous terrain never tests. The clause was in 3.5 too, so this
is a pre-existing gap surfaced by #420.

"Done" = at activation, if any living model of the activated unit is inside Dangerous terrain, the unit takes
one Dangerous Terrain Test (the #420 pool) once, before its action; a unit that then also moves through
Dangerous terrain does not test twice for the same activation (decide: one test per activation, or one for
activating-in plus one for the move? Read the text: "moving across ... or that activate in it" reads as one
test per activation on either trigger - record the ruling); Flying / Strider scopes apply as they do for the
move test; a unit deployed into Dangerous terrain (Ambush/Scout/teleport) is covered by its first activation.

## What changes

1. Engine: a start-of-activation hook (the #408 activation-start path or `ChooseActionStage` entry) that checks
   model footprints against Dangerous terrain and runs `DangerousTerrainBatch` once; a per-activation marker so
   the movement test is skipped if the activation test already fired (or vice versa, per the ruling).
2. Presentation: the dice row and log line from #420 ("dangerous terrain test - N dice (wounds to destroy)")
   with the trigger named ("activated in").
3. Tests: Hold in Dangerous terrain tests once; move out of it tests once (not twice); a unit adjacent but not
   inside does not test; Flying ignores; a seeded headless game with a unit parked in Dangerous terrain.

## Code anchors

- `MovementStage/MovementExecutor.cs` (`RollDangerousTerrain`, `RollForcedDangerousTerrain`), `Rules/Dispatch/
  DangerousTerrainBatch.cs` (#420's shared roll), `ApplyNonMovementTerrainEffectsStage.cs` (rolls for the move's
  path only), `IUnitExtensions.GetWoundsToDestroy()`, `TerrainData.cs:78-82` (point-in-terrain), `MovementRuleQueries.
  IgnoresAllTerrain`.

## Notes

- 2026-10-30: **done.** Engine commit `0b233b0a` on `feature/opr-3.6` (the stage, the marker, the move-side gate, the
  presentation, 17 integration tests); one superproject commit bumps the pointer with the app side (GUI move-panel
  line, terrain-picker text), `docs/EngineNotes.md`, the `Scenarios/446-activate-in-dangerous.json` probe and this
  ledger.
  - **Engine.** New leaf stage `ActivatedInDangerousTerrainStage` is `MainUnitActionStage`'s starting child (D1). It
    calls `MovementExecutor.RollActivationDangerousTerrain(ctx, unit, terrain)`: if any LIVING model's base overlaps a
    `Dangerous` piece where it stands (`MovementUtilities.IsModelInDangerousTerrain`, D3), the unit rolls the #420
    pool once (`DangerousTerrainBatch` via the existing `RollBatch`) and the wounds land through the existing
    `ResolveDangerousTerrain` (dice row, casualty beats, destruction seam, #419 wound ledger). Exempt: dead, embarked,
    off-table Aircraft, `IgnoresAllTerrain` (Flying, Aircraft); Strider tests (D5). A rolled test marks the activation
    irreversible (D6); a unit it destroys leaves through `OnUnitDestroyed` straight to the end-of-activation reconcile
    (D7).
  - **One test per activation (D2).** `Rules/Dispatch/DangerousTerrainTestedThisActivation` - a
    `TokenType.DangerousTerrainTestedThisActivation` flag (invisible; RoundEnd backstop), set by both trigger seams when
    they roll (the activation test and `MovementExecutor.RollDangerousTerrain`, which covers the normal move and the
    out-of-band `TryMove`) and read by both, which skip the test when it is set. Swept table-wide at activation start
    (`MainUnitActionStage.Enter`, next to #419's ledger sweep) and end (`ReconcileEndOfActivationStage`). The forced
    tests (Dangerous Terrain Debuff (Immediate), spillout) neither read nor set it.
  - **Presentation (D8).** Dice row context "Unit: activated in Dangerous terrain - 5 dice (wounds to destroy)"
    (label stays "Dangerous Terrain"); log/CLI line "Unit: dangerous terrain test (activated in Dangerous terrain) - 5
    dice (wounds to destroy), 1 wound(s) dealt."; a suppressed move logs "Unit: already took its dangerous terrain test
    this activation - no second test for this move."; the GUI move panel replaces its dice hint with "no test on
    commit, the unit already took its Dangerous terrain test this activation" when the unit is marked; the
    terrain-placement text reads "a unit moving through it, or activating in it, rolls ... (once per activation)". The
    CLI movement resolver has no Dangerous hint, so nothing changed there. All ASCII.
  - **AI (D9).** No change: the Tactician and the Combat Calculator have no Dangerous-terrain term at all (grep of
    `Ai/`, `Calculator/`, `FdgRaylib/Rendering/CombatCalc/`), so a unit parked in Dangerous terrain is not priced as "will test on activation" and is not
    moved out pre-emptively; the search's rollouts run the real stages (`SimulationService` drives
    `DeterminePlayerTurnStage`), so searched lines do include the activation test. Hand-off to #442.
  - **Tests.** `ActivateInDangerousTerrainRuleIntegrationTests` (17, ledger below), most through the real
    `MainUnitActionStage` graph. Engine suite 3956 -> 3973 passed (1 skipped as before); `FdgRaylib.Tests` 3104 -> 3104
    (no app test touches ImGui text). Every test was broken on purpose and seen failing (16 sabotages: the stage
    unwired, the move-side marker check removed, either sweep removed, centre-point and inflated-radius inside tests,
    the Flying waiver removed or swapped for the Strider query, the embark exemption removed, the irreversible mark
    removed, the destroyed exit rerouted, the trigger text dropped, the wound ledger cleared after the test, counts-as
    as an activation trigger, the move test not marking, "always inside"), then restored.
  - **Verification.** Engine suite green; `FdgRaylib.Tests` green; full `dotnet build` 0 errors; `--validate-rules`
    OK 294 definitions; `--rule-coverage` unchanged (Dead: 36, 36 no-definition across 2 names); headless smoke
    `printf "2\n2\n" | ... --headless` exit 0 with `Game result:`; the probe
    `--headless --scenario Scenarios/446-activate-in-dangerous.json` exit 0: Lava Squad and Strider Beasts (6 dice,
    Tough(3) x2) test at activation and their moves out log the "no second test" line, the AI's Mire Raiders test at
    activation, Flying Scouts do not (and keep Back on their menu), Edge Squad (0.7in from the edge) does not test at
    activation and takes the ordinary move test when its move crosses the piece.
  - **Deferred, named.** (1) Disembarking into Dangerous terrain does not test at the disembark (`DisembarkStage` is a
    placement; 3.6 Transport says units "exit by using any move action") - pre-existing, outside this clause; such a
    unit now tests at its next activation. Not filed (no number taken); recorded here for #442/#445 to pick up if
    wanted. (2) AI pricing of standing in Dangerous terrain -> #442 (hand-off written). (3) #443 (deferred terrain
    types): Unstable's "counts as Dangerous" must ride the move path, not the `Dangerous` flag, or it would trigger
    the activation test - hand-off written there.
- 2026-10-29: filed. See `WorkItems/420-dangerous-terrain-dice.md` D5 for the discovery.

## Ledger (facet -> code anchor -> test)

| Facet | Code anchor | Test (`ActivateInDangerousTerrainRuleIntegrationTests`) |
|---|---|---|
| Hold in Dangerous terrain tests once, before the menu; trigger named on dice row + log | `ActivatedInDangerousTerrainStage.Enter`; `MovementExecutor.RollActivationDangerousTerrain`, `ACTIVATED_IN_TRIGGER`, `DescribeDice` | `Hold_InDangerousTerrain_TestsOnce_BeforeTheMenu_AndNamesTheTrigger` |
| A rolled test closes the pristine back-out (D6) | `ActivatedInDangerousTerrainStage` -> `MarkIrreversibleAction` | `Hold_InDangerousTerrain_TheActivationCanNoLongerBeBackedOut` |
| Outside the piece: no test, activation stays pristine | `RollActivationDangerousTerrain` (inside check) | `Hold_OutsideDangerousTerrain_NoTest_AndTheActivationStaysPristine` |
| Move out after the activation test: one test, not two (D2) | `RollDangerousTerrain` marker gate; `DangerousTerrainTestedThisActivation` | `MoveOutAcrossTheSamePiece_TestsOnce_NotTwice` |
| The move test alone still rolls, unchanged row, and marks | `RollDangerousTerrain` -> `Mark` | `TheMoveTestAlone_StillRolls_ForAUnitThatActivatedOutside` |
| Counts-as move after the test: no second test (D4) | `RollDangerousTerrain` marker gate | `AfterTheActivationTest_ACountsAsDangerousMove_DoesNotTestAgain` |
| Marker swept at activation start | `MainUnitActionStage.Enter` | `AMarkerLeftOverFromAnEarlierActivation_IsSwept_SoTheUnitStillTests` |
| Marker swept at activation end, table-wide | `ReconcileEndOfActivationStage.Enter` | `TheEndOfTheActivation_SweepsTheMarker_OnEveryUnit` |
| Adjacent (base short of the edge) is not in it (D3) | `MovementUtilities.IsModelInDangerousTerrain` | `ABaseThatStopsShortOfTheEdge_IsNotInIt` |
| One model's base poking in puts the unit to its full pool (D3) | `IsModelInDangerousTerrain` (static swept-base pose) | `ABaseThatPokesIntoThePiece_IsInIt_EvenIfOnlyOneModelDoes` |
| Flying ignores; plain control tests | `RollActivationDangerousTerrain` -> `IgnoresAllTerrain` | `Flying_IgnoresIt_APlainUnitDoesNot` |
| Strider does not ignore | (Difficult-only scope) | `Strider_DoesNotIgnoreIt` |
| Embarked unit does not test (D5) | `RollActivationDangerousTerrain` -> `TransportUtilities.IsEmbarked` | `AnEmbarkedUnit_DoesNotTest_ItsModelsAreNotOnTheTable` |
| Deployed into Dangerous terrain: tests at first activation | starting child of `MainUnitActionStage` | `AUnitDeployedIntoDangerousTerrain_TestsAtItsFirstActivation` |
| Counts-as grant is not an activation trigger (D4) | `RollActivationDangerousTerrain` (physical pieces only) | `ACountsAsDangerousGrant_IsNotAnActivationTrigger` |
| Destroyed by the test: activation ends, no menu (D7) | `ActivatedInDangerousTerrainStage.OnUnitDestroyed` -> reconcile | `AUnitDestroyedByTheTest_EndsItsActivation_WithNoMenu` |
| Test wounds count in #419's melee result (charge out of it) | stage runs after `WoundsTakenThisActivation.SweepAll` in `MainUnitActionStage.Enter`; wounds land via `CasualtyPresentation` | `AChargeOutOfDangerousTerrain_TheTestsWoundsCountInTheMeleeResult` |
| GUI move-panel hint / terrain-picker text | `GuiDefineMovementResolver.DrawInfoPanel`, `TerrainEffectText` | GUI hand-verify (no ImGui text tests) |

## GUI hand-verify (Chris)

Launch `dotnet run --project FdgRaylib/FdgRaylib.csproj -- --scenario Scenarios/446-activate-in-dangerous.json`
(Realistic dice, seed 446; you are the Lava Walkers):
1. Activate **Lava Squad** (parked in the lava strip): before the action menu, a "Dangerous Terrain" dice row with
   the line "Lava Squad: activated in Dangerous terrain - 5 dice (wounds to destroy)"; the console log line
   "Lava Squad: dangerous terrain test (activated in Dangerous terrain) - 5 dice ..."; the action menu has no Back.
2. Choose Move and drag the squad out across the strip: the panel's red line reads "no test on commit, the unit
   already took its Dangerous terrain test this activation"; on Done no second dice row, and the log says "already
   took its dangerous terrain test this activation - no second test for this move."
3. **Strider Beasts**: dice row with 6 dice (two Tough(3) models). **Flying Scouts**: no dice row, Back still offered.
   **Edge Squad** (just outside the left edge): no dice row on activation; a move through the strip shows the
   ordinary move row "Edge Squad: 3 dice (wounds to destroy)" and the usual panel hint.
4. The AI's **Mire Raiders** (in the lava pool on the far side) roll their row when they activate.
5. Terrain placement screen (any new game): the Dangerous effect line reads "... a unit moving through it, or
   activating in it, rolls one die per wound ... (once per activation); each 1 is a wound."

## Decisions

- **D1 (OWNER REVIEW) - where the test runs: first thing in the activation.** `ActivatedInDangerousTerrainStage` is
  `MainUnitActionStage`'s starting child, ahead of `ActivationStartStage` and Surprise Attack. "Activate in it" is
  read as where the unit stands when its activation BEGINS, so: an activation-start reposition (Wolfborn / Bounding /
  Rapid Blink) can neither dodge the test by placing out nor cause one by placing in (a placement is not a move
  across); a Shaken unit - which still activates, and idles - tests; and a unit the test destroys never sees the
  activation-start prompts. It runs after `MainUnitActionStage.Enter`'s #419 wound-ledger sweep, so its wounds count
  as "taken during this activation" for a later melee result. Alternative: run it after the activation-start stages,
  just before the menu (`ChooseActionStage` entry) - a Wolfborn unit could then hop out first, and one that placed in
  would test.
- **D2 (OWNER REVIEW) - one test per activation, on either trigger.** "Units with models moving across Dangerous
  terrain, or that activate in it, must roll ..." (and the Beginner's Guide: "If any model in a unit moves into or
  through Dangerous terrain, or activates in it, then the entire unit must take a Dangerous Terrain Test") names two
  triggers for ONE test, so the per-activation marker makes whichever fires first the activation's test: a unit that
  tested on activating does not test again moving out across the piece (which, before #446, was itself a test, since
  a first leg starting inside the piece counts as crossing it), and - the symmetric half - a second move across
  Dangerous terrain in the same activation no longer tests twice (before #446 each move tested; reachable only with a
  normal move plus a rule-triggered move in one activation). The marker is swept table-wide at both ends of every
  activation, so a unit moved during someone else's activation (a post-melee Harassing move) tests on that move and
  carries nothing into its own activation. Forced tests (Dangerous Terrain Debuff (Immediate): "must immediately take
  a Dangerous Terrain test"; transport spillout) are separate rules and ignore the marker. Alternative: two tests (one
  for activating in it, one more for the move out) - a unit standing in lava would pay double to leave it, which reads
  against the single "must roll" sentence; it is a one-line change (drop the marker read in `RollDangerousTerrain`).
- **D3 (OWNER REVIEW) - "in it" = any part of a model's base overlapping the piece.** The move test's own footprint
  rule (#150: base overlap, not the centre, counts as crossing), applied as a zero-length sweep at the model's
  position and resting facing (`MovementUtilities.IsModelInDangerousTerrain`); any one living model in it puts the
  whole unit to the test, as one crossing model does on a move. A base that stops short of the edge is not in it.
  Alternatives: the model's centre inside the piece, or "fully inside" (the Cover wording) - both would let a unit
  hug the inside edge of a lava pool for free, and would disagree with the move test about the same base on the same
  ground.
- **D4 (OWNER REVIEW) - the "counts as being in Dangerous Terrain" grant.** The deferred Dangerous Terrain Debuff
  ("counts as being in Dangerous Terrain once (next time the effect would apply)"; the supplement's granted rule reads
  "on its next move") stays a MOVE effect: it is not an activation trigger, and a unit carrying it that never moves is
  untouched, as before (#153/#197 P8 shape). A counts-as move after an activation test is the same test, so the
  marker suppresses it; `ExecuteMoveStage` still spends the one-shot grant on that move. Alternatives: (a) treat the
  grant as "activating in it" too (the debuffed unit tests at the start of its next activation, grant spent there) -
  arguably what "next time the effect would apply" means under the 3.6 clause, and stronger for the debuffer; (b) let
  the grant bypass the marker (a second test).
- **D5 - who is exempt.** Flying and Aircraft (the `IgnoresAllTerrain` scope; #427 and the 2026-07-28 owner ruling:
  one waiver for every Dangerous path); Strider is Difficult-only and tests. An embarked unit (its models are parked
  at the origin, not on the table; the transport tests for itself), an Aircraft that flew off the table, and a unit
  with nothing alive do not test.
- **D6 - a rolled test commits the activation.** The #248/#408 pristine back-out would let a player un-pick the unit
  and re-pick it to re-roll, so the stage calls `MarkIrreversibleAction` whenever it rolls (all-safe included); the
  menu then offers no Back. A unit that does not test keeps the back-out.
- **D7 - a unit the test destroys** goes straight to `ToReconcileEndOfActivation` (no activation-start offers, no
  menu; the reconcile already skips a dead unit's end-of-activation abilities, and the unit is marked activated) with
  the log line "Unit was destroyed by Dangerous terrain - its activation ends." The move-test death path, by
  contrast, returns to the menu, which auto-passes a unit with nothing to do; the activation test cannot rely on that,
  because a dead unit that has not moved is still offered Move.
- **D8 - presentation.** The dice-row label stays "Dangerous Terrain" (one vocabulary for every Dangerous row); the
  trigger rides the context line and the log line, so a roll that fires before the unit has done anything reads as
  the rule rather than a phantom. A move or forced row is unchanged. The GUI move panel reads the marker directly from
  the unit's tokens (no request field, so nothing new on the wire).
- **D9 - AI: parity only, nothing to change.** No estimator prices Dangerous terrain (move or activation); rollouts
  run the real stages and so include the activation test. Pricing goes to #442 (hand-off written there).

## Outcome

A unit whose living model's base overlaps a Dangerous piece when its activation begins takes the #420 Dangerous
Terrain Test first thing (Flying/Aircraft, embarked and off-table units exempt; Strider tests), with the trigger
named on the dice row and the log line; a unit the test destroys ends its activation cleanly; the wounds count for
#419's melee result. The two triggers share one test per activation (a marker swept at both ends of every
activation), so moving out across the piece afterwards - or a second move in the same activation - does not test
again. GUI move panel and terrain text follow. Owner review asked on D1 (placement), D2 (one test per activation),
D3 (footprint), D4 (counts-as grant). Deferred, named: disembarking into Dangerous terrain (pre-existing, not filed),
AI pricing (#442), Unstable terrain's interplay (#443). Engine `0b233b0a`.

## Rules for the agent (plan section 1, restated)

- Branch `feature/opr-3.6` in BOTH repos; never commit, merge or push `master`. Submodule-first cadence;
  subjects start with `446:`. 3.6 text from `~/Projects/GDF Armies/Rules 3.6.0/` only; no 3.5 fallbacks.
- Vertical slice (engine + CLI/GUI presentation + AI legality + integration test); verify per CLAUDE.md plus
  `--rule-coverage` non-regression; never commit red; keep this file and the plan's status table current.
