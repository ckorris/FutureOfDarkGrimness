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
| `ChooseRangedAttackRequest` | `ChooseRangedAttackResolver` | `GuiChooseRangedAttackResolver` | Flattens weapon x target into a single button list. Three reply forms (#315): fire (`Selected` with a target), **hold fire** (`Selected` with a **null** target — decline just this weapon, see below), or `Cancelled` for the single exit the request names. **Exactly one exit is offered**: `AllowCancel` = Back (nothing fired; rewinds to Choose Action) or `AllowStopShooting` = Done shooting (something fired; ends the action). Both reply `Cancelled` — the stage decides which it meant from `AlreadyUsedWeapons`, so a resolver cannot route it wrongly. Done is confirmed in both front ends; Back is not |
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
- **Rotation only shapes the live ghost (#282).** `PathTemplate.AddStep` captures the manual offset
  per waypoint at placement; committed waypoints keep the facing they were placed with (on screen and
  in the executed result), so a late Wheel/R never re-orients the already-planned path.
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
- **One anchor per frame** (#247): `FieldAnchorPlan.Resolve` picks a single winner —
  `hover > (pinned target | move ghosts) > placement ghosts` — which is what keeps two team-coloured
  washes off the same ground. Resolvers report what they *have* to anchor on; the controller decides what
  draws. A move job with nothing pinned shows its own ghosts: **pinning is the gesture** that asks for the
  target-anchored picture, which is why the old `GhostAnchoredField` mode flag is gone (default-off meant
  a plain move drew nothing at all).
- Anything that needs to know whether the drawn field is target-anchored asks `_lastAnchorKind`, not a
  mode flag. Both the band snap and the band rings/labels follow the field, not the move request.

## Explaining rules in-game (#292)

`RuleHoverText` (`FdgRaylib/Rendering/`) is the in-game counterpart to the Army Forge's `RuleTextFlow`:
it splits a stat line into segments, underlines each special-rule name, and reports which one the mouse
is inside so the caller can raise a "Name\ndescription" tooltip. Used by the shoot panel's weapon rows.

Deliberately a **sibling** of `RuleTextFlow`, not a generalization of it, on two counts: in play a rule
is a `ResolvedRule` that carries its own description (no `RuleGlossary` lookup at all), and the shoot
panel paints its rows onto a draw list at computed offsets over one invisible `Selectable`, so nothing
may touch the ImGui cursor — which is precisely what `RuleTextFlow.Draw` is built to do. Keep the two
VISUALLY identical (solid underline = documented, faded = inert in play, 28-em tooltip wrap); a player
should not learn two vocabularies for one idea.

## Canvas hover is a two-way binding (#286)

Where a resolver highlights table models from its dialog, it must do the reverse too. The table hover
arrives through `ICanvasInteractionHandler.GetHoverLabel`, which `TableTooltipOverlay` calls **before**
the resolver's own `Draw` in the same frame (`RaylibRenderer` draws the tooltip overlay first) — so
record the hovered object there, consume it in `Draw`, and clear it at the end of `Draw`. That
single-frame handshake is what `GuiChooseRangedAttackResolver` (`_canvasHoveredOption`) and
`GuiAssignWoundsResolver` (`_canvasHoveredModel`) both use. Seed the frame's emphasis from the canvas
hover and let a hovered dialog row override it, so exactly one object is ever emphasised. If the matching
row can scroll out of view, scroll it back — a highlight the player cannot see is not a connection.

## Sizing a docked panel's content (#288)

A panel whose content should fill the available height must cost its FOOTER first and give the content
the remainder; otherwise a verbose unit pushes Done/Back off the bottom, silently. `PlacementPanelLayout`
holds that arithmetic for `GuiPlaceObjectsResolver` (button heights are constants shared by the drawing
and the measurement, so they cannot drift), while the ImGui measuring — `CalcTextSize(text, false,
wrapWidth)` for wrapped warnings, `ItemSpacing.Y` for the gaps — stays in the resolver. This forces any
variable footer text to be **composed above** the content and **drawn below** it.

## Keys: bind the intent, not the key (#295)

Resolver-wide bindings live in `ResolverKeybinds` — a named intent (`Confirm`, `Back`), the physical keys
behind it, and the `Hint` / `Parenthetical` text that advertises it. Panels ask for the intent
(`ResolverKeybinds.Confirm.IsPressed()`, or just `ResolverButtons.Primary`, which appends the hint itself);
they never name a key or hand-write "(Enter)". Adding Space to Confirm in #295 was a one-line edit there,
and every button, tooltip and Options line followed. Muting (typing / Esc-menu open) and the edge-only
`repeat: false` rule from #240 live in the binding, so a caller cannot forget them.

Panel-LOCAL keys stay put: an option list's number keys, `R` to rotate, `G` for group mode, `Y`/`N`. The
table is for what is shared across resolvers, which is what goes stale in text.

**Selecting one model of a unit is a click on that model** (single-mode movement and consolidation), not a
cycle key — that is what freed Space. The click and the hover highlight that advertises it read the same
`ModelPicker.HitTest`; paint a highlight from anything else and it will eventually disagree with the click.

## Validation gotchas

- **Deployment spacing**: `MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES` is 1.0" base-to-base. Auto-placement uses 0.1" gap, **not 1.0"** — at exactly 1.0", float accumulation during diagonal movement can push models fractionally over the cohesion limit.
- **Movement float precision**: `AutoAdvance` caps `step` at `MaxAdvanceDistance - 0.001f`. Without this margin, the resulting 3D move distance can come out fractionally above `MaxAdvanceDistance` and `ChooseActionStage.GetCanShoot` will block shooting after a legal advance.
- **Back / cancel sentinel**: `GuiSelectionResolver<T>` and `GuiChooseRangedAttackResolver` resolve with `null` when the player clicks Back. Any stage that awaits those requests must null-check the result and activate its `BackToChooseAction` binding rather than proceeding. `ChooseMeleeDefenderStage` and `ChooseRangedAttackStage` already do this.
- **Two nulls that mean different things (#315)**: in a `ChooseRangedAttackRequest` reply, `Cancelled` means "leave this decision" (Back or Done — the request says which), while a `Selected` whose `RangedAttackChoice.TargetUnit` is **null** means "hold fire with THIS weapon and offer me the rest". Read `IsHoldFire`, never `TargetUnit == null` by hand, and never conflate the two: hold fire keeps the shoot action alive, which is what lets a player decline a once-per-game Limited weapon (or a Deadly one that would otherwise gate the unit's ordinary weapons) without giving up the whole shoot.
- **Melee weapon choice hides two option KINDS in one string list (#316)**: `ChooseMeleeWeaponStage` sends a plain `StringSelectionRequest` whose `ValidOptions` mixes "attack with this weapon" rows and `"Hold back: "`-prefixed rows that decline one. Test with `ChooseMeleeWeaponStage.IsHoldBackChoice`, never by position — any resolver that falls through to `ValidOptions[0]` (the AI's catch-all) would otherwise decline its own attacks the moment the ordering changes. That is the same trap the Ambush prompt documents in `AiStringSelectionResolverTests`.
- **Weapon rules do not cross the wire**: `IWeapon.RuleDefinitions` is `[JsonIgnore]`, so a request that reached a remote player carries a weapon with **no rules on it**. Anything the UI needs to say about a weapon's rules must be precomputed onto the request by the stage (`CoverIgnoreRule`, `LineOfSightIgnoreRule`, `LimitedRule`/`LimitedAlreadyFired`), never read off `wo.Weapon` in a resolver.
- **Charge availability**: `ChooseActionStage.GetCanCharge` queries live unit positions and grays out Charge when no enemy is within `MELEE_RANGE_INCHES_HORIZONTAL` (2"). The check re-runs each time Choose Action is entered, so it stays accurate after movement.
