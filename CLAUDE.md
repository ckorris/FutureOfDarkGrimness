# FDG Raylib

A Raylib-based client for **Future of Dark Grimness** — a tabletop wargame rules engine. The repository contains two C# .NET 8 projects.

## Projects

| Project | Type | Purpose |
|---------|------|---------|
| `FutureOfDarkGrimness` | Class library | Game engine: rules, state machine, unit/model data, stage resolution |
| `FdgRaylib` | Console exe | Application layer: Raylib GUI renderer + CLI input resolvers |

## Build & Run

```bash
# Build everything
dotnet build

# Run with Raylib window (requires a display)
dotnet run --project FdgRaylib/FdgRaylib.csproj

# Run headless (CLI only, no window — useful for piped/automated play)
dotnet run --project FdgRaylib/FdgRaylib.csproj -- --headless

# Pipe empty stdin to auto-resolve everything via EOF defaults
echo "" | dotnet run --project FdgRaylib/FdgRaylib.csproj

# Run engine tests
dotnet test FutureOfDarkGrimness/FutureOfDarkGrimness.csproj
```

## Architecture

### Threading (GUI mode)
- Raylib must own the **main thread** — `RaylibRenderer.Run()` blocks there.
- The game loop runs on a **background thread** via `Task.Run(() => app.RunAsync())`.
- `CliApp.Prepare()` must be called before either thread starts; it creates the `LocalMessageBus`, `GameDataStore`, and `FDGGame_AsLocal` (and thus `ITableState`) without requiring any user input.

### Renderer (`FdgRaylib/Rendering/RaylibRenderer.cs`)
- Reads live state from `ITableState` — no polling, no callbacks into the request system.
- Subscribes to `ITableState.Models.OnObjectCreated` and each model's `OnPositionChanged` to know when a model has been deployed. Models are only drawn after their first `SetPosition` call.
- Circles are drawn at true scale: `BaseRadiusInches * 10 px/inch`. Two circles visually touching = bases touching in the game world.
- Player colours are assigned in `CliApp.CreatePlayerSlots()` and looked up at draw time via `Func<PlayerID, Color>`.

### Stage Resolver Pattern
The engine sends `IStageTaskRequest<TResult>` objects through the message bus whenever it needs a player decision. `FdgRaylib/Cli/Resolvers/` contains CLI implementations for each request type. Each resolver:
- Prints a human-readable prompt describing the situation
- Handles `null` from `Console.ReadLine()` (EOF) with a sensible default so piped input works
- Returns a typed result that the engine consumes

Key resolvers:
| Resolver | Request | EOF default |
|----------|---------|-------------|
| `YesNoResolver` | Yes/no decision | `true` |
| `SelectionResolver<T>` | Pick one from a list | First option |
| `PlaceObjectsResolver<T>` | Place models in a zone | Spread left-to-right with 1" gap between bases, staggered Z row per unit |
| `DefineMovementPathResolver` | Move models | Stay in place |
| `ChooseRangedAttackResolver` | Choose weapon + target | First valid option |
| `AssignWoundsResolver` | Assign wounds to models | Auto-fill |

### Deployment Validation (`PlaceObjectsResolver`)
- Rejects positions outside the deployment zone.
- Rejects positions where the base would overlap any other base — including models from previously deployed units already on the table (read from `ITableState`).
- Auto-placement scans left-to-right to find the first free spot; each successive unit for the same player uses a 2" Z offset and half-step X stagger to avoid visual clustering.

### Engine Concepts
- **`ITableState`**: Live observable view of the game world. Has `Units`, `Models`, `Armies`, `Teams`, `Terrain` — each with `OnObjectCreated`/`OnObjectRemoved` events and an `Objects` enumerable.
- **`IModel`**: Has `Position` (live), `BaseRadiusInches`, `OnPositionChanged`, `OnWoundsDealt`.
- **`IUnit`**: Has `Models` (list of `IModel`) and `PlayerID`.
- **`DataBinding<T>`**: Wrapper around a value stored in `GameDataStore`; `GetValue()` is always current.
- **`LocalMessageBus`**: Implements both `IMessageBusHost` and `IMessageBusClient` — used for single-machine play without a network layer.

## Key Files

```
FdgRaylib/
  Program.cs                        Entry point; forks Raylib/game threads
  Cli/
    CliApp.cs                       App setup: Prepare() + RunAsync()
    ArmyLoader.cs                   Prompts for army choice; EOF → test army
    LocalMessageBus.cs              In-process message bus
    Resolvers/                      One file per request type
  Rendering/
    RaylibRenderer.cs               Raylib window loop + model drawing

FutureOfDarkGrimness/
  GameModel/
    FDGServer.cs                    Runs the state machine, creates army data
    FDGGame_AsLocal.cs              Client-side game instance; holds TableState
  TableState/
    ITableState.cs                  Observable game world interface
    DataState.cs                    Event-driven collection backing TableState
  StageResolution/                  Request/resolver infrastructure
  StateMachine/                     Turn structure, deployment, movement, combat
  Tests/                            NUnit test suite
```

## Army Files

Army lists use the `.fdgarmy` extension (JSON). The CLI prompts for a file path or falls back to a built-in two-unit test army (5× Warriors with rifles + 3× Heavy Gunners with heavy rifles).
