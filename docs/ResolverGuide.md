# Stage Resolver Guide

Reference moved out of CLAUDE.md (2026-07-08) to keep the always-loaded context lean.
**Read this before touching resolvers, movement, or deployment code** — the gotchas at the
bottom repeatedly cause bugs when missed.

## Stage Resolver Pattern

The engine sends `IStageTaskRequest<TResult>` objects through the message bus whenever it needs a player decision. Resolvers implement `IStageResolver<TRequest, TResult>` and are registered with a `StageResolverRegistry`.

There are **two parallel sets of resolvers**:

- `FdgRaylib/Cli/Resolvers/` — stdin/stdout. Used in headless mode and as fallback. Each handles `null` from `Console.ReadLine()` (EOF) with a sensible default so piped input works.
- `FdgRaylib/Rendering/Resolvers/` — interactive ImGui dialogs and table-canvas interactions. Used in GUI mode. As of this writing **every request type has a GUI resolver**; `BuildGui` registers no CLI fallbacks.

`ResolverRegistryFactory.Build(tableState)` builds the headless registry; `BuildGui(tableState)` returns `(registry, GuiResolverOverlay)`.

## GUI resolver overlay (`FdgRaylib/Rendering/Resolvers/`)

GUI resolvers implement `IGuiResolver`:
- `bool HasPendingRequest` — true while waiting for a click/decision
- `void Draw(int screenW, int screenH)` — called from the main thread inside `rlImGui.Begin()/End()`

`GuiResolverOverlay` holds them all and draws whichever has a pending request. `RaylibRenderer` calls `_resolverOverlay.Draw()` once per frame while in-game.

Resolvers that need to interact with the table canvas (movement, placement) additionally implement `IGuiCanvasOverlay`, which receives `UpdateLayout(scale, originX, originY, tableH)` from the renderer each frame so they can do pixel-to-inch conversion. They draw rings, ghost models, and zone outlines via `ImGui.GetBackgroundDrawList()` — this puts shapes on top of the Raylib canvas but underneath ImGui windows. Mouse hit-testing uses `ImGui.GetIO().MousePos` and respects `WantCaptureMouse` so clicks on info panels don't bleed through to the table.

### Networked previews (#280, opt-in)

A resolver that wants OTHER players to watch its in-progress decision (ghosts, planned paths) implements `IPreviewSource` (`FdgRaylib/Rendering/Previews/`): return a `PreviewState` (payload object per named slot) from `BuildPreviewState()`; `PreviewPublisher` polls the active resolver at ~10 Hz, serializes each slot, and sends only what changed — the engine relays it and `RemotePreviewOverlay` draws it on every other client via a per-payload-type presenter. The movement family shares the `GhostPathBase`/`GhostPathGhosts` payloads ("base" slot = roster + committed waypoints at click cadence, "ghost" slot = live positions referencing the roster by index; `BaseVersion` pairs them) and `GhostPathPreviewPresenter`. New visual families = new payload records + a presenter registered in `GuiResolverOverlay.AttachPreviews`; the engine transport (`IPreviewChannel`/`IPreviewFeed` on `IFDGGame`) never changes. Quantize payload floats via `PreviewQuantize.Inches` (0.01") so the publisher's dedup absorbs mouse jitter.

Ghost-path sources (slice 3): `GuiDefineMovementResolver` (banded), `GuiConsolidationMoveResolver` and `GuiPlaceObjectsResolver<ModelData>` (band `Neutral` — no advance/rush/charge semantics), `GuiAircraftAdvanceResolver` (Advance, no waypoints — the presenter's anchor line doubles as the approach). The presenter draws no start-anchored lines for a model still at the `(0,0)` unplaced sentinel (deployment / reserve arrival).

The objective and terrain placement resolvers place non-model markers (no `ModelId` to resolve receiver-side), so they share the second family instead (slice 4): `ObjectiveMarkerPreview` / `TerrainFootprintPreview` on a single `"marker"` slot, drawn by `MarkerPreviewPresenter`. No base/ghost cache split — the whole preview is one cursor-following ghost of a few hundred bytes (committed markers/terrain reach every client through synced table state already). Terrain footprints cross the wire as flat circles + quads (`MarkerFootprints.Flatten`, rotation baked into the corners) — the preview channel never carries the engine's polymorphic `IZone` (#186 discipline). Both payloads carry the placer's `Valid` flag, so remote outlines go red exactly when the placer's do; both resolvers snapshot the drawn ghost per frame (same `_snapshotRequest` guard as consolidation), so remote players see nothing while the mouse is off the table or a template is still being picked.

## Resolver inventory

| Request | CLI resolver | GUI resolver | Notes |
|---|---|---|---|
| `YesNoRequest` | `YesNoResolver` | `GuiYesNoResolver` | EOF default: `true` |
| `SelectionRequest<T>` | `SelectionResolver<T>` | `GuiSelectionResolver<T>` | Registered for `UnitData`, `ModelData`, `RectangularZone`; GUI has a Back button that resolves `null` |
| `StringSelectionRequest` | `StringSelectionResolver` | `GuiStringSelectionResolver` | |
| `ChooseDeploymentZoneRequest` | `ChooseDeploymentZoneResolver` | `GuiChooseDeploymentZoneResolver` | |
| `ChooseRangedAttackRequest` | `ChooseRangedAttackResolver` | `GuiChooseRangedAttackResolver` | Flattens weapon x target into a single button list; GUI has a Back button that resolves `null` |
| `AssignWoundsRequest` | `AssignWoundsResolver` | `GuiAssignWoundsResolver` | Stateful — `AssignWoundsResults` accumulates clicks; auto-completes when full |
| `DefineMovementPathRequest` | `DefineMovementPathResolver` | `GuiDefineMovementResolver` | Click destination on canvas; whole unit moves same delta |
| `PlaceObjectsRequest<T>` | `PlaceObjectsResolver<T>` | `GuiPlaceObjectsResolver<T>` | Click each model in turn within deployment zone |
| `ChooseAbilityEffectRequest` | `ChooseAbilityEffectResolver` | `GuiChooseAbilityEffectResolver` | #197 P5a. "Pick one of this rule's effects" at activation start (Versatile Attack/Reach, Watchborn). Reply is the chosen option's **index**. Mandatory — no Back, no cancel; the stage only raises it when 2+ effects are available. EOF/AI default: option 0. Its own request type on purpose: `docs/ai-agent-plan.md` A4 swaps AI resolvers in one request type at a time, and riding `StringSelectionRequest` would force an agent to take over Choose Action and the pre-attack menu too, telling them apart by prompt text |
| `ChooseSpellRequest` | `ChooseSpellResolver` | `GuiChooseSpellResolver` | #244. Spell pick + the caster's own boost-token count in one reply (`ChooseSpellReply`: spell **index**, negative = cancel, + boost). Non-castable spells ride along as disabled rows with a reason. Boost UI caps at min(affordable, `MaxUsefulBoost`) — enough to reach the 2+ floor (a natural 1 always fails) plus one per in-range enemy hinder token. EOF default: first castable spell, 0 boost. AI (solo + Tactician): value pick, 0 boost |

(The table lists the original core set; later additions — `GuiUnitSelectionResolver`, `GuiCancellableUnitSelectionResolver`, `GuiCastAssistResolver`, aircraft-advance, terrain-placement, consolidation resolvers — follow the same pattern. See #161 for the consistency pass across them.)

## Group formations (#277)

In Group mode (movement, consolidation, deployment/teleport placement) **Ctrl+Wheel cycles the
unit's formation**; plain Wheel / R / Shift+R still rotate, and Shift-hold stays "stay within
Advance". The shapes come from the engine's `FormationLibrary` (`RowPartitions` -> `LayoutOffsets`,
shared with `CohesiveFormation.PackGrid`), filtered to those whose span respects the 9" all-pairs
rule; app-side state lives in `FormationCycle`, input in `GroupInput` (both
`FdgRaylib/Rendering/Resolvers/`). Conventions:

- **Index 0 = the unit's current shape, unchanged** wherever the unit already stands (movement,
  consolidation, teleport/reposition). A fresh deployment starts at the first legal partition, which
  reproduces the old default (line when it fits, else two balanced rows).
- Movement/consolidation feed the picked shape as the base positions of the two-array
  `PlanGroupMove`, so per-model budgets and terrain clamps apply to the morph exactly as they do to
  the coherency repair. The index resets to "current" on every committed step.
- **Ctrl+Wheel belongs exclusively to the formation cycle.** Alt is the camera/measure modifier:
  the **ruler moved from Ctrl+drag to Alt+drag** (holding Ctrl used to raise `WantCaptureMouse`,
  hiding the wheel from the resolvers) and the **zoom moved from Ctrl+wheel to Alt+wheel**, so
  zooming keeps working while a group ghost is live and nothing contends for Ctrl.

## Ghost-anchored field during placement (#230)

The tactical overlay's ghost-anchored opportunity field ("what can I hit from here" — per-model weapon-range
bands from the *pending* positions, LoS and cover taken from those positions, GPU-rasterized, rebuilt every
frame) was reachable only through `GuiDefineMovementResolver`. Placement now offers its ghosts through
`IGhostFieldSource` (`FdgRaylib/Rendering/Resolvers/`), surfaced as `GuiResolverOverlay.ActiveGhostField` —
the same "the pending resolver's X, if it opts in" pattern as `ActivePreviewSource` /
`ActiveEnemyExclusion`, so the field appears and disappears with the placement and a new opt-in resolver
needs no change in the controller.

- `TacticalOverlayController.DrawField` falls through to `TryDrawPlacementField` when no move job is
  running; `RebuildGhostField` takes `(unit, ghosts, req?)` and the null request simply takes the no-pin
  path (`WeaponRangeOverrides` and secondary contours are both pin-only, and pins are scoped to a move job).
- **Placement is always ghost-anchored**, regardless of `TacticalOverlayConfig.GhostAnchoredField`. That
  flag chooses between "where can I stand to shoot the pin" and "what can I hit from here"; the first has no
  meaning when there is no pin and the ghosts *are* the question.
- Ghosts are read from the canvas pass, which runs **before** the resolver's own `Draw` — so they are one
  frame old, exactly as the movement resolver's always have been.
- **Hotkey `V`** (`ViewSettings.ShowReachOverlay`, default on) is handled globally in
  `TacticalOverlayController.UpdateInput`, **not** in any resolver — the same key must mean the same thing
  while moving, placing and idle. The placement panel and Esc → Options carry the same flag as checkboxes.
  `DrawBandLabels` gates on "a field was drawn this frame" (`_fieldActive`), not on a move request, or the
  band captions naming each weapon would vanish during placement.
- **One anchor per frame** (#247): `FieldAnchorPlan.Resolve` picks a single winner from
  hover / move-ghosts / placement-ghosts / pinned-target, which is what keeps two team-coloured washes off
  the same ground. Resolvers report what they *have* to anchor on; the controller decides what draws.

## Validation gotchas

- **Deployment spacing**: `MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES` is 1.0" base-to-base. Auto-placement uses 0.1" gap, **not 1.0"** — at exactly 1.0", float accumulation during diagonal movement can push models fractionally over the cohesion limit.
- **Movement float precision**: `AutoAdvance` caps `step` at `MaxAdvanceDistance - 0.001f`. Without this margin, the resulting 3D move distance can come out fractionally above `MaxAdvanceDistance` and `ChooseActionStage.GetCanShoot` will block shooting after a legal advance.
- **Back / cancel sentinel**: `GuiSelectionResolver<T>` and `GuiChooseRangedAttackResolver` resolve with `null` when the player clicks Back. Any stage that awaits those requests must null-check the result and activate its `BackToChooseAction` binding rather than proceeding. `ChooseMeleeDefenderStage` and `ChooseRangedAttackStage` already do this.
- **Charge availability**: `ChooseActionStage.GetCanCharge` queries live unit positions and grays out Charge when no enemy is within `MELEE_RANGE_INCHES_HORIZONTAL` (2"). The check re-runs each time Choose Action is entered, so it stays accurate after movement.
