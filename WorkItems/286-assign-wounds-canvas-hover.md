# 286 — Assign Wounds: hovering a model on the table highlights it AND its list row

**Status**: in-progress
**Related**: #006 (hero-last ordering), #024 (finish a wounded model first)

## Goal
`GuiAssignWoundsResolver` highlights the table model when you hover its dialog row, but not the reverse:
hovering the actual figure on the canvas gives no ring on the model and no emphasis on the matching row
in the panel list. Make the canvas hover a first-class hover — same ring/halo as the row hover, plus a
highlighted row in the dialog so the two surfaces stay connected in both directions.

Done when: hovering a model on the table rings that model and visibly emphasizes its "Model N" row in
the Assign Wounds panel, and hovering the row still rings the model as it does today.

## Notes
- 2026-07-26: filed from a play session.
- The canvas hover already reaches the resolver through `ICanvasInteractionHandler.GetHoverLabel`, which
  `TableTooltipOverlay` calls BEFORE the resolver's own `Draw` each frame — the same one-frame ordering
  `GuiChooseRangedAttackResolver` relies on for `_canvasHoveredOption`.

## Decisions

## Outcome
