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
| `SelectionRequest<T>` | `SelectionResolver<T>` | `GuiSelectionResolver<T>` | Registered for `UnitData`, `ModelData`, `RectangularZone`. When `AllowCancel`, both front ends offer an exit that resolves `null` — a GUI button and the CLI's `[0]` row — labelled `CancelLabel` ("Back" by default, #335). CLI EOF still auto-picks option 1, cancellable or not |
| `StringSelectionRequest` | `StringSelectionResolver` | `GuiStringSelectionResolver` | |
| `ChooseDeploymentZoneRequest` | `ChooseDeploymentZoneResolver` | `GuiChooseDeploymentZoneResolver` | |
| `ChooseRangedAttackRequest` | `ChooseRangedAttackResolver` | `GuiChooseRangedAttackResolver` | Flattens weapon x target into a single button list. Three reply forms (#319): fire (`Selected` with a target), **hold fire** (`Selected` with a **null** target — decline just this weapon, see below), or `Cancelled` for the single exit the request names. **Exactly one exit is offered**: `AllowCancel` = Back (nothing fired; rewinds to Choose Action) or `AllowStopShooting` = Done shooting (something fired; ends the action). Both reply `Cancelled` — the stage decides which it meant from `AlreadyUsedWeapons`, so a resolver cannot route it wrongly. Done is confirmed in both front ends; Back is not |
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
- **...and only the node it is placed at (#341).** Capturing the offset per waypoint was not enough on its
  own, because the *validators* swept each leg as one rigid base at its ARRIVING attitude — and a swept base
  covers its start point, so the turn was applied to the ground the model set off from. A model hugging a
  wall could not be turned at all. A leg now runs between two attitudes and its rotation is not validated:
  see "The two-attitude leg rule" under Validation gotchas. The turn is instead shown, interpolated per leg
  by `GlideState` from the facings the move beat now carries.
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

**Undo vs back (#343)**: right-click = undo the last action; Backspace = back out, NEVER undo. One key
must not undo in one resolver and abandon the work in another, which is what #248's undo-first-back-second
Backspace did once deployment (where Backspace was always back-only) became cancellable. Deployment's undo
is ACTION-granular (`PlacementHistory`): a group drop or Restart reverses as one step and re-opens the
formation ghost with its rotation, a drag-edit restores the pre-drag pose, and right-click during a
pick-up cancels the pick-up. Movement/consolidation right-click still clears the last waypoint (one per
model in group mode). The GUI auto-placer went with the Auto-place button (AI and CLI-EOF placement have
their own, engine/CLI-side).

**Selecting one model of a unit is a click on that model** (single-mode movement and consolidation). The
click and the hover highlight that advertises it read the same `ModelPicker.HitTest`; paint a highlight
from anything else and it will eventually disagree with the click.

## The model roster (#326)

#295's click-to-select freed Space, but it left **the set of models with no representation outside the
table** — and the only affordance, a hover highlight, appears once the cursor is already on a base, which
during a move is busy aiming the waypoint ghost. Players not told the gesture existed did not find it.

The movement panel now draws a **roster** of the unit's living models in single mode (`ModelRoster` +
`GuiDefineMovementResolver.DrawModelRoster`): "Model N" and distance travelled against that model's own
budget, greyed until it moves, green while it can still shoot, orange once it is rushing. Consequences
worth keeping:

- **The table's left click has one meaning again** — place a waypoint. Selection has its own surface.
- **Selection is two-way bound** (#286): a hovered row washes the model on the table, a model hovered on
  the table paints and scrolls its row back into view. The panel is drawn *after* the table, so the
  roster→table direction uses the same single-frame handshake — `_panelHoveredModel` is written in
  `DrawInfoPanel`, consumed at the top of the next `Draw`, cleared there. It feeds the **highlight only**;
  the click hit test still reads the live `ModelPicker.HitTest`, because a frame-old value recorded while
  the pointer was over the panel must never decide which model a click on the table selects.
- **Keys are additive, never a rebind.** `ResolverHotkeys.CycleDelta()` = Up/Down + Tab/Shift+Tab. The
  tempting key is the Space that used to cycle, but Space is `ResolverKeybinds.Confirm`, whose whole value
  is that one table generates every label, tooltip and Options line — a per-panel exception makes all of
  that text lie. Advertise with `ResolverHotkeys.CycleHint`, never a hand-written "(Tab)".
- **The footer is costed first** (#288): `ModelRoster.FooterHeight` prices the hint block, mode button,
  both checkboxes and the whole button stack; `RosterHeight` takes the remainder, capped at
  `MaxVisibleRows` so a big Tough unit scrolls rather than pushing Done off the bottom.
- `ModelRoster` is arithmetic only (no ImGui), so the budget is unit-tested in `ModelRosterTests` — same
  split as `PlacementPanelLayout` and `ActionMenuLayout`.

Consolidation (`GuiConsolidationMoveResolver`) carries the identical click-to-select gesture and is the
next slice to get the same treatment; until then it keeps the click-only affordance.

## Validation gotchas

- **The two-attitude leg rule (#341)**: a path is a sequence of POSES (position + the facing that node was
  placed with), and the rotation *between* two poses is deliberately not validated — the base turns
  somewhere along the leg and the animation decides when. So the swept tests come in two polarities and a
  caller must say which it means (`MovementUtilities.ELegAttitudeRule`):
  - **Legality** (impassible terrain, enemy pass-through): the leg is blocked only when the swept footprint
    collides at **both** endpoint attitudes — the departing one (the previous node's facing, or the model's
    pre-move resting facing for leg 0) and the arriving one. The OR is per attitude over the **whole**
    obstacle set, never per obstacle. Every node's pose is then checked strictly on its own, which is what
    still refuses a move that ends rotated into a wall; a pose identical to the one before it is skipped, or
    a hold by an already-overlapping model would self-flag (the same reason zero-length legs are skipped).
  - **Hazard detection** (Dangerous / Difficult): unchanged, the arriving attitude alone. "Does this ground
    affect the model" is not "is this move legal", and widening it changes how often units take terrain
    wounds or hit the 6" cap.

  A preview clamp must follow the same rule or it re-introduces the bug one layer up: `EnemyClampTravel`
  allows the farther of the two attitudes. `ClampTravelToTable` (a node-pose constraint) and the
  difficult-terrain clamp (a move cap) are untouched by it. `ValidateMovingThroughImpassibleTerrain`
  delegates to `FindFirstTerrainCrossing`, so the Done gate and the red "show me why" ghost are literally
  one walk.
- **Deployment spacing**: `MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES` is 1.0" base-to-base. Auto-placement uses 0.1" gap, **not 1.0"** — at exactly 1.0", float accumulation during diagonal movement can push models fractionally over the cohesion limit.
- **Movement float precision**: `AutoAdvance` caps `step` at `MaxAdvanceDistance - 0.001f`. Without this margin, the resulting 3D move distance can come out fractionally above `MaxAdvanceDistance` and `ChooseActionStage.GetCanShoot` will block shooting after a legal advance.
- **Back / cancel sentinel**: `GuiSelectionResolver<T>` and `GuiChooseRangedAttackResolver` resolve with `null` when the player clicks Back — as does the CLI `SelectionResolver<T>` on `[0]` (#335). Any stage that awaits those requests must null-check the result and activate its `BackToChooseAction` binding rather than proceeding. `ChooseMeleeDefenderStage` and `ChooseRangedAttackStage` already do this.
- **A cancel that isn't a back-out**: a `null` reply does not always mean "rewind". `ChooseDeployActionStage`'s embark prompt treats it as the *other* deployment (place on the table), which is why the button says what the stage's `CancelLabel` says rather than "Back". When a cancel does something, name it — a player who never presses Back must still be able to find the action (#335).
- **Two nulls that mean different things (#319)**: in a `ChooseRangedAttackRequest` reply, `Cancelled` means "leave this decision" (Back or Done — the request says which), while a `Selected` whose `RangedAttackChoice.TargetUnit` is **null** means "hold fire with THIS weapon and offer me the rest". Read `IsHoldFire`, never `TargetUnit == null` by hand, and never conflate the two: hold fire keeps the shoot action alive, which is what lets a player decline a once-per-game Limited weapon (or a Deadly one that would otherwise gate the unit's ordinary weapons) without giving up the whole shoot.
- **Companion actions on a string menu (#321)**: `StringSelectionRequest.SecondaryActions` maps an option to a second action that BELONGS to it — melee's "hold this weapon back" against the weapon you would otherwise attack with. The companion is still an ordinary entry in `ValidOptions`/`InvalidOptions` and is replied to by its own string; the map only says who owns it. A resolver that understands it **must** draw the companion on its owner's row (a right-hand button sharing the row's letter under Shift) and must **not** also list it as a row — two peer entries for one weapon read as two unrelated choices, which is the confusion this replaced. Skip companions when handing out letter hotkeys too (`GuiStringSelectionResolver.AssignRowLetters`), or they burn the pool and shift everyone else's letter. Any resolver that falls through to `ValidOptions[0]` (the AI's catch-all) must filter companions out first — they are opt-OUTs, and picking one by position is the same trap the Ambush prompt documents in `AiStringSelectionResolverTests`.
- **A shared hotkey needs a discriminating modifier (#321)**: `ResolverHotkeys.IsLetterPressed(letter)` means "that letter, *without* Shift"; the `(letter, shift: true)` overload is the companion's. `ImGui.IsKeyPressed(E)` is true whether or not Shift is held, so without the check one press would fire both actions.
- **Weapon rules cross the wire via the persisted blob (#325)**: `IWeapon.RuleDefinitions` is `[JsonIgnore]`, but `Weapon` rehydrates it from its self-contained persisted blob on deserialization (`[OnDeserialized]`), so a request's weapons arrive with readable rules - names AND descriptions - on remote clients too (they used to arrive rule-less, which silently blanked the shoot panel's rule sublines). The precomputed request facts (`CoverIgnoreRule`, `LineOfSightIgnoreRule`, `LimitedRule`/`LimitedAlreadyFired`, `Forecast`) remain the pattern for anything STAGE-derived - a conclusion that needs the evaluator, tokens or table state must still be computed engine-side and carried on the request, never re-derived in a resolver.
- **Charge availability**: `ChooseActionStage.GetCanCharge` queries live unit positions and grays out Charge when no enemy is within `MELEE_RANGE_INCHES_HORIZONTAL` (2"). The check re-runs each time Choose Action is entered, so it stays accurate after movement.
- **The forced-charge band is a consequence, not a rejection (#206/#334)**: a non-charge move MAY legally end within `ENEMY_STANDOFF_DISTANCE_INCHES` (1", base-to-base) of an enemy — `MovementUtilities.ValidateMovingThroughEnemyUnits` deliberately does not reject it. What it costs is Pass: `ChooseActionStage.GetCanPass` gates on the same proximity one stage later. Never gate Done on it. The predicate itself lives in `ForcedChargeUtilities` (engine) in two forms — `AnyEnemyWithinStandoff` (live positions, IS the gate) and `FindContacts` (hypothetical `StandoffPose`s, for previews) — so a front end that wants to warn ahead of time measures exactly what the gate will. Both movement resolvers do: the GUI draws the 1" band around reachable enemies and tints the ghost inside it, the CLI prints the same warning on the accepted move. Two traps the wording has to respect: the band is *narrower* than the 2" charge band (a unit at 1"–2" may Charge but is **not** forced, and must still be allowed to Pass), and Charge needs a melee weapon while Pass does not — so a rifle-only unit inside the band can do **neither** and falls through to the zero-options fallback.
