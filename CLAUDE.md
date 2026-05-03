# FDG Raylib

A Raylib-based client for **Future of Dark Grimness** — a tabletop wargame rules engine. The repository contains two C# .NET 8 projects.

## Projects

| Project | Type | Purpose |
|---------|------|---------|
| `FutureOfDarkGrimness` | Class library | Game engine: rules, state machine, unit/model data, stage resolution, networking |
| `FdgRaylib` | Console exe | Application layer: Raylib + ImGui front end, screens (menu/lobby/army builder), CLI + GUI input resolvers |

`FutureOfDarkGrimness` is a **git submodule** — usually treat it as read-only. Stop and ask before modifying it.

## Build & Run

```bash
# Build everything
dotnet build

# Run with Raylib window (requires a display)
dotnet run --project FdgRaylib/FdgRaylib.csproj

# Run headless (CLI only, no window — useful for piped/automated play)
dotnet run --project FdgRaylib/FdgRaylib.csproj -- --headless

# Pipe empty stdin to auto-resolve everything via EOF defaults
printf "2\n2\n" | dotnet run --project FdgRaylib/FdgRaylib.csproj -- --headless

# Slow mode: pause N ms before each resolver call (default 1500ms if no value given)
dotnet run --project FdgRaylib/FdgRaylib.csproj -- --slow 2000

# Run engine tests
dotnet test FutureOfDarkGrimness/FutureOfDarkGrimness.csproj
```

## Application Flow

Two top-level modes determined in `Program.cs`:

**Headless (`--headless`)** — `CliApp.Prepare()` then `CliApp.RunAsync()`. No screens, no Raylib window. Stage requests resolved via stdin/stdout (CLI resolvers).

**GUI (default)** — `RaylibRenderer.Run()` blocks the main thread. Screen stack starts at `MainMenuScreen` and navigates via `renderer.NavigateTo(IAppScreen)`:

```
MainMenu ─┬─► HostModal ────► LobbyScreen ──► (in-game)
          ├─► ClientModal ──► LobbyScreen ──► (in-game)
          ├─► ArmyBuilder
          └─► Quit
```

Each screen is an `IAppScreen` with `Draw(int screenW, int screenH)` and exposes `Action`-based callbacks for navigation. `Program.cs` wires those callbacks together — that's where the screen graph lives.

The game itself only starts running when `LobbyScreen.HandleLaunch` fires (after the host clicks LAUNCH, on both host and client). Until then no `IFDGGame` exists.

## Networking

Multiplayer goes through `FDGHost` (TCP listener on port 6389) and `FDGClient` (TCP connect). Lobby state on each side is an `ILobbyViewModel` (`LobbyViewModel_Host` or `LobbyViewModel_Client`) — both expose the same observable state (player list, chat, settings) so `LobbyScreen` doesn't need to care which side it's on.

When LAUNCH fires, **both** sides invoke `OnLaunched` with an `IFDGGame`. Both sides then run `LobbyScreen.HandleLaunch`, which calls `ResolverRegistryFactory.BuildGui(tableState)` and `game.AssignInterfaces(...)`. On the client, `FDGGame_AsClient.AssignInterfaces` internally creates a `NetworkedRequestMessageReceiver` that pulls `StageTaskRequestMessage` off the bus, routes them to the local resolver registry, and sends replies back to the host. So the GUI resolver pattern Just Works for networked games — no extra wiring needed on the client side.

## Threading

- **GUI mode**: Raylib + ImGui own the main thread. The game engine runs on whatever thread the network/lobby kicks it off on (usually a background `Task`). Resolvers' `Resolve()` methods are called from the engine thread; their `Draw()` methods are called from the main thread. **`_request` and `_tcs` fields must be guarded by a lock.**
- **Headless mode**: `CliApp.RunAsync()` runs on the main thread (no Raylib). Resolvers read stdin synchronously.

## Stage Resolver Pattern

The engine sends `IStageTaskRequest<TResult>` objects through the message bus whenever it needs a player decision. Resolvers implement `IStageResolver<TRequest, TResult>` and are registered with a `StageResolverRegistry`.

There are **two parallel sets of resolvers**:

- `FdgRaylib/Cli/Resolvers/` — stdin/stdout. Used in headless mode and as fallback. Each handles `null` from `Console.ReadLine()` (EOF) with a sensible default so piped input works.
- `FdgRaylib/Rendering/Resolvers/` — interactive ImGui dialogs and table-canvas interactions. Used in GUI mode. As of this writing **every request type has a GUI resolver**; `BuildGui` registers no CLI fallbacks.

`ResolverRegistryFactory.Build(tableState)` builds the headless registry; `BuildGui(tableState)` returns `(registry, GuiResolverOverlay)`.

### GUI resolver overlay (`FdgRaylib/Rendering/Resolvers/`)

GUI resolvers implement `IGuiResolver`:
- `bool HasPendingRequest` — true while waiting for a click/decision
- `void Draw(int screenW, int screenH)` — called from the main thread inside `rlImGui.Begin()/End()`

`GuiResolverOverlay` holds them all and draws whichever has a pending request. `RaylibRenderer` calls `_resolverOverlay.Draw()` once per frame while in-game.

Resolvers that need to interact with the table canvas (movement, placement) additionally implement `IGuiCanvasOverlay`, which receives `UpdateLayout(scale, originX, originY, tableH)` from the renderer each frame so they can do pixel↔inch conversion. They draw rings, ghost models, and zone outlines via `ImGui.GetBackgroundDrawList()` — this puts shapes on top of the Raylib canvas but underneath ImGui windows. Mouse hit-testing uses `ImGui.GetIO().MousePos` and respects `WantCaptureMouse` so clicks on info panels don't bleed through to the table.

### Resolver inventory

| Request | CLI resolver | GUI resolver | Notes |
|---|---|---|---|
| `YesNoRequest` | `YesNoResolver` | `GuiYesNoResolver` | EOF default: `true` |
| `SelectionRequest<T>` | `SelectionResolver<T>` | `GuiSelectionResolver<T>` | Registered for `UnitData`, `ModelData`, `RectangularZone`; GUI has a Back button that resolves `null` |
| `StringSelectionRequest` | `StringSelectionResolver` | `GuiStringSelectionResolver` | |
| `ChooseDeploymentZoneRequest` | `ChooseDeploymentZoneResolver` | `GuiChooseDeploymentZoneResolver` | |
| `ChooseRangedAttackRequest` | `ChooseRangedAttackResolver` | `GuiChooseRangedAttackResolver` | Flattens weapon × target into a single button list; GUI has a Back button that resolves `null` |
| `AssignWoundsRequest` | `AssignWoundsResolver` | `GuiAssignWoundsResolver` | Stateful — `AssignWoundsResults` accumulates clicks; auto-completes when full |
| `DefineMovementPathRequest` | `DefineMovementPathResolver` | `GuiDefineMovementResolver` | Click destination on canvas; whole unit moves same Δ |
| `PlaceObjectsRequest<T>` | `PlaceObjectsResolver<T>` | `GuiPlaceObjectsResolver<T>` | Click each model in turn within deployment zone |

### Validation gotchas

- **Deployment spacing**: `MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES` is 1.0" base-to-base. Auto-placement uses 0.1" gap, **not 1.0"** — at exactly 1.0", float accumulation during diagonal movement can push models fractionally over the cohesion limit.
- **Movement float precision**: `AutoAdvance` caps `step` at `MaxAdvanceDistance - 0.001f`. Without this margin, the resulting 3D move distance can come out fractionally above `MaxAdvanceDistance` and `ChooseActionStage.GetCanShoot` will block shooting after a legal advance.
- **Back / cancel sentinel**: `GuiSelectionResolver<T>` and `GuiChooseRangedAttackResolver` resolve with `null` when the player clicks Back. Any stage that awaits those requests must null-check the result and activate its `BackToChooseAction` binding rather than proceeding. `ChooseMeleeDefenderStage` and `ChooseRangedAttackStage` already do this.
- **Charge availability**: `ChooseActionStage.GetCanCharge` queries live unit positions and grays out Charge when no enemy is within `MELEE_RANGE_INCHES_HORIZONTAL` (2"). The check re-runs each time Choose Action is entered, so it stays accurate after movement.

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

- `ReconcileObjectivesStage` counts entries and transitions to `VictoryCalculationStage` after 4 rounds (hardcoded stub).
- `VictoryCalculationStage` logs a tie and calls `IGameContext.NotifyGameEnded("It's a tie!")`.
- The notification propagates: `GameContext.OnGameEnded` event → `FDGServer.OnGameEnded` event → `CliApp` `TaskCompletionSource` → `RunAsync` returns.
- In GUI mode the Raylib window stays open after the game ends; the user closes it manually. (Navigating back to the main menu post-game is **not yet wired up**.)
- Victory is intentionally always a tie for now — in GrimDark Future rules a player can win even if all their models are eliminated (objectives determine winner), so unit counts must never be used as a win condition.

## Known stubs in the engine

The engine has substantial gaps. Don't assume rules are enforced just because a stage exists. Surveyed Apr 2026:

**Won't end the game properly**
- `ReconcileObjectivesStage` — hardcoded 4-round counter; no objective control logic
- `VictoryCalculationStage` — always declares a tie
- `MapSetupStage` — no terrain or objective placement (TODO)

**Movement validation is partial**
- `MovementUtilities.ValidateMovingThroughImpassibleTerrain` — implemented; blocks moves whose path intersects any `Impassible`-flagged terrain piece
- `MovementUtilities.ValidateMovingThroughEnemyUnits` — empty (TODO)
- LoS is fully implemented: `ChooseRangedAttackStage` and `OcclusionCheckStage` call `LineOfSightUtilities.HasLineOfSight` with terrain + model-base circular blockers (excluding the attacking and defending unit's own models)

**Melee is barely implemented**
- `DetermineInRangeAttackersStage` / `DetermineInRangeDefendersStage` — skip range checks; any model can fight
- `PileInStage` — no-op

**Fatigue & morale absent**
- `ApplyFatigueStage` — logs and exits
- `AssignMeleeMoralePenaltyStage` — no-op (waits on fatigue)
- `RollForMoraleStage` — modifiers TODO

**Round/turn machinery placeholders**
- `StartOfRoundExtraActionStage`, `ReconcileNewRoundStage` — transition with no work
- `ApplyNonMovementTerrainEffectsStage` — implemented: rolls d6 per model whose path crosses `Dangerous` terrain; deals 1 wound on a roll of 1
- `ChooseActionStage` — custom-action branch hardcoded `false`

**Half-built**
- `RangedContext.SetAttackWeapon` and friends — `NotImplementedException` on multiple paths
- `AssignWoundsResults` — no priority for "tough" models, wound-split validation missing
- `AssignWoundsResults.AutoFill()` has a bug (`modelWoundsRemaining` always 0); the GUI/CLI wound resolvers fill manually instead

## Key Files

```
FdgRaylib/
  Program.cs                               Entry point; wires screen graph and Raylib loop
  Cli/
    CliApp.cs                              Headless app: Prepare() + RunAsync()
    ArmyLoader.cs                          Prompts for army; EOF → built-in test army
    LocalMessageBus.cs                     In-process message bus (single-machine play)
    ResolverRegistryFactory.cs             Build()/BuildGui() — assemble resolver registry
    Resolvers/                             CLI (stdin/stdout) resolvers, one per request type
  Rendering/
    RaylibRenderer.cs                      Window loop, screen dispatch, in-game canvas
    IAppScreen.cs                          Screen interface used by the screen stack
    MainMenuScreen.cs / ArmyBuilderScreen.cs
    HostModal.cs / ClientModal.cs / LobbyScreen.cs
    Resolvers/
      IGuiResolver.cs                      Has-pending + Draw
      IGuiCanvasOverlay.cs                 Optional layout receiver for table interactions
      GuiResolverOverlay.cs                Holds resolvers; draws active one each frame
      Gui*Resolver.cs                      One per request type (see inventory above)

FutureOfDarkGrimness/                      Submodule — read-only by default
  GameModel/
    FDGServer.cs                           State machine driver; creates army data
    FDGGame_AsLocal.cs / FDGGame_AsClient.cs
  Network/
    Connection/
      FDGHost.cs / FDGClient.cs            TCP host/client over port 6389
      Lobby/LobbyViewModel_*.cs            Observable lobby state (host & client)
    NetworkedRequestMessageReceiver.cs     Bridges network requests → local resolvers
  TableState/                              Observable game world
  StageResolution/                         Request/resolver infrastructure
  StateMachine/                            Turn structure, deployment, movement, combat
  Tests/                                   NUnit test suite
```

## Army Files

Army lists use the `.fdgarmy` extension (JSON, with `TypeNameHandling.Auto`). The CLI prompts for a file path; EOF falls back to a built-in two-unit test army (5× Warriors with rifles + 3× Heavy Gunners with heavy rifles). The Army Builder screen edits these files via `TinyDialogs` save/load dialogs.
