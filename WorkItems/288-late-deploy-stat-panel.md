# 288 — Late-deploy (Ambush) panel: full-height, tooltip-grade unit stats

**Status**: in-progress
**Related**: #223 (shared `UnitStatBlockRenderer`), Ambush arrival in `StartOfRoundExtraActionStage`

## Goal
`GuiPlaceObjectsResolver.DrawInfoPanel` shows the deploying unit's stats in a fixed 118px scroll box —
far too small for the case it exists for (an Ambush arrival, where the unit is off-table and cannot be
hovered), and it omits rule descriptions. Grow the box to fill whatever vertical space is left above the
hint/footer and give it the full hover-tooltip treatment (weapon rules + unit special rules WITH their
descriptions).

Done when: a late-deploying unit's panel reads like the model hover tooltip and only scrolls when it
genuinely overflows; ordinary deployment placement gets the same box without pushing the Done button
off-panel.

## Notes
- 2026-07-26: filed from a play session ("the scroll view is way too small ... should be stylized more
  like the tooltips when hovering on the model itself").
- 2026-07-26: design fork resolved with the user — **fill remaining panel height** (not a taller fixed
  box, not a collapsible).

## Decisions
- The footer (hint text, cohesion/edge warnings, Done/Back) is measured first and the stat box takes the
  remainder, so the buttons can never be pushed out of the panel by a rule-heavy unit. This forced the
  status/cohesion/edge strings to be COMPOSED above the stat box and drawn below it - the order on screen
  is unchanged, but the heights have to be known before the box is sized.
- The budget arithmetic moved to a non-generic `PlacementPanelLayout` so it can be unit-tested; the
  ImGui measuring (`CalcTextSize` with a wrap width, `ItemSpacing`) stays in the resolver. Button heights
  are constants there, shared by the drawing and the measurement so they cannot drift apart.
- `UnitStatBlockRenderer.Draw` gained an optional `descriptionWrapWidth` (default 300px, which suits the
  auto-sizing #223 tooltip) - the docked column is narrower and rule text was running off its edge.

## Outcome
Shipped 2026-07-26 (`905bbbe`). The stat box now fills the panel above the footer (floored at 90px,
scrolling beyond that) and draws with `includeRuleDescriptions: true`, matching the model hover tooltip.
`UnitStatBlockRenderer` also gained a Wounds line (via #287's `WoundFormat`), which the #223 unit-picker
tooltip picks up too. New `PlacementPanelLayoutTests` (7 tests) pin the footer accounting - the failure
mode being guarded against is silent: the stat box covering Done/Back exactly when a warning appears.
App suite 632/632 green; headless smoke exits 0. Awaiting GUI hand-verify on an Ambush arrival.
