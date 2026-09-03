# 347 — Casualty tick marks on unit health bars

**Status**: in-progress (implemented + tested; awaiting GUI hand-verify)
**Related**: #152 (the health bar), #158 (living-model semantics)

## Goal
Thin light rules across the health bar at every point where the unit loses a model — every fifth for a
squad of five, Tough-aware — so "how many more hits before a body drops" reads off the bar. None for a
one-model unit.

## Notes

- 2026-08-05: Implemented. `HealthBarRenderer.CasualtyTicks(IUnit)` returns the boundaries as fractions
  in (0,1); `Draw` takes them as an optional argument and hairlines them over the fill, under the
  border. `TableTooltipOverlay` passes them. Tests in `FdgRaylib.Tests/HealthBarRendererTests.cs`
  (uniform squad, Tough, single-model, mixed unit, empty unit).

## Decisions

- **Boundaries are the running sum of each model's `TotalWounds`, so Tough falls out for free** — a
  squad of Tough(3) ticks every three wounds, which is exactly "where a model is lost".

- **Roster order, and no tick can name a model.** The bar's fill is an aggregate (see `Compute`:
  remaining is summed across every model, floored per model at 0), so there is no ordering that would
  make a tick correspond to a particular casualty. What carries the meaning is the SPACING — "this much
  damage costs you a body" — which is order-independent for the uniform units this is drawn on. A mixed
  unit (a joined Tough hero on 1-wound troopers) gets the right set of boundaries in an arbitrary
  arrangement, which still reads as the right number of ticks in the right sizes.

- **The final boundary is dropped**, since it coincides with the bar's own end and would just double the
  border. That is also what makes a single-model unit return an empty set with no special case.

- **Faint and hairline on purpose.** They are a scale on the bar, not a second thing to read: the fill
  should still be what you see at a glance.

## Outcome
_(pending)_
