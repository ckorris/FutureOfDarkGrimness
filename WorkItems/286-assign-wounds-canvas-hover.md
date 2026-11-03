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
- The canvas hover SEEDS `_hoveredModel` at the top of `Draw` rather than being a second, parallel
  highlight source: a hovered dialog row then overrides it in the row loop, so exactly one model is ever
  emphasised and the existing `DrawMapHighlight` needed no change at all.
- The canvas-hovered row is scrolled into view when it is off-list. A highlight the player cannot see is
  no connection at all, and a big Tough unit overflows the list easily; the scroll is suppressed when the
  row is already visible so it never fights a deliberate scroll.

## Outcome
Shipped 2026-07-26 (`4fccc96`). `GetHoverLabel` now records `_canvasHoveredModel` (it previously
composed tooltip text and recorded nothing); `Draw` seeds `_hoveredModel` from it, paints the matching
row with a tinted fill + bright border in the same yellow as the ring, scrolls that row into view, and
clears the field at the end of the frame (the same single-frame handshake
`GuiChooseRangedAttackResolver` uses). `Complete` clears both hover fields so a closed dialog leaves no
stale emphasis. 4 new `GuiAssignWoundsResolverTests`; app suite 625/625 green, headless smoke exits 0.
Awaiting GUI hand-verify.
