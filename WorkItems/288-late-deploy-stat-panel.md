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
  remainder, so the buttons can never be pushed out of the panel by a rule-heavy unit.

## Outcome
