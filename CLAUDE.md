# FDG Raylib

A Raylib-based client for **Future of Dark Grimness** — a tabletop wargame rules engine. The repository contains two C# .NET 8 projects.

## Git Conventions

- Do not include Claude, AI, or co-author attributions in commit messages. Keep messages brief.
- **Submodule-first commit cadence.** When engine changes are authorized (the `FutureOfDarkGrimness` submodule), commit the submodule first, then bump the superproject submodule pointer together with any app-side changes in a second commit.
- **Verify before committing — never commit red.** Run `dotnet test FutureOfDarkGrimness/FutureOfDarkGrimness.csproj` green, and for app-side changes a full `dotnet build`. When a change touches a playable path, also run a headless smoke (`printf "2\n2\n" | dotnet run --project FdgRaylib/FdgRaylib.csproj -- --headless`) and confirm it exits 0 with the expected log line.
- **Re-verify assumptions before shared/irreversible operations.** Inspect git state before merging to or pushing a shared branch; if a stated premise turns out false (e.g. "master is synced"), surface it before proceeding rather than pressing on.

## Working Conventions

- **One vertical slice at a time.** Implement → add an integration test mirroring the nearest existing `*RuleIntegrationTests` → verify (above) → commit → update the canonical running record (the work item's dated notes / partial-facet ledger). Don't batch unrelated facets into a single change.
- **Never silently cut scope.** When deferring a facet or edge case, say so explicitly and record it in the canonical ledger at the same time — don't drop it quietly.
- **Surface design forks before building anything non-trivial.** Present the options with tradeoffs and a recommendation, and get sign-off before committing to UI or architecture decisions.
- **Game text is ASCII-only.** The ImGui font atlas bakes only Basic Latin + Latin-1 glyphs, so anything beyond U+00FF (em/en dashes `—` `–`, arrows `→`, ellipsis `…`, `≤` `≥` `−` `✓` `✗`, accented letters like `ī`) renders as `?` in-game. No such characters in any user-facing string: log lines, banners, request instructions/labels, UI text, rule/spell descriptions, or book/army data. Use `-`, `->`, `...`, `<=`, `>=`, `x` instead. `OprBookImporter.AsciiFold` scrubs imported OPR text; hand-authored strings must be born ASCII. (Comments and docs are exempt.)

## Work Items

Long-running engineering tasks are tracked outside this file to keep the context budget tight:

- `WorkItemsList.md` (repo root) — numbered index of all known work, always-loaded. Items are roughly Jira-ticket sized.
- `WorkItems/NNN-slug.md` — per-item working memory: goal, dated running notes, decisions, and final outcome. Created when work starts on that item, not preemptively. See `WorkItems/README.md` for the template and conventions.

When working on a numbered item, **append** dated entries to its Notes section (newest on top); record rationale separately in Decisions; write an Outcome and move the index line to `## Done` when finished. Numbers are permanent and never reused.

This file-based system is for durable, cross-session tracking. The built-in Task tool is still the right place for in-session ad-hoc todos.

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

- `ReconcileObjectivesStage` runs at the end of each round: any objective with living models from exactly one player within 3" (base-edge to objective center) is seized by that player; objectives contested by multiple players become neutral; otherwise ownership is preserved. After 4 rounds (the official game length per the GDF rulebook, not a stub) it transitions to `VictoryCalculationStage`.
- `VictoryCalculationStage` tallies controlled objectives per player and calls `IGameContext.NotifyGameEnded(...)` — a unique top scorer wins, ties (or zero objectives controlled) end in a tie.
- The notification propagates: `GameContext.OnGameEnded` event → `FDGServer.OnGameEnded` event → `CliApp` `TaskCompletionSource` → `RunAsync` returns.
- In GUI mode the Raylib window stays open after the game ends; the user closes it manually. (Navigating back to the main menu post-game is **not yet wired up**.)
- In GrimDark Future rules a player can win even if all their models are eliminated (objectives determine the winner), so unit counts must never be used as a win condition.

## Known stubs in the engine

The engine has substantial gaps. Don't assume rules are enforced just because a stage exists. Surveyed Apr 2026 (setup, melee pile-in, and wound auto-fill re-verified 2026-06-14):

**Setup is implemented** (was stubbed at the Apr 2026 survey)
- `MapSetupStage` runs the full sequence: `RollForObjectiveCountStage` rolls D3+2 (3–5 objectives), `RollForFirstObjectivePlacementStage` + `PlaceObjectivesStage`/`PlaceOneObjectiveStage` alternate players placing real `ObjectiveData` (player request, or a debug auto-placer behind `AutoPlaceObjectivesDebug`), and `RollForFirstTerrainPlacementStage` + `PlaceTerrainStage` place terrain (AutoFromLayout / LoadFromFile / Alternating modes).
- `ReconcileObjectivesStage` and `VictoryCalculationStage` (seizure + objective tally → real winner) operate on the objectives `MapSetupStage` actually produces.

**Movement validation is partial**
- `MovementUtilities.ValidateMovingThroughImpassibleTerrain` — implemented; blocks moves whose path intersects any `Impassible`-flagged terrain piece
- `MovementUtilities.ValidateMovingThroughEnemyUnits` — empty (TODO)
- LoS is fully implemented: `ChooseRangedAttackStage` and `OcclusionCheckStage` call `LineOfSightUtilities.HasLineOfSight` with terrain + model-base circular blockers (excluding the attacking and defending unit's own models)

**Melee — partial** (much of the flow works: strike order, swing, strike-back, winner determination, consolidate; the gap below remains)
- `DetermineInRangeAttackersStage` / `DetermineInRangeDefendersStage` — skip range checks; any model can fight
- `PileInStage` — implemented (moves defenders toward the charger via `PileInUtilities.ComputePileInMoves`); no longer a no-op

**Fatigue & morale implemented** (was absent at the Apr 2026 survey; #020 fatigue + #091 morale-core landed since — re-verified 2026-06-28)
- `ApplyFatigueStage` — applies `FatigueUtilities.ApplyFatigued` to units that fought (charge/strike-back/swing); a Fatigued (or Shaken) unit hits only on unmodified 6s in melee (`DetermineHitRollStage`)
- `AssignMeleeMoralePenaltyStage` — real: Shaken, or Rout (lethal-wounds-to-all, since there's no whole-unit removal primitive) when the loser is at half strength
- `RollForMoraleStage` — runs `MoraleUtilities.TakeMoraleTest` (rule-aware: folds `Morale_OnPreMoraleTest` modifiers, Fearless re-roll). The test is invocable on demand — #034's conditional spells (`Effect.MoraleTestThen`) call it to branch on pass/fail

**Round/turn machinery placeholders**
- `ReconcileNewRoundStage` — transitions with no work
- `StartOfRoundExtraActionStage` — implemented: from round 2 it brings reserve (Ambush) units onto the table, offering each owner a Yes/No then placing it >9" from enemies (#042 deploy primitive)
- `ApplyNonMovementTerrainEffectsStage` — implemented: rolls d6 per model whose path crosses `Dangerous` terrain; deals 1 wound on a roll of 1
- `ChooseActionStage` — custom-action branch hardcoded `false`

**Half-built**
- `RangedContext.SetAttackWeapon` and friends — `NotImplementedException` on multiple paths
- `AssignWoundsResults` — no priority for "tough" models, wound-split validation missing (`AutoFill()` itself was rewritten and works: it loops `TryAddWounds` and throws if it can't place every wound)

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
