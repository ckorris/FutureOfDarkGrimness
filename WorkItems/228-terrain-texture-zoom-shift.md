# 228 — Terrain texture shifts when zooming

**Status**: todo
**Related**: renderer terrain drawing (`TerrainColors.cs` / terrain draw path in `RaylibRenderer`), #162 (tactical overlay - unrelated texture, but same canvas)

## Goal
Zooming the table view visibly shifts terrain textures relative to the terrain pieces (texture appears anchored to the screen or sampled by pixel rather than to table space). Terrain art should stay glued to its piece across zoom/pan. Reproduce, find whether the texture UVs are computed from screen coordinates instead of table coordinates, fix.

## Notes
- 2026-07-15: Filed from user playtest feedback.

## Decisions

## Outcome
