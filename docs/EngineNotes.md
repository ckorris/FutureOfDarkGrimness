# Engine Notes

Reference moved out of CLAUDE.md (2026-07-08) to keep the always-loaded context lean.
Read the section relevant to the area you're touching. Sibling doc: `docs/ResolverGuide.md`
(stage resolver pattern, GUI overlay, resolver inventory, validation gotchas).

## Networking

Multiplayer goes through `FDGHost` (TCP listener on port 6389) and `FDGClient` (TCP connect). Lobby state on each side is an `ILobbyViewModel` (`LobbyViewModel_Host` or `LobbyViewModel_Client`) — both expose the same observable state (player list, chat, settings) so `LobbyScreen` doesn't need to care which side it's on.

When LAUNCH fires, **both** sides invoke `OnLaunched` with an `IFDGGame`. Both sides then run `LobbyScreen.HandleLaunch`, which calls `ResolverRegistryFactory.BuildGui(tableState)` and `game.AssignInterfaces(...)`. On the client, `FDGGame_AsClient.AssignInterfaces` internally creates a `NetworkedRequestMessageReceiver` that pulls `StageTaskRequestMessage` off the bus, routes them to the local resolver registry, and sends replies back to the host. So the GUI resolver pattern Just Works for networked games — no extra wiring needed on the client side.

## Engine Concepts

- **`ITableState`**: Live observable view of the game world. Has `Units`, `Models`, `Armies`, `Teams`, `Terrain` — each with `OnObjectCreated`/`OnObjectRemoved` events and an `Objects` enumerable.
- **`IModel`**: Has `Position` (live), `BaseRadiusInches`, `OnPositionChanged`, `OnWoundsDealt`. A model is in `_tableState.Models` from creation but its `Position` stays at `(0,0,0)` until `SetPosition` is called — code that scans for occupants must filter that out.
- **`IUnit`**: Has `Models` (list of `IModel`) and `PlayerID`.
- **`DataBinding<T>`**: Wrapper around a value stored in `GameDataStore`; `GetValue()` is always current.
- **`LocalMessageBus`**: Implements both `IMessageBusHost` and `IMessageBusClient` — used for single-machine play without a network layer.

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
- `MapSetupStage` runs the full sequence: `RollForObjectiveCountStage` rolls D3+2 (3-5 objectives), `RollForFirstObjectivePlacementStage` + `PlaceObjectivesStage`/`PlaceOneObjectiveStage` alternate players placing real `ObjectiveData` (player request, or a debug auto-placer behind `AutoPlaceObjectivesDebug`), and `RollForFirstTerrainPlacementStage` + `PlaceTerrainStage` place terrain (AutoFromLayout / LoadFromFile / Alternating modes).
- `ReconcileObjectivesStage` and `VictoryCalculationStage` (seizure + objective tally -> real winner) operate on the objectives `MapSetupStage` actually produces.

**Movement validation is partial**
- `MovementUtilities.ValidateMovingThroughImpassibleTerrain` — implemented; blocks moves whose path intersects any `Impassible`-flagged terrain piece
- `MovementUtilities.ValidateMovingThroughEnemyUnits` — implemented (#011/#090): pass-through/stacking block + 1" standoff. Friendly-unit end-state check is still missing (#182)
- LoS is fully implemented: `ChooseRangedAttackStage` and `OcclusionCheckStage` call `LineOfSightUtilities.HasLineOfSight` with terrain + model-base circular blockers (excluding the attacking and defending unit's own models)

**Melee — partial** (much of the flow works: strike order, swing, strike-back, winner determination, consolidate; the gap below remains)
- `DetermineInRangeAttackersStage` / `DetermineInRangeDefendersStage` — real 2"/4" range gating landed with #017
- `PileInStage` — implemented (moves defenders toward the charger via `PileInUtilities.ComputePileInMoves`); no longer a no-op

**Fatigue & morale implemented** (#020 fatigue + #091 morale-core; re-verified 2026-06-28)
- `ApplyFatigueStage` — applies `FatigueUtilities.ApplyFatigued` to units that fought (charge/strike-back/swing); a Fatigued (or Shaken) unit hits only on unmodified 6s in melee (`DetermineHitRollStage`)
- `AssignMeleeMoralePenaltyStage` — real: Shaken, or Rout (lethal-wounds-to-all, since there's no whole-unit removal primitive) when the loser is at half strength
- `RollForMoraleStage` — runs `MoraleUtilities.TakeMoraleTest` (rule-aware: folds `Morale_OnPreMoraleTest` modifiers, Fearless re-roll). Invocable on demand — #034's conditional spells (`Effect.MoraleTestThen`) call it to branch on pass/fail

**Round/turn machinery placeholders**
- `ReconcileNewRoundStage` — transitions with no work
- `StartOfRoundExtraActionStage` — implemented: from round 2 it brings reserve (Ambush) units onto the table, offering each owner a Yes/No then placing it >9" from enemies (#042 deploy primitive)
- `ApplyNonMovementTerrainEffectsStage` — implemented: rolls d6 per model whose path crosses `Dangerous` terrain; deals 1 wound on a roll of 1
- `ChooseActionStage` — custom-action branch is real (#010): rules with an `ActivatedAbility` at `Activation_OnActionChoice` surface as selectable actions

**Half-built**
- `RangedContext.SetAttackWeapon` and friends — `NotImplementedException` on multiple paths
- `AssignWoundsResults` — residual polish tracked as #177 (float `==`, misused exception ctor, split exploit window); Tough-priority (#023) and split validation (#024) are done. `AutoFill()` works: it loops `TryAddWounds` and throws if it can't place every wound

## Key Files

```
FdgRaylib/
  Program.cs                               Entry point; wires screen graph and Raylib loop
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
