# Engine Notes

Reference moved out of CLAUDE.md (2026-07-08) to keep the always-loaded context lean.
Read the section relevant to the area you're touching. Sibling doc: `docs/ResolverGuide.md`
(stage resolver pattern, GUI overlay, resolver inventory, validation gotchas).

## Networking

Multiplayer goes through `FDGHost` (TCP listener on port 6389) and `FDGClient` (TCP connect). Lobby state on each side is an `ILobbyViewModel` (`LobbyViewModel_Host` or `LobbyViewModel_Client`) — both expose the same observable state (player list, chat, settings) so `LobbyScreen` doesn't need to care which side it's on.

When LAUNCH fires, **both** sides invoke `OnLaunched` with an `IFDGGame`. Both sides then run `LobbyScreen.HandleLaunch`, which calls `ResolverRegistryFactory.BuildGui(tableState)` and `game.AssignInterfaces(...)`. On the client, `FDGGame_AsClient.AssignInterfaces` internally creates a `NetworkedRequestMessageReceiver` that pulls `StageTaskRequestMessage` off the bus, routes them to the local resolver registry, and sends replies back to the host. So the GUI resolver pattern Just Works for networked games — no extra wiring needed on the client side.

The launch message carries a host-minted game ID (`ILobbyViewModel.LaunchedGameId`, #412), used only to pair the host's anonymous `game_start`/`game_end` with each client's `game_join`. It is optional on the wire in both directions, so it did not bump `NetworkProtocol.Version`.

## Engine Concepts

- **`ITableState`**: Live observable view of the game world. Has `Units`, `Models`, `Armies`, `Teams`, `Terrain` — each with `OnObjectCreated`/`OnObjectRemoved` events and an `Objects` enumerable.
- **`IModel`**: Has `Position` (live), `BaseRadiusInches`, `OnPositionChanged`, `OnWoundsDealt`. A model is in `_tableState.Models` from creation but its `Position` stays at `(0,0,0)` until `SetPosition` is called — code that scans for occupants must filter that out.
- **`IUnit`**: Has `Models` (list of `IModel`) and `PlayerID`.
- **`DataBinding<T>`**: Wrapper around a value stored in `GameDataStore`; `GetValue()` is always current.
- **`LocalMessageBus`**: Implements both `IMessageBusHost` and `IMessageBusClient` — used for single-machine play without a network layer.
- **Rule-ownership gating ("most models", #428)**: OPR 3.6 applies every unit-wide rule only to "a unit where most models have this rule" (Fearless, Regeneration, Stealth, Artillery, the Subject-seat defensive family, the whole-unit reposition/reactivation family, ~85 supplement definitions). Those definitions gate on `Condition.MostModelsHaveThisRule` (`Rules/Definitions/Condition.cs`): a strict majority (more than half) of the bearer unit's LIVING models carry the firing rule, so 5 of 9 qualifies and 4 of 9 or 2 of 4 does not, and casualties shift the count mid-game. A model carries it via its own rules, the unit's static rules (native models only - a joined hero never inherits the host's rules), or a unit-held grant (aura/buff tokens cover every model, hero included). The joined hero always COUNTS as a model, so a 10-model unit whose hero lacks the rule is 10 of 11 and keeps it, and a hero's own copy can't make a majority on a multi-model host. A unit with no living models is counted as it stood (Reinforcement's "fully destroyed" arm). Only Inquisitorial Agent and Martial Prowess keep 3.6's "if ALL models in this unit have this rule" and use `Condition.EveryModelHasThisRule` (JSON `everyModelHasThisRule`). The pre-3.6 JSON kind `allModelsHaveThisRule` still parses, as `LegacyAllModelsHaveThisRule`, a subtype of the most-models gate (3.6 semantics), so old `.fdgarmy`/`.fdgsave` files and not-yet-re-baked `.fdgbook`s load. `--trace-rules` names it, and `RuleValidator.ValidateAuthoring` (`--validate-rules`) refuses it in new data. `RuleValidator.Validate` (the army-load gate) requires an ownership gate on unit-scoped Subject-seat defensive entries, and `ValidateAuthoring` additionally requires one on whole-unit reposition/reactivation effects. This is separate from `EModelRuleScope` (AnyOwner/AllOwners), which decides which models' per-model rules a pooled weapon batch contributes.
- **Counted wounds (#431 Scourge)**: "enemies count as having taken 2 extra wounds when checking who won melee, or if shooting wounds would trigger Morale Tests" - wounds a unit COUNTS as having taken without losing them. `Effect.CountsAsWoundsTaken` (JSON `countsAsWoundsTaken`) emits `RuleOperation.AddCountedWoundsTaken` at save-roll-complete; `AssignWoundsStage` banks it on the struck unit as a `TokenType.CountedWoundsTaken` magnitude (`Rules/Dispatch/CountedWounds.cs`). Two readers `Take` it (read + remove): `DetermineMeleeWinnerStage` adds it to the struck unit's own taken total (#419; Fear's "enemies count as having taken" polarity), and the wound-driven morale check (`ResolveRangedMoraleStage`, `ResolveStrafeMoraleStage` -> `MoraleUtilities.ResolveWoundDrivenMorale(..., countedWounds)`) treats it as wounds taken, allocating it virtually for the half-strength test (`IUnitExtensions.GetIsAtHalfStrength(unit, countedWounds)`: wounded models first, joined hero last). `ReconcileEndOfActivationStage` sweeps the token off EVERY unit, since the struck unit is not the activated one and a melee that ends in a wipe-out never reaches its result step.
- **Range-conditional army rules (#432)**: OPR 3.6's "when this model shoots/charges enemies over 9"/6" away" (offensive), "when shot/charged from over 9"/6" away" (defensive and morale) read `IHasShootChargeDistance` (`Rules/Foundation/`): the SHOOTING distance (closest living models, base to base - the measurement every range gate reads) and the CHARGE distance (the pre-charge gap snapshotted at the charger's activation start, `UnitActionContext.SnapshotDistancesToEnemies` -> `CombatActionContext` -> `CombatMetadata.ChargeOriginDistanceInches`), each null when that kind of attack is not what happened (a strike-back is not a charge, spell damage is not shooting). Two conditions: `Condition.AttackingFromOver` (JSON `attackingFromOver {shootInches, chargeInches}`, Actor seat) and `Condition.AttackedFromOver` (`attackedFromOver`, Subject seat / the tested unit) - the pair is each rule's data. The hit-modifier, hit-complete and save-complete contexts provide it, and so does `PreMoraleTestContext`, which now carries the attack that triggered the test (`ResolveRangedMoraleStage`: the distance the shooter first targeted the unit from, `AttackedDefender.ShotFromInches`; `RollForMoraleStage`: the charge the LOSER received, `ICombatActionContext.ChargedFromInchesFor`). The range family is one effect: `Effect.RangeModifier` gained `ChargeDelta` (`rangeModifier {delta, chargeDelta, minResultInches}` at `Shooting_OnRangeCheck`; Darkborn -4/-2 min 6 at the Subject seat, Highborn +4/+2 at the Actor seat). Charge range is the charge move: the Actor half joins every charge budget (`RangeRuleQueries.OwnChargeRangeDelta` in `MovementActionContext`, `PerModelMoveBudgets`, the AI's `TacticalAnalysis.ChargeBudget`), the Subject half the per-target `MovementRuleQueries.EffectiveChargeDistanceAgainst` (DefinePathStage's worst case). The floor ("to a min. of 6"") only limits a reduction - a 9" gun stays 6", a 4" gun stays 4" (`RangeRuleQueries.RangeFold`). The 3.5 `attackedFromOverInches` kind and `IHasAttackOriginDistance` stay for old army files and not-yet-rebaked books.
- **Negative-modifier immunity (#427 Unstoppable)**: 3.6 "ignores all negative modifiers to this weapon which are not from this model's special rules". `Effect.IgnoreNegativeWeaponModifiers` (JSON `ignoreNegativeWeaponModifiers`) emits a marker `RuleOperation.IgnoreNegativeWeaponModifiers` at `Shooting_OnHitRollModifier` AND `Shooting_OnHitRollComplete`; `RuleEvaluator.ApplyNegativeModifierImmunity` (the first pass beside `SuppressRule`, since it alone knows each op's source) then drops every to-hit penalty (`ApplyRollModifier` of `Hit` below zero) and AP reduction (`ReduceArmorPenetration`) that is NOT the marker bearer's own: same unit (its unit, weapon and model rules) and not a `RuleGrant` token another unit placed (`Token.OwnerUnitID` set - an enemy spell's debuff). The marker stays in the queue so the two readers of a GRANTED negative hit token (`DetermineHitRollStage`, `ShootingForecast`) leave it out via `GrantedRollModifiers.ConsumeNet/PeekNet(ignoreOtherUnitsNegative:)`. Not covered, by decision: shooting-range reductions (Aircraft's -12", Shrouded) and Defense modifiers. AI/`CombatMath` and the calculator get it free (they run the same evaluation; pinned in `CombatMathPinTests`).
- **Passive casting-roll modifiers (#427 Spell Amplifier)**: rules author `Effect.RollModifier(ERollKind.Cast, +N)` at `Casting_OnSpellCastAttempt` (context `SpellCastAttemptContext`, the caster only); `CastRollRules.Net(caster, evaluator, live)` folds them over the unit, its living models and every living model's weapon (so a weapon-scoped rule on a Caster Staff counts; the evaluator's dedup keeps an argument-less rule at one firing). `CastSpellStage` adds it to the net modifier (live, narrated in the log; the roll breakdown shows "rules +N"), and passes the picker the caster's own threshold as `ChooseSpellRequest.BaseThreshold` (4+ less the bonus, read-only). New casting modifiers (#429) belong at that hook.

## Renderer (`FdgRaylib/Rendering/RaylibRenderer.cs`)

- Reads live state from `ITableState` — no polling, no callbacks into the request system.
- Subscribes to `ITableState.Models.OnObjectCreated` and each model's `OnPositionChanged`. Models are only drawn after their first `SetPosition` call.
- Circles drawn at true scale: `BaseRadiusInches * scale px/inch`. Two circles visually touching = bases touching in the game world.
- Player colours are assigned in `LobbyScreen.HandleLaunch` (palette-indexed) and read at draw time via `Func<PlayerID, Color>`.
- The `Layout` record (scale + origin) is computed each frame from current screen size; resolver overlay receives it via `UpdateLayout`.

## Game Termination

- `ReconcileObjectivesStage` runs at the end of each round: any objective with living models from exactly one player within 3" (base-edge to objective center) is seized by that player; objectives contested by multiple players become neutral; otherwise ownership is preserved. After 4 rounds (the official game length per the GDF rulebook, not a stub) it transitions to `VictoryCalculationStage`.
- `VictoryCalculationStage` tallies controlled objectives per player and calls `IGameContext.NotifyGameEnded(...)` — a unique top scorer wins, ties (or zero objectives controlled) end in a tie.
- The notification propagates: `GameContext.OnGameEnded` event -> `FDGServer.OnGameEnded` event -> `CliApp` `TaskCompletionSource` -> `RunAsync` returns. In GUI mode a Game Over card offers Return to Main Menu (#040).
- In GrimDark Future rules a player can win even if all their models are eliminated (objectives determine the winner), so **unit counts must never be used as a win condition**.

## Known stubs in the engine

The engine has substantial gaps. Don't assume rules are enforced just because a stage exists. Surveyed Apr 2026 (setup, melee pile-in, and wound auto-fill re-verified 2026-06-14):

**Setup is implemented** (was stubbed at the Apr 2026 survey)
- `MapSetupStage` runs the full sequence: `RollForObjectiveCountStage` rolls D3+2 (3-5 objectives), `RollForFirstObjectivePlacementStage` + `PlaceObjectivesStage`/`PlaceOneObjectiveStage` place `ObjectiveData` per `GameSettings.ObjectivePlacementMode` (AutoPlaced = engine places all via the shared `ObjectiveAutoPlacer`, the same algorithm the solo-rules AI uses; PlayerPlaced = alternating player/AI requests), and `RollForFirstTerrainPlacementStage` + `PlaceTerrainStage` place terrain (AutoFromLayout / LoadFromFile / Alternating modes).
- `ReconcileObjectivesStage` and `VictoryCalculationStage` (seizure + objective tally -> real winner) operate on the objectives `MapSetupStage` actually produces.

**Movement validation is partial**
- `MovementUtilities.ValidateMovingThroughImpassibleTerrain` — implemented; blocks moves whose path intersects any `Impassible`-flagged terrain piece
- `MovementUtilities.ValidateMovingThroughEnemyUnits` — implemented (#011/#090): pass-through/stacking block + 1" standoff. Friendly-unit end-state check is still missing (#182)
- LoS is fully implemented: `ChooseRangedAttackStage` and `OcclusionCheckStage` call `LineOfSightUtilities.HasLineOfSight` with terrain + model-base circular blockers (excluding the attacking and defending unit's own models)

**Melee — partial** (much of the flow works: strike order, swing, strike-back, winner determination, consolidate; the gap below remains)
- `DetermineInRangeAttackersStage` / `DetermineInRangeDefendersStage` — real range gating landed with #017: 3" horizontal (base-to-base; Core Rules v3.6.0 "Models within 3" of enemies must strike", #417 - 2" in v3.5.1) AND 4" vertical (our #022 convention, kept by #417), all through `MeleeRangeUtilities.AreModelsInMeleeRange` on `GameWideConstants.MELEE_RANGE_INCHES_HORIZONTAL`/`_VERTICAL` - the same cylinder gates Charge availability, defender eligibility, the beyond-Rush charge-reach check and the AI's melee threat reach. The 1" standoff is unchanged, so the may-charge-but-not-forced band is 1"-3"
- `PileInStage` — implemented (moves defenders toward the charger via `PileInUtilities.ComputePileInMoves`, up to 3" - unchanged in v3.6.0; a third-party enemy base is a stop-at-contact obstacle, an Aircraft's footprint grows by the 1" standoff first - "can't be moved in contact with", #427); no longer a no-op. `ConsolidateStage`: the survivor of a wipeout moves up to 3", else the charger falls back 1" (both v3.6.0, unchanged)
- `DetermineMeleeWinnerStage` — the melee result (#419, Core Rules v3.6.0: "compare the number of wounds each unit took during this activation, and the unit with the highest total loses"; tie = nobody tests). A side's total = the wounds it had already taken earlier in the activation (the activation wound ledger `Rules/Dispatch/WoundsTakenThisActivation.cs`, a `TokenType.WoundsTakenThisActivation` magnitude fed where wounds land - `ApplyWoundsStage` and `CasualtyPresentation.ApplyAndPresent` - and swept table-wide at activation start in `MainUnitActionStage` and end in `ReconcileEndOfActivationStage`; `CombatActionContext` snapshots it as `Attacker/DefenderWoundsTakenBeforeMelee`, so a Dangerous Terrain test on the charge move or a Breath Attack counts) + the remaining-wounds drop since the melee snapshot (Impact, Counter strikes, swings, strike-backs, Hazardous); both snapshots swap with Counter's role swap. Fear(X) adds X to the ENEMY's total ("enemies count as having taken +X wounds"), Scourge's counted wounds to the struck unit's own. The log line reads "Melee result: A took N wounds, B took M wounds - B took more and loses the melee."

**Fatigue & morale implemented** (#020 fatigue + #091 morale-core; re-verified 2026-06-28)
- `ApplyFatigueStage` — applies `FatigueUtilities.ApplyFatigued` to units that fought (charge/strike-back/swing); a Fatigued (or Shaken) unit hits only on unmodified 6s in melee (`DetermineHitRollStage`), and those 6s don't count as 6s for special rules (#418, 3.6): `RollToHitStage` sets `HitRollCompleteContext.SixesCountForSpecialRules` from `FatigueUtilities.SixesCountForSpecialRules`, and every on-a-natural-N trigger (`UnmodifiedRollEquals`, `AddExtraHit`, `SelfWoundOnUnmodifiedRoll`, `PerHitSaveModifier`, `PerHitApSplitter`) reads the context's `SpecialRuleHitRolls` view (face 6 zeroed), never the raw roll. AI `CombatMath` mirrors the flag; the Combat Calculator runs the real stages. Shooting and defender-side rolls (Shred, Bane) are unaffected. The same predicate also switches off Impact, Counter and Brace ("unless this unit is Fatigued", #422, 3.6): their entries gate on `Condition.CountsAsFatiguedInMelee`, whose `Holds` is the predicate `FatigueUtilities.CountsAsFatiguedInMelee` delegates to. Impact resolves before either unit attacks (`ResolveImpactHitsStage` precedes `DetermineStrikeOrderStage`); Brace(X) folds a negative impact-dice count into the same `ImpactSink`
- `AssignMeleeMoralePenaltyStage` — real: Shaken, or Rout (lethal-wounds-to-all, since there's no whole-unit removal primitive) when the loser is at half strength
- `RollForMoraleStage` — runs `MoraleUtilities.TakeMoraleTest` (rule-aware: folds `Morale_OnPreMoraleTest` modifiers, Fearless re-roll). Invocable on demand — #034's conditional spells (`Effect.MoraleTestThen`) call it to branch on pass/fail

**Round/turn machinery placeholders**
- `ReconcileNewRoundStage` — transitions with no work
- `StartOfRoundExtraActionStage` — implemented: from round 2 it brings reserve (Ambush) units onto the table, offering each owner a Yes/No then placing it >9" from enemies (#042 deploy primitive)
- `ApplyNonMovementTerrainEffectsStage` — implemented (3.6, #420): when any model's path crosses `Dangerous` terrain, the whole unit rolls one d6 per wound it would take to fully destroy it (`IUnit.GetWoundsToDestroy()`, Tough-aware, living models only, floored), each 1 a wound to the UNIT assigned in the normal order (already-wounded first, joined hero last; `AssignWoundsResults.AutoAssign`). The roll lives in `Rules/Dispatch/DangerousTerrainBatch` and is shared by the movement crossing, the forced Dangerous Terrain Debuff (`RollForcedDangerousTerrain`) and a destroyed transport's spillout (`TransportUtilities.ApplySpilloutEffects`); No Retreat's self-wound pool uses the same `GetWoundsToDestroy()` count. Probabilistic mode spreads the fractional expected wounds across the living models by their share of the unit's wounds. Not implemented: the "or that activate in it" clause (a unit that holds/shoots standing in `Dangerous` terrain without moving takes no test)
- `ChooseActionStage` — custom-action branch is real (#010): rules with an `ActivatedAbility` at `Activation_OnActionChoice` surface as selectable actions

**Half-built**
- `RangedContext.SetAttackWeapon` and friends — `NotImplementedException` on multiple paths
- `AssignWoundsResults` — residual polish tracked as #177 (float `==`, misused exception ctor, split exploit window); Tough-priority (#023) and split validation (#024) are done. #401: it carries an ordered queue of `WoundPacket`s (a plain volley is one unconfined pool; a Deadly clump is a confined packet of X, landed on one model with the excess lost) and `TryAddWounds` commits the next packet. `AutoFill()` never throws any more: what the last living model cannot absorb is reported as `Overkill`, a clump's excess as `ClumpExcessLost`. The stage rolls Regeneration per packet before asking (`AssignWoundsStage`), and `CombatMath` prices the same queue through `WoundAllocation.Packets`/`Simulate`

## Key Files

```
FdgRaylib/
  Program.cs                               Entry point; wires screen graph and Raylib loop
  Analytics/                               #412 anonymous usage stats: UsageAnalytics facade, AnalyticsClient
                                           (gates/batching/spool), GameAnalyticsSession (game events),
                                           SessionTracker. Event schema = tools/list-server/src/analytics.ts
  Cli/
    CliApp.cs                              Headless app: Prepare() + RunAsync()
    ArmyLoader.cs                          Prompts for army; EOF -> built-in test army
    LocalMessageBus.cs                     In-process message bus (single-machine play)
    ResolverRegistryFactory.cs             Build()/BuildGui() — assemble resolver registry
    Resolvers/                             CLI (stdin/stdout) resolvers, one per request type
  Rendering/
    RaylibRenderer.cs                      Window loop, screen dispatch, in-game canvas
    IAppScreen.cs                          Screen interface used by the screen stack
    MainMenuScreen.cs / ArmyBuilderScreen.cs / ArmyForgeScreen.cs
    HostModal.cs / ClientModal.cs / LobbyScreen.cs
    Resolvers/
      IGuiResolver.cs                      Has-pending + Draw
      IGuiCanvasOverlay.cs                 Optional layout receiver for table interactions
      GuiResolverOverlay.cs                Holds resolvers; draws active one each frame
      Gui*Resolver.cs                      One per request type (see ResolverGuide.md)

FutureOfDarkGrimness/                      Submodule — read-only by default
  GameModel/
    FDGServer.cs                           State machine driver; creates army data
    FDGGame_AsLocal.cs / FDGGame_AsClient.cs
  ArmyBuilding/                            Army Forge book model, compiler, validator, OPR importer
  Rules/                                   #042 data-driven special-rules system (catalog, dispatch, tokens)
  Network/
    Connection/
      FDGHost.cs / FDGClient.cs            TCP host/client over port 6389
      Lobby/LobbyViewModel_*.cs            Observable lobby state (host & client)
    NetworkedRequestMessageReceiver.cs     Bridges network requests -> local resolvers
  TableState/                              Observable game world
  StageResolution/                         Request/resolver infrastructure
  StateMachine/                            Turn structure, deployment, movement, combat
  Tests/                                   NUnit test suite
```
