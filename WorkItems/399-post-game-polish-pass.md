# 399 — Post-game polish pass (2026-09-09 session report)

**Status**: done
**Related**: #366 (ValidateNoSelfOverlap), #277 (group formations), #197 (CapabilityOperation log spam),
#393/#394 (terrain palette), #329 (army list cards), #322 (status HUD)

## Goal

Seven defects and one small feature the owner hit in a single game, filed as one item with a facet
ledger rather than seven index lines. Done when every facet below is either shipped (ticked, with its
commit) or explicitly deferred with a reason recorded here.

## Facets

- [x] **A — Group move stacks models with rectangular bases.** Dark Elf Raider jetbikes (35x60mm
  rectangle) end a grouped move on top of each other. `GuiDefineMovementResolver.GroupPositionBlocked`
  skips the moving unit outright, so group mode has no self-overlap gate at all; group models all turn
  to one shared heading (`GroupHeading`), so dragging a row of rectangles sideways swings every 2.36"
  base across its neighbours while the centres keep their spacing. Invisible for circular bases.
- [x] **B — "held in reserve" log spam during a player's turn.** `RuleOperation.DeferDeployment` is a
  marker the deployment subsystem *reads* (its own doc comment says so), but `RuleEvaluator.Log`
  narrates it, and `ChooseUnitToActivateStage.GetUnavailableReason` re-evaluates it for every reserved
  unit on every activation prompt. Same shape as the #197 `CapabilityOperation` spam stream.
- [x] **C — Colon too tight after the player's name** in the status HUD's "Waiting on Bob: ..." line.
  `StatusHudOverlay.DrawWaitingLines` positions three separately-measured runs; raylib's `MeasureText`
  omits the trailing inter-glyph spacing, so every seam loses one `fontSize/10`.
- [x] **D — Terrain: impassible == blocking, no height values.** Authored palette only (owner's call,
  2026-09-09).
- [x] **E — "Activated" tag overlaps a long unit name** in the in-game army list card.
  `ArmyListOverlay.DrawCardHeader` centres the header, then `SameLine`s the tag at the right margin with
  no width reserved.
- [x] **F — Dust cloud when an ambusher arrives.** New engine presentation beat (owner's call).
- [x] **G — "Finish the move" overruns its button** in the done-confirmation popup: `Vector2(160f, h)`
  is hard-coded while the app scales its style and font for the display.

## Notes

- 2026-09-09: **F front-end half shipped.** `DustCloudPlan` (pure geometry) + `DustOverlay` (Raylib
  drawing), on the same active-beat + progress track as the spell and save overlays. Two layers per
  model: a ground ring that snaps out in the first 45% (the kick) and seven puffs that bloom, drift out
  and thin (the cloud), staggered by model index so a rank ripples. No RNG anywhere - the beat crosses
  the wire, so the cloud has to be a pure function of it or two clients would draw different clouds;
  `DustCloudPlanTests` pins that. Drawn over the models: they are already placed when the beat plays,
  so the dust settling is what reveals them. Deliberately no sound cue - `CueFor` falls through to null
  and adding one would mean a new cue key plus placeholder samples, which is a separate change.
- 2026-09-09: Bookkeeping: the submodule pointer bump for B, D and F's engine half rode the facet A
  commit (596b030) rather than getting its own - `git add -A` at the superproject root staged it. The
  cadence held (submodule committed first, pointer bumped alongside app-side changes); noting it so the
  history is not confusing to read back.
- 2026-09-09: **G shipped.** `ResolverPanelLayout.ConfirmButtonWidth` sizes a confirmation row from its
  labels (longest + frame padding both sides, floored at 8 ems so "Yes"/"No" pairs stay clickable).
  Applied to all three popups that carried pixel constants, not just the reported one: the move
  done-confirm (160f), the aircraft fly-off confirm (140f) and the stop-shooting confirm (150f) - the
  same latent overrun in each, since the app scales its font for the display. Tests appended to
  `ResolverPanelLayoutTests`.
- 2026-09-09: **E shipped.** `UnitCardHeaderLayout.Decide` reserves the tag's room first: the header
  centres in what is left when it fits there, otherwise the tag drops to its own right-aligned line
  under the header (what the compact table view already did). Nothing is truncated - the name is how a
  player finds the unit. The header is measured at the 1.12x scale it is DRAWN at, or names just inside
  the boundary would be judged against 1x widths and overlap again. `CenterNextText` gained a
  centre-within-width overload. Tests: `FdgRaylib.Tests/UnitCardHeaderLayoutTests.cs`.
- 2026-09-09: **C shipped.** `StatusHudOverlay.WaitLineRuns` - pure integer layout for the three runs,
  each starting one `GlyphSpacing` past the last one's end. raylib's `MeasureText` is
  sum(advances) + (count - 1) * spacing, no trailing gap, so laying the runs out at raw measured widths
  closed a gap at every seam. Extracted rather than inlined because `MeasureText` needs a loaded font
  and answers 0 with no window - `FdgRaylib.Tests/StatusHudWaitLineTests.cs` pins the offsets instead.
- 2026-09-09: **A shipped.** `GroupSelfOverlap` (new file,
  `FdgRaylib/Rendering/Resolvers/`) holds the pairwise rule as a pure function over poses;
  `GuiDefineMovementResolver.MarkGroupSelfOverlaps` builds the poses and draws one `SelfOverlapLabel`
  caption ("Models Would Stack") between the first flagged pair. Also wired into
  `GuiConsolidationMoveResolver`, which had the identical gap (`PhantomOverlapsOtherUnit` skips the
  moving unit) - a consolidation step is not rigid even when its rotation is, because the formation
  morph, the coherency repair and the per-model table clamp each move one model relative to another.
  Second defect found and fixed in passing: `WouldOverlapAnyModel` (single-model mode) took a
  unitmate's POSITION from its plan but its FACING from its resting attitude, measuring a base that
  exists nowhere - harmless for circles, wrong for rectangles. 7 new tests in
  `FdgRaylib.Tests/GroupSelfOverlapTests.cs`.
- 2026-09-09: **B shipped** (engine). `RuleEvaluator.Log` drops `DeferDeployment` by type.
- 2026-09-09: **D shipped** (engine). Tank traps / Water pool / Rocky ridge are `Solid`; the `Blocker`
  shorthand is gone; every `HeightInches` left the pool and the `Piece()` helper lost the parameter.
  Two `DefaultTerrainPoolTests` inverted (`NoPalettePiece_SeparatesImpassibleFromBlocking`,
  `NoPalettePiece_CarriesAHeightValue`).
- 2026-09-09: **F engine half shipped.** `UnitArrivedBeat` + `PresentationDurations.UnitArrival` (800ms),
  emitted by `StartOfRoundExtraActionStage.PresentArrival` after the placement is applied so the beat
  carries where each model actually landed. `TryGetLaterRoundDefer` gained a named overload so the beat
  can say "Rapid Ambush" / "Ambush Re-Deployment" rather than always "Ambush". Front-end half still to do.
- 2026-09-09: Filed. Owner reported all seven from one game. Root causes traced before any code was
  written; see the facet list. Three design forks put to the owner:
  - Terrain (D): **authored palette only** - `DefaultTerrainPool`'s three Impassible-only pieces become
    `Solid` and the pool stops carrying height values. `HeightInches` stays on `ITerrain` /
    `TerrainPieceEntry` (scenario + layout-file schema unchanged), and a hand-authored layout file or an
    old save can still carry an Impassible-only piece. Deliberately NOT normalized in the `TerrainData`
    constructor.
  - Dust cloud (F): **engine presentation beat**, so the arrival plays on the opponent's screen too and
    paces with the rest of the beat stream rather than firing over whatever else is on screen.
  - Bookkeeping: **one item, seven-facet ledger** (this file), still one commit per facet.

## Decisions

- 2026-09-09: `ETerrainType.Elevated` is left alone. It is declared, unread by any rule, and already
  documented as such in `DefaultTerrainPool` and `TerrainEffectText`; "remove height values" was about
  `HeightInches`, and dropping a flag from a `[Flags]` enum that saves may carry is a separate change.

## Outcome

All seven facets shipped, one commit each, engine first (three submodule commits: the DeferDeployment
log drop, the terrain pool, `UnitArrivedBeat`) then the app. Engine suite 3318 green, app suite 2910
green, headless smoke exits 0.

Two extra defects of the same class were found and fixed alongside the reported ones, rather than left
for the next report:
- `GuiConsolidationMoveResolver` had the identical self-overlap gap as the movement resolver
  (`PhantomOverlapsOtherUnit` skips the moving unit), and a consolidation step is not rigid even when
  its rotation is - the formation morph, the coherency repair and the per-model table clamp each move
  one model relative to another.
- Single-model movement's `WouldOverlapAnyModel` took a unitmate's POSITION from its plan but its
  FACING from its resting attitude, measuring a base that exists nowhere. Invisible for circles.
- The hard-coded confirm-button width was in three popups, not one; all three were sized from their
  labels.

Deliberately NOT done, so it is on the record rather than quietly dropped:
- **Terrain height stayed in the schema.** Per the owner's call the change is authored-palette only:
  `HeightInches` remains on `ITerrain`, `TerrainData`, `TerrainPieceEntry` and `ScenarioFile`, and the
  table tooltip still prints it when a piece has one - so a hand-authored layout or scenario file can
  still set a height, and can still separate Impassible from Blocking. Only the built-in pool is
  normalized. If height is to leave the model entirely, that is a schema change worth its own number.
- **`ETerrainType.Elevated` untouched** - see Decisions.
- **No sound cue for the arrival.** `PresentationSoundCues.CueFor` falls through to null for
  `UnitArrivedBeat`. Adding one means a new cue key plus placeholder samples; the ask was a visual.
- **Aircraft return and Reinforcement arrival get no dust cloud.** They share `PlaceFromReserve` /
  the same stage but come on from a table edge, which is a different entrance; the beat is emitted from
  the ambush path only. Easy to widen if the owner wants it.
- **The dust cloud is unverified on screen.** Its geometry is unit-tested and the beat plumbing is
  integration-tested, but nobody has watched an ambush land in the GUI yet.
