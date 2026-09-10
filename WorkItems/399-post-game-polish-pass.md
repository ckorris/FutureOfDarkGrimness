# 399 — Post-game polish pass (2026-09-09 session report)

**Status**: in-progress
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
- [ ] **C — Colon too tight after the player's name** in the status HUD's "Waiting on Bob: ..." line.
  `StatusHudOverlay.DrawWaitingLines` positions three separately-measured runs; raylib's `MeasureText`
  omits the trailing inter-glyph spacing, so every seam loses one `fontSize/10`.
- [x] **D — Terrain: impassible == blocking, no height values.** Authored palette only (owner's call,
  2026-09-09).
- [ ] **E — "Activated" tag overlaps a long unit name** in the in-game army list card.
  `ArmyListOverlay.DrawCardHeader` centres the header, then `SameLine`s the tag at the right margin with
  no width reserved.
- [~] **F — Dust cloud when an ambusher arrives.** New engine presentation beat (owner's call).
- [ ] **G — "Finish the move" overruns its button** in the done-confirmation popup: `Vector2(160f, h)`
  is hard-coded while the app scales its style and font for the display.

## Notes

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

_Written when the item closes._
