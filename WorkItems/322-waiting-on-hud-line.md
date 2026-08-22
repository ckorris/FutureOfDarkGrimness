# 322 — "Waiting on" line in the status HUD

**Status**: in-progress
**Related**: Was #318 pre-reconciliation-41 (that number stayed with the merged melee hold-back item); commit messages from before the renumber say #318.

## Goal
Restore visibility into what other players are doing while the local player waits (removed with the
old draggable "Outstanding Tasks" ImGui window). A second, smaller line under the top-center
`StatusHudOverlay` strip lists each *non-local* player's outstanding task ("<pip> Bob: Place Unit
Models"), player-color-coded, click-through, zero footprint when nothing is outstanding. Local
players' tasks are filtered out (the resolver panel already shows those); consequence: pure-hotseat
games never show the line, by design (agreed 2026-08-02).

## Notes
- 2026-08-02 (3): Applied the full audit - every stage name is now game-language gerund form.
  Non-discriminator TaskNames reworded in place (nothing app-side reads TaskName; the only
  string-matches are the three Tactician placement discriminators, confirmed by grep); shared
  class literals (YesNoRequest "Yes/No Question", StringSelectionRequest "Select Option",
  SingleBindingRequest "Select Item", Selection* "Select <T>") gained a displayName ctor param
  with per-site wording; Scout/Ambush keep their discriminator TaskNames and carry displayName.
  Final display strings (complete inventory): Choosing an Army; Placing Terrain (n of m) /
  (x of y points left); Placing Objective n of m; Choosing a Deployment Zone; Choosing a Unit to
  Deploy; Deploying [unit]; Deploying [unit] (Scout); Deciding Whether to Deploy [unit];
  Choosing a Unit to Activate; Deciding Whether to Delay Activation; Choosing an Action;
  Moving [unit] (advance/rush/charge + rule-triggered); Flying [unit]; Choosing a Transport for
  [unit]; Disembarking [unit]; Teleporting [unit]; Repositioning [unit]; Placing [unit] (summon);
  Placing Reinforcements ([unit]); Deploying [unit] from Ambush; Deciding Whether to Deploy [unit]
  from Ambush; Redeploying [unit] (aircraft + redeploy rule); Choosing a Unit to Redeploy;
  Choosing a Ranged Weapon; Choosing a Target; Choosing a Takedown Target; Choosing a Strafing
  Target; Choosing a Storm Target; Choosing a [rule] Target (surprise attack); Choosing a Melee
  Target; Choosing a Melee Weapon; Assigning Wounds; Consolidating After a Wipeout / After
  Disengaging; Deciding Whether to Strike Back; Deciding Whether to Use [rule]; Deciding Whether
  to Reactivate [unit]; Deciding Whether to Reinforce [unit]; Deciding on Regenerative Strength;
  Spending Target Markers; Choosing a Spell; Choosing a Target for [spell]; Assisting a Spell
  Cast; Choosing an Effect; Spilling Out [unit]. Suite 2595 green, headless smoke exit 0.
- 2026-08-02 (2): Playtest feedback - HUD showed raw TaskNames ("Select UnitData", "Place Unit
  Models"). Engine 38b40ca: `IStageTaskRequest.DisplayName` (defaults to TaskName; sender broadcasts
  it in the awaiting message) + optional `displayName` on Selection/CancellableSelection/PlaceObjects
  requests; deployment now shows "Choosing Unit to Deploy" / "Deploying [unit name]". TaskName kept
  as the machine identifier (TacticianPlaceObjectsResolver discriminates placement flavors by it).
  Suite 2595 green, headless smoke exit 0. A Sonnet audit catalogued every runtime TaskName with
  verdicts + proposed gerund replacements - table saved below; the worst offenders still unfixed:
  "Select UnitData" (activation pick, melee defender, embark, strafing, spell target...), "Select
  ModelData", "Select Option" (choose action / melee weapon / hold-or-deploy), "Yes/No Question"
  (~13 sites), "Select Item" (army pick), "Triggered Move", "Move Unit". Await user's pick on which
  renames to apply (all via displayName; the three discriminator TaskNames never need to change).
- 2026-08-02: Implemented. Engine d2aed58: `IFDGGame.LocalPlayerIDs` on both flavors +
  `LocalPlayerIDs_ExposedOnBothGameFlavors` test (suite 2594 green). App: `GuiOutstandingTaskDisplay`
  reworked into a read model (`GetWaitingOnOthers()`, local-ID filter, old ImGui Draw deleted);
  `StatusHudOverlay` grew the waiting lines (font 20, cap 3 + "+N more", dim prefix + player-colored
  name); renderer feeds it in `DrawStatusHud` and the commented-out old draw call is gone. Headless
  smoke exit 0. Remaining: hand-verify in a networked GUI game (line appears while the other player
  decides, disappears when resolved, absent in hotseat).
- 2026-08-02: Filed. Infrastructure survey: engine `OutstandingTaskLister` still streams
  `OutstandingTaskInfo` (works networked); `GuiOutstandingTaskDisplay` still subscribes and is wired
  end-to-end — only its draw call is commented out (`RaylibRenderer.cs` ~554). Plan: expose
  `LocalPlayerIDs` on `IFDGGame` (engine, exists privately), snapshot getter on the display class,
  thread local IDs through `GameGuiWiring`, render via extended `StatusHudOverlay`.

## Decisions
- Second HUD line over restoring the ImGui window (permanent chrome, screen cost), a console line
  (buried among log history), or change-toasts (it's a state, not an event).
- Filter local tasks: the line appears exactly when "why is nothing happening?" has the answer
  "someone else is deciding".

## Outcome
(open)

## TaskName audit (2026-08-02, Sonnet subagent; verbatim strings, non-Tests engine tree)

What the waiting HUD shows per request, after the deployment fixes. "Proposed" = suggested
displayName; TaskName itself never changes (three are AI discriminators, marked DISC).

| Shown today | Source | Moment | Verdict | Proposed |
|---|---|---|---|---|
| Choosing Unit to Deploy | ChooseUnitToDeployStage:107 | deployment pick | fixed | - |
| Deploying [unit] | DeployUnitStage:58 (DISC "Place Unit Models") | deployment placement | fixed | - |
| Select UnitData | Selection/CancellableSelectionRequest auto-name; sites: ChooseUnitToActivateStage:107, ChooseMeleeDefenderStage, StormStage:135, BeforeAttackActionStage:218, SurpriseAttackStage:160, CastSpellStage:578, EmbarkStage:131, StrafingStage:163, ReconcileEndOfActivationStage:157, ReDeploymentStage:125 | many | TECHNICAL | per-site gerund ("Choosing a Unit to Activate", "Choosing a Melee Target", ...) |
| Select ModelData | CastSpellStage:632, BuildTargetListStage:78 | spell/shot model pick | TECHNICAL | "Choosing a Target Model" |
| Select Option | StringSelectionRequest; sites: ChooseActionStage:434, ChooseMeleeWeaponStage:75, hold-or-deploy, TargetMarkerSpend | action menu etc. | TECHNICAL | "Choosing an Action" / "Choosing a Weapon" / "Deciding Whether to Deploy" |
| Yes/No Question | YesNoRequest; ~13 sites (morale opt-ins, strike back, regenerate, activation triggers, ...) | everywhere | TECHNICAL | per-site wording needed |
| Select Item | ArmySetupStage:28 | army pick | TECHNICAL | "Choosing an Army" |
| Move Unit | DefinePathStage:69 | advance/rush/charge | TECHNICAL | "Moving a Unit" |
| Triggered Move | GameOperationServices:51 | rule-triggered move | TECHNICAL | "Making a Forced Move" |
| [unit] (Aircraft) - forced move | DefinePathStage:154 | aircraft advance | TECHNICAL | "Flying [unit]'s Forced Move" |
| Consolidate Move (Wipeout/Disengage) | ConsolidateStage:55 | post-melee | TECHNICAL | "Consolidating After ..." |
| Place Spawned Unit | GameOperationServices:317 | summon | TECHNICAL | "Placing a Summoned Unit" |
| Place Reinforcements | StartOfRoundExtraActionStage:156 | reserves | minor | "Placing Reinforcements" |
| Aircraft Redeploy | StartOfRoundExtraActionStage:202 | round start | minor | "Redeploying an Aircraft" |
| Ambush Deploy (DISC) | StartOfRoundExtraActionStage:213 | ambush arrival | minor | "Deploying from Ambush" |
| Place Scout Unit (DISC) | PlaceDeferredUnitsStage:39 | scout deploy | minor | "Deploying a Scouting Unit" |
| Disembark [unit] / Teleport [unit] (...) / Reposition [unit] (...) / Spill out [unit] (...) / Re-Deploy [unit] | DisembarkStage:63, TeleportStage:66, RepositionPlacement:42, SpilloutExecutor:70, ReDeploymentStage:144 | various | minor (imperative) | gerund forms |
| Cast Assist | CastSpellStage:752 | casting | minor | "Assisting a Spell Cast" |
| Choose Spell / Choose Effect / Choose Ranged Weapon / Choose Deployment Zone / Assign Wounds | various | - | OK | optional gerund polish |
| Place objective N of M / Place terrain ... | PlaceOneObjectiveStage:57, PlaceTerrainStage:323,454 | setup | OK | optional gerund polish |
