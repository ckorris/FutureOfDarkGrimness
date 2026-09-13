# 394 — Terrain picker type filter

**Status**: in-progress (implemented + tested; awaiting GUI hand-verify)
**Related**: #393 (the palette growth that motivated it), #268 (palette), #301 (cost rows the filter must not disturb), #346 (the panel's per-piece detail lines)

## Goal
Owner request: buttons at the top of the terrain placement picker to filter by type — impassible, cover,
difficult, dangerous — so that clicking one shows only pieces carrying that attribute. Done = a button
row above the piece list in `GuiPlaceOneTerrainResolver`, All plus the four types, toggling off when the
active one is clicked again, with the list showing only matching rows and saying what it is showing.

## Notes

- 2026-09-06 (verified + pushed): app suite **2836/0**, engine **3175/0**, build clean, headless smoke
  exits 0. The ImGui row itself is not unit-testable, so the filtering behaviour is pinned at the
  `TerrainTypeFilter` seam and the row needs eyes.

## HAND-VERIFY (owner)

In the **Alternating: Points** terrain picker (the filter row only appears while choosing a piece, not
while confirming a position):

1. The row reads All / Impassible / Cover / Difficult / Dangerous and wraps rather than clipping.
2. Click each type - only pieces carrying it remain, and the line under the row says "Showing N of M".
3. Click the active type again, and separately All - both clear back to the full list.
4. **The important one:** filter to a type, pick a piece, place it, and confirm the piece that lands is
   the one whose row you clicked. (A filtered list that returned its own index would place the wrong
   piece; that is what the index test guards, but it is worth one look in the real panel.)
5. The filter persists to the next placement request rather than resetting each turn.
6. In points mode with few points left, an unaffordable row stays dimmed and unclickable under a filter.

- 2026-09-06: `TerrainTypeFilter` (app-side, `FdgRaylib/Rendering/Resolvers/`) holds the four filterable
  flags, their labels, the match predicate, the count, and the summary line — one definition the button
  row and the list both run, so a button can never claim a count the list disagrees with.
  `GuiPlaceOneTerrainResolver.DrawTypeFilterBar` draws the row (flow layout, wraps on a narrow panel);
  `DrawTemplatePicker` skips non-matching rows. Six tests in `FdgRaylib.Tests/TerrainTypeFilterTests.cs`.

## Decisions

- **Filtering is a VIEW; pool indices are untouched.** `TerrainPlacementResult.TemplateIndex` indexes the
  pool the stage holds, so the picker's loop still runs over pool indices and `continue`s past hidden
  rows. Re-packing visible rows into their own list would place the wrong piece — pinned by
  `FilteringIsAView_SoTheSurvivingIndicesStillAddressTheUnfilteredPool`.
- **No Blocking button.** Every Blocking piece in the built-in palette is also Impassible, so a Blocking
  button would duplicate that one's contents. A test pins that premise; if a Blocking-only piece ever
  ships it fails and the button gets added. Elevated has no button either — nothing reads the flag.
- **The filter survives across requests.** One game asks for many placements in a row; re-picking the
  filter for each is exactly the friction this removes. The row is always visible and shows what is
  active, and an empty filtered list says "All shows every piece", so the state can't strand anyone.
- **Thumbnail scale stays keyed to the whole pool**, so a piece is the same size on screen whichever
  filter is up — the picker's point is relative size.
- **CLI picker deliberately NOT filtered** (explicitly deferred, not dropped): its menu is a numbered
  list read from stdin, and filtering would renumber the choices mid-prompt. Its own slice if wanted.
- **Not verified this session.** Owner asked for no builds or test runs; nothing is committed. Needs
  `dotnet build` + the engine suite green, then a GUI hand-verify of the row (the ImGui drawing itself is
  not unit-testable).

## Outcome
_Open._
