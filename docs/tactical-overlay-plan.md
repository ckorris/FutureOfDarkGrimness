# Tactical Overlay System — Implementation Plan

**Phase 1 deliverable** (planning session, 2026-07-06, branch `TargetingExperiment`).
Companion to the feature spec ("Tactical Overlay System — Range / Threat / Eligibility Visualization").
Grounded in the codebase as of superproject `master` @ 9c447be / engine `master`.

---

## 0. The invariant, restated

The field textures and contour polylines are *pictures* — approximate, navigational. Every
authoritative determination (pip state, summary counts, snap validation, the promoted
measurement line, fidelity verdicts) is produced by calling the engine's real rules functions
(`LineOfSightUtilities.EvaluateSightLine`, `DistanceUtilities.GetBaseToBaseDistanceInches_3D`,
request-carried effective ranges/budgets). No instrument ever reads a texel. This is enforced
structurally: instruments talk only to `RulesProbe` (§3), which has no reference to the field
grid or texture types.

**No engine (submodule) changes are needed or planned.** Every rules seam this feature needs
already exists and is public — the submodule stays read-only.

---

## 1. Codebase reality the plan builds on

Facts that materially shaped the design (file references are current line numbers):

- **View model**: there is no `Camera2D`, no pan/zoom. `RaylibRenderer.ComputeLayout`
  (RaylibRenderer.cs:422) recomputes `Layout(Scale, OriginX, OriginY, ...)` every frame; scale
  changes on window resize and console expand/collapse. World→screen is
  `sx = originX + x*scale`, `sy = originY + (tableH - z)*scale` everywhere.
- **Draw split**: all Raylib canvas drawing (table, grid, terrain, objectives, tokens) happens
  *before* `rlImGui.Begin()` (RaylibRenderer.cs:332–372); everything ImGui — including every
  existing world-anchored overlay (ghosts, fire lines, range rings, labels) on
  `ImGui.GetBackgroundDrawList()` — paints *above* all Raylib content (374–404).
- **Move job**: `GuiDefineMovementResolver` (1761 lines) is click-to-place waypoints, not
  drag — a ghost follows the mouse between clicks. It already has a rich targeting overlay
  (`DrawTargeting`, from #041/#045): per-enemy-unit aggregate weapon counts with LoS, fire
  lines with cover styling, a per-frame LoS cache, and `LineOfSightUtilities.BuildModelBlockers`
  usage that matches engine verdicts.
- **The request carries rule-true data**: `DefineMovementPathRequest` has `MaxAdvanceDistance` /
  `MaxRushDistance` / `MaxDistanceInches` (hard cap = max(rush, effective charge)), per-model
  `ModelMoveBudgets`, `WeaponRangeOverrides` (#102 effective ranges), and `WeaponSightProfiles`
  (per-weapon cover/LoS-ignore). Bands and pips can be rule-accurate app-side with no evaluator.
- **Cover is real**: `ESightLineEffect { Clear, Cover, Blocking }`,
  `LineOfSightUtilities.EvaluateSightLine(attacker, target, terrain)`;
  `CoverCheckStage` majority rule gives −1 to the save threshold. #045 already renders
  per-line cover in the move overlay — the pip "hatched" state uses the same call.
- **Activation is poll-only**: no "unit activated" / "round changed" events exist. The
  replicated read model is `ITableState.Progress` (`IGameProgress`: `ActivatingUnit`,
  `RoundCount`) plus `GameProgressData.UnactivatedUnits`. Per-unit events that *do* exist:
  `IUnit.OnWoundsDealt`, `IModel.OnPositionChanged`, `ITokenContainer.OnTokenAdded/Removed`
  (Shaken/Fatigued are tokens: `unit.Tokens.HasToken(TokenType.Shaken)`).
- **Charge has no declaration stage**: the Charge action only lights when an enemy is already
  in melee range (`ChooseActionStage.GetCanCharge`); movement *into* charge range is the
  ordinary move whose hard cap already includes effective charge distance. Defender choice
  (`ChooseMeleeDefenderStage`) only sends a request when 2+ defenders qualify.
- **Hit-testing/hover**: `TableHitTester.Update` runs once per frame (RaylibRenderer.cs:379)
  and exposes `HoveredUnit/HoveredModel/Clicked` for all units; `TableTooltipOverlay` routes
  clicks to the active resolver's `ICanvasInteractionHandler` (null when no resolver opts in).
- **Render-texture precedent**: exactly one — `_exclusionRT` (RaylibRenderer.cs:540–580),
  composited in the Raylib pass with the negative-source-height flip. No shaders, no
  `BeginBlendMode` anywhere.
- **Keys in use**: Ctrl (measure), L (labels), **T (dev token reveal — taken)**, R/Shift+R,
  G, Space, Backspace, Shift, Enter, Esc (objective/terrain placement + lobby only — **free
  during movement**). Tab and Alt are free (gated on `WantCaptureKeyboard`).

---

## 2. Integration map

### 2.1 Module and ownership

New directory `FdgRaylib/Rendering/TacticalOverlay/`:

| File | Responsibility |
|---|---|
| `TacticalOverlayController.cs` | All overlay state: pins (ordered list + focus index), hover timer, threat cache, dirty flags, idle-isolation selection. Public surface: `Update(frameTime)`, `UpdateLayout(scale, originX, originY, tableH)`, `DrawField(Layout)` (Raylib pass), `DrawContours(Layout)` (Raylib pass), `DrawInstruments(screenW, screenH)` (ImGui pass), `DrawPanelSection()` (called from the move panel), `NotifyMoveJobStarted(DefineMovementPathRequest)`, `NotifyMoveJobEnded()`, `TryHandleCanvasClick(IUnit, IModel)` |
| `TacticalOverlayConfig.cs` | Single static config block: all opacities, epsilons (snap 0.4", snap-inside margin 0.05", measurement-promote 0.5"), accent palette (teal #2AB7A9, amber #E0A63C, magenta #C05FA0), threat color, hover delay 150 ms, texels-per-inch, hotkeys, rebuild budget 30 ms |
| `FieldGrid.cs` | CPU byte grid at fixed texels-per-inch over the 72×48 table. Cell = packed (bandIndex, losBlocked, coverFlag). Pure geometry: disc-union rasterization, shadow-quad rasterization, polygon scanline fill. No engine references beyond `Position`/zone shapes |
| `FieldCompositor.cs` | FieldGrid → RGBA pixel buffer (accent fill per band, world-space diagonal hatch where cover, transparent where blocked) → `UpdateTexture` into one persistent `Texture2D`(bilinear). Owns the texture lifecycle |
| `MarchingSquares.cs` | Mask → polylines (16-case, saddle via center sample), Douglas-Peucker simplification. Shared by threat frontiers, secondary-pin contours, and focused-band boundary rings |
| `ThreatFrontierCache.cs` | Per-enemy-unit cached charge/shoot reach masks; union + re-march on invalidation; per-unit polylines retained for idle isolation |
| `RulesProbe.cs` | The narrow rules adapter (§2.5). The **only** path instruments use |
| `FidelitySampler.cs` | §6 debug sampler: 2" grid, claimed-vs-probe comparison, mismatch % + board markers |

The controller is constructed in `RaylibRenderer.TransitionToGame` (alongside the other
overlays, RaylibRenderer.cs:118–159) and torn down in `ExitGame`. It receives `ITableState`,
the local `PlayerID`, `Func<PlayerID, Color>`, and the `TableHitTester`.

### 2.2 Rebuild triggers → concrete mechanisms

All engine events fire on the engine thread; **handlers only set dirty flags**. Rebuilds run
on the main thread inside `controller.Update()`, matching the repo's lock-and-flag threading
convention. "Event-driven" means *rebuild only on state change* — a cheap per-frame dirty
*check* against `ITableState.Progress` is the mechanism where no event exists.

Threat frontiers (§3 spec):

| Trigger | Mechanism |
|---|---|
| Enemy unit activates | Per-frame dirty check: compare (`Progress.ActivatingUnit`, hash of `Progress.UnactivatedUnits` membership) against last-seen. Chosen over store subscription because it is identical on host and client and needs no new plumbing |
| Enemy loses models / destroyed | Subscribe `IUnit.OnWoundsDealt` per enemy unit at game start → dirty(unit). (Objects are never removed from the store; death = wounds + `GetIsAlive()` filter — there is no removal event to use) |
| Enemy becomes Shaken | Subscribe `enemyUnit.Tokens.OnTokenAdded/OnTokenRemoved`, filter `TokenType.Shaken` → dirty(unit) |
| New round | Dirty check on `Progress.RoundCount` |
| Toggle-on after stale | Dirty flag set by the toggle itself |

Opportunity field:

| Trigger | Mechanism |
|---|---|
| Pin / unpin / focus / preview change | Controller-internal state transitions |
| Focused target loses a model | `pinnedUnit.OnWoundsDealt` → dirty |
| Moving unit / weapon list changes | New `DefineMovementPathRequest` arrival (`NotifyMoveJobStarted`) |
| Terrain changes | `ITableState.Terrain.OnObjectCreated/OnObjectRemoved` |

Layout/resize never rebuilds anything: the field texture lives in world space at fixed
texels-per-inch and is drawn through the current `Layout` each frame via `DrawTexturePro`.

### 2.3 Input wiring

| Interaction | Wiring |
|---|---|
| Pin click | In `GuiDefineMovementResolver`'s existing left-click branch (single: ~line 400; group: ~line 700): **before** own-model selection / waypoint placement, hit-test enemy models (`BaseShape.ContainsLocalPoint` over units with `PlayerID != request player`, same pattern the resolver already uses for own models). On hit → `controller.TryHandleCanvasClick(...)`, consume the click. Ground clicks keep their current meaning |
| Hover preview | `TableHitTester.HoveredUnit` read in `controller.Update()`; 150 ms accumulator; preview renders at reduced alpha, no chip |
| `Tab` cycle focus | `ImGui.IsKeyPressed(ImGuiKey.Tab)` gated `!WantCaptureKeyboard`, in `controller.Update()` while a move job is live |
| `Esc` | Clears all pins if any exist. **No second-Esc move-cancel** — see conflict C1 |
| Threat toggle | **`F`** (T is taken by the dev token-reveal toggle). Also a toolbar button next to Labels/Grid in `TableTooltipOverlay` (line 96–121) for discoverability |
| `Alt` snap-disable | `ImGui.GetIO().KeyAlt` at drop time |
| Idle isolation click | When no resolver is pending, clicks currently go nowhere (`ActiveInteractionHandler` is null). Controller handles it itself in `Update()`: `_hitTester.Clicked && HoveredUnit is enemy` → isolate; empty-ground click or re-click → clear |

### 2.4 Draw-order slots (spec §5 order, mapped to the real pipeline)

Raylib pass (in `Run`'s in-game branch):

1. `controller.DrawField(layout)` — **between `DrawTableGrid` (:334) and `DrawTerrain` (:335)**. Field under terrain ✓ (deployment zones only exist during the deployment stage and are ImGui-drawn; no interleaving issue in the main phase).
2. `controller.DrawContours(layout)` — **between `DrawTerrain` (:335) and `DrawObjectives` (:336)**. Threat + secondary contours above terrain, *below* objectives/tokens, exactly per spec. Drawn as Raylib `DrawLineEx` polylines with manual dash segmentation (the existing dotted-line helpers are ImGui-side; a small Raylib dashed-polyline helper is new code in the controller).

ImGui pass:

3. `controller.UpdateLayout(...)` alongside the existing overlay layout calls (:383–385); `controller.Update(frameTime)` at the top of the ImGui block (after `_hitTester.Update` :379, so hover state is fresh).
4. `controller.DrawInstruments(screenW, screenH)` — after `_tooltipOverlay.Draw` (:384), before the resolver overlay draw (:389). Background draw list → pips, band-label pills, promoted measurement line, ghost red-tint outlines sit above tokens and under ImGui windows, same layer as the existing ghosts/fire lines.

Chips + live summary: rendered inside `GuiDefineMovementResolver.DrawInfoPanel` via
`controller.DrawPanelSection()` — the panel is already the player's eye-line during a move
job and already hosts the aggregate weapon counts this readout supersedes.

### 2.5 RulesProbe — the §7 adapter (needed: yes)

The #041 LoS cache and blocker assembly live as private code inside the movement resolver, so
no reusable seam exists — a narrow adapter is justified. Signatures (only where they
disambiguate):

```csharp
// All results computed via engine calls; no field/texture types referenced anywhere.
ShotEval EvaluateShot(IModel shooter, Position hypotheticalPos, Weapon w, IUnit target);
//   -> (bool inRange, ESightLineEffect sight, float effectiveRangeInches, IModel nearestTargetModel)
//   assembles Terrain.Objects.Concat(BuildModelBlockers(...)), applies the request's
//   WeaponRangeOverrides and WeaponSightProfiles (cover/LoS-ignoring weapons stay lit).
(float charge, float shoot) ThreatReach(IUnit enemy);   // GetMobility + max weapon RangeInches
float GapToNearestModel(Position pos, IBaseShape shape, IUnit unit);  // 3D surface gap, per rules
```

Per-frame instrument cost: ghosts × pins × weapons × target models ≈ low hundreds of sight
lines vs ~tens of blockers — the same order of work `DrawTargeting` already does every frame.
Correctness over caching, per spec; the #041 committed-position cache pattern is available if
profiling ever disagrees.

---

## 3. Threat & field geometry (data sources)

- **Qualifying enemies**: units present in `Progress.UnactivatedUnits` with
  `PlayerID != local`, alive, minus `Tokens.HasToken(TokenType.Shaken)`.
- **Charge reach disc** per enemy model: `chargeDist + enemyModel.BaseRadiusInches + refRadius`.
  **Shoot reach disc**: `advanceDist + maxWeaponRange + both radii`. `advanceDist`/`chargeDist`
  from `unit.GetMobility(out advance, out charge)` — the unit-data seam (see approximation A2).
- **Reference radius** (`refRadius`): during a move job, the moving unit's modal base radius;
  idle inspection uses the local army's modal radius. Documented on the config constant.
- **Opportunity bands**: per pinned-target model, discs of
  `effectiveRange + shooterModalRadius + targetModel.BaseRadiusInches`, one band per distinct
  *effective* range (deduped after applying `WeaponRangeOverrides`), unioned via max-band-wins
  during CPU rasterization.
- **LoS shadow channel**: per target model, extrude `Blocking`-flagged terrain edges away from
  the model into shadow quads, scanline-rasterized into a per-model shadow mask; a texel is lit
  if *any* target model's mask leaves it unshadowed (matches "sees the unit = sees any model").
  Model-base blockers are deliberately **omitted from the texture** (they move too often and
  are visually tiny); the authoritative pip/count path includes them via `BuildModelBlockers`,
  and the fidelity sampler will quantify the gap (expected: small, localized). Documented
  approximation A3.
- **Cover channel**: same construction with `Cover`-flagged terrain as occluder.
- **v1 treats ground as open for threat reach** (no difficult terrain) — comment in
  `ThreatFrontierCache` + summary note, per spec.

---

## 4. Phased build order

Each phase ends buildable (`dotnet build` green) and manually verifiable in a GUI session.
There is no app-side test project; pure-geometry classes (`FieldGrid`, `MarchingSquares`) are
written testable-in-isolation, and the fidelity sampler is the feature's real verification
harness — hence it lands in Phase 2, not last.

- **P0 — Scaffolding.** Module skeleton, config block, controller constructed/torn down in
  `TransitionToGame`/`ExitGame`, draw/update slots wired (drawing nothing).
  *Checkpoint*: build green, zero behavior change.
- **P1 — Threat frontiers, static → live.** `FieldGrid` disc rasterization,
  `MarchingSquares`, `ThreatFrontierCache`, Raylib dashed/solid contour drawing, `F` toggle +
  toolbar button, all §2.2 threat triggers.
  *Checkpoint*: contours visible from live game state; activating an enemy visibly removes its
  contribution; Shaken enemy projects nothing. → unlocks scenario 5 (core), 6 (toggle part).
- **P2 — Fidelity sampler.** Probe-vs-grid comparison for threat masks (band/LoS/cover checks
  activate as those channels land in P3/P4), mismatch % to the game log, board markers.
  *Checkpoint*: sampler runs on a live game and reports; systematic mismatches in P1 geometry
  fixed now. → scenario 2's verification tooling in place.
- **P3 — Pinning core + bands-only field.** Pin/unpin/focus/hover-preview state machine,
  enemy-click capture in the move resolver, chips row + Esc + Tab, `FieldCompositor` with
  band fills and boundary rings (no LoS/cover channels yet), band labels.
  *Checkpoint*: scenario 1's band visuals and scenario 6's lifecycle (hover preview, Esc,
  auto-clear on Done) demonstrable.
- **P4 — LoS shadows + cover channel.** Shadow-quad and cover rasterization, hatching,
  composite polish; sampler extended to per-channel verdicts.
  *Checkpoint*: scenarios 2 and 3 visuals; sampler mismatch low and edge-localized.
- **P5 — Instruments.** Pips (with `WeaponSightProfile` overrides), live summary readout rows
  + threat row + ghost red tint, distance readout with ≤0.5" promoted measurement line.
  *Checkpoint*: scenarios 1–5 instrument behaviors, all values probe-sourced.
- **P6 — Snapping.** Band snap (inside, 0.05" margin, probe-validated with inward nudge),
  threat snap (outside), band-wins precedence, Alt disable; snapped positions still pass the
  resolver's existing overlap/cohesion/budget validation path.
  *Checkpoint*: scenario 1 end-to-end including drop-snap.
- **P7 — Multi-pin, shoot stage, idle isolation, perf.** Secondary-pin marching-squares
  contours, Tab-focus rebuilds, shoot-stage pip lighting from `ChooseRangedAttackRequest`'s
  `WeaponTargetStats` (authoritative payload — no recompute), idle-click isolation
  brighten/dim, rebuild-budget warning, perf validation.
  *Checkpoint*: scenarios 4, 6 (idle part), 7.

---

## 5. Decision points with pre-committed fallbacks

- **D1 — Field accumulation: CPU byte-grid rasterization (primary) vs GPU max-blend RT
  (fallback).** CPU chosen: the codebase has zero shader/blend precedent; marching squares
  needs a CPU grid anyway, so one grid feeds texture, contours, and sampler from a single code
  path. At 12 texels/inch the grid is 864×576 ≈ 500k cells; disc + polygon scanline fills are
  a few ms. *Fallback*: if composite cost blows the 30 ms budget, drop to 8 tpi first; only
  then move band accumulation to a `RenderTexture2D` with `rlSetBlendFactorsSeparate` max
  blending (the `_exclusionRT` pattern shows the compositing idiom).
- **D2 — Shadow geometry: per-target-model shadow-quad rasterization into the grid (primary)
  vs Red Blob visibility polygons (fallback).** Quads are simpler and union naturally in the
  grid; visibility polygons only if quad artifacts (edge-grazing slivers) prove ugly.
- **D3 — Marching squares: full 16-case with center-sample saddle resolution +
  Douglas-Peucker (primary); fallback: emit raw per-cell segments without polyline joining**
  (dashes degrade to per-segment spacing — acceptable, ugly-but-correct).
- **D4 — Band labels: place the pill at the boundary polyline vertex nearest the moving
  unit's centroid (primary; polylines already exist from D3). Fallback: ray-march from the
  target-unit centroid toward the moving unit's centroid to find the band edge and pin the
  label there** (no polyline dependency).
- **D5 — Summary readout home: inside `DrawInfoPanel` (primary — it's where the player
  already looks, and it supersedes the existing aggregate-counts section). Fallback: a
  separate compact anchored window top-right (the `##tabletools` idiom), if the panel gets
  too tall on multi-pin.**
- **D6 — Enemy threat distances: `unit.GetMobility` + raw max `RangeInches` (primary; see
  A2). Fallback if a `RuleEvaluator` proves reachable app-side during implementation: route
  `MovementRuleQueries.EffectiveMoveShootDistance` / `EffectiveChargeDistanceAgainst` through
  `RulesProbe` behind the same method signatures — callers don't change.**

---

## 6. Spec ↔ codebase conflicts and resolutions

- **C1 — "second Esc cancels the move as it does today": no move-cancel exists today.** The
  movement request has no cancel path (`DefinePathStage` validates or throws; exits are
  Done/Skip/Auto-advance). *Resolution*: Esc clears pins only; a second Esc does nothing. Not
  silently cut — recorded here and in the implementation summary; adding move-cancel is a
  separate work item if wanted.
- **C2 — "Charge declaration: pinning renders a charge-opportunity field": no charge
  declaration exists.** Charge is an action-menu entry only lit when an enemy is already in
  melee range; reaching charge range happens during the ordinary move whose hard cap already
  includes effective charge distance. *Resolution*: the charge-opportunity field becomes a
  band of the move-job pinned field — a distinct "charge" band at
  `min(hard cap, effective charge) + radii`, styled per spec (single band semantics, no LoS
  shadowing on that band). `ChooseMeleeDefenderStage` gets no pin UI in v1 (it often sends no
  request at all — auto-select on a single defender).
- **C3 — `T` is taken** (dev token-reveal). *Resolution*: threat toggle = `F`, noted in the
  toolbar button tooltip.
- **C4 — "under the camera transform" / "pan/zoom never rebuilds": there is no camera.**
  *Resolution*: fixed texels-per-inch world-space texture, drawn through the per-frame
  `Layout`; resize/console changes rescale the draw, never the texture.
- **C5 — Spec draw order vs the Raylib/ImGui split.** Tokens are Raylib-drawn below *all*
  ImGui content, so ImGui-drawn contours could never sit below tokens. *Resolution*: field
  texture and contours draw in the Raylib pass at exactly the spec'd positions (§2.4); pips /
  labels / measurement lines / readouts stay ImGui, above tokens — same layer as the existing
  ghosts and fire lines they annotate.
- **C6 — ASCII-only game text** (CLAUDE.md): the spec's example strings use `⚠`, `″`, `→`,
  `✕`. *Resolution*: `!` for the warning row, `"` for inches, `->` for deltas, `x` for chip
  close buttons. `·` (U+00B7) is Latin-1 and stays.
- **C7 — "dragging" language vs click-to-place waypoints.** *Resolution*: "during a drag" ≡
  whenever the mouse ghost is live (every frame between clicks); "on drop" ≡ the click that
  commits a waypoint. Snapping applies to the commit click (single and group modes alike).
- **C8 — Pips/eligibility vs movement band legality.** A position can be in weapon range yet
  unshootable this activation because the path so far classifies as Rush
  (`GetCanShoot` gates on `MoveDistance > MaxAdvanceDistance`). The resolver already exposes
  band classification (`ClassifyBand`). *Resolution*: when the current path classifies beyond
  Advance, pips and count rows render in their dim state with a `(rush - no shooting)` note in
  the readout — sourced from the same budgets the engine enforces, keeping pips never-wrong.

Documented approximations (restated in the Phase 2 summary per spec):

- **A1** — Threat reach ignores difficult terrain (spec v1 directive).
- **A2** — Enemy threat distances use `GetMobility` + raw weapon ranges; per-unit rule
  modifiers (Fast/Slow, shrouding) are not folded app-side for *enemy* units (no
  `RuleEvaluator` on the client read path today; see D6). The moving unit's own bands *are*
  rule-true via the request payload.
- **A3** — The field texture's LoS shadows use terrain blockers only; model-base blockers are
  authoritative in pips/counts but not painted into the texture. Sampler quantifies the gap.

---

## 7. Risks and containment

- **R1 — `GuiDefineMovementResolver` (1761 lines) regression risk.** Touch points are kept to
  three: the left-click branch (enemy hit-test first), `DrawInfoPanel` (one
  `DrawPanelSection()` call), and a small read-only view of ghost state (selected model,
  per-model ghost final positions, band classification) handed to the controller. All pin
  state lives in the controller. Existing `DrawTargeting` is left untouched in early phases;
  once the readout supersedes its aggregate-counts section (P5), that section is removed in a
  separate commit so it can be reverted independently.
- **R2 — CPU rebuild cost.** Contained by fixed texels-per-inch config (default 12, drop to
  8 first), persistent reused buffers (no per-rebuild allocation), the 30 ms budget log line,
  and per-enemy-unit mask caching (activation = cheap re-union + re-march).
- **R3 — Threading.** Engine events → dirty flags only; every rebuild, texture op, and GL
  call happens in `controller.Update()` on the main thread. No new locks beyond the flag set.
- **R4 — Marching-squares/dash quality burning time.** D3's fallback is pre-approved; do not
  polish past "clean at 1×–2× zoom" before P7.
- **R5 — Rules-call cost during drags.** Per-frame probe work is bounded (§2.5) and mirrors
  what `DrawTargeting` already does; the #041 cache pattern is the escape hatch.
- **R6 — No app-side test harness.** Geometry classes are engine-free and deterministic; the
  fidelity sampler (P2) is the acceptance instrument, and each phase checkpoint names a
  manual GUI verification. Full headless smoke stays green throughout (overlay never runs in
  CLI mode).

---

## 8. Seams left for non-goals (do not build in v1)

- Difficult-terrain-aware threat: `ThreatFrontierCache` mask generation is a single method
  per unit — a terrain-aware reachability fill swaps in behind it.
- Per-model target assignment UI: pips already key (model, weapon, pin); an assignment layer
  would consume the same probe results.
- Hold-key emphasis flip / opportunity∩safety intersection: both are `FieldCompositor`
  restyles over channels the grid already carries; config reserves a hold-key slot.
- Fatigue-dimmed frontiers: per-unit polyline styling hook in `ThreatFrontierCache` (already
  needed for idle isolation brighten/dim).
- Charge path legality: a `RulesProbe` method slot next to `ThreatReach`.

---

## 9. Ledger

At implementation start, register this as work item **#162 — Tactical overlay (range/threat/
eligibility visualization)** in `WorkItemsList.md` under *Client / renderer*, with
`WorkItems/162-tactical-overlay.md` carrying dated notes per repo convention; this plan file
remains the design reference.
