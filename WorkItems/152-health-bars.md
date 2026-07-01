# 152 — Unit health bars on the table

**Status**: in-progress
**Related**: #151 (token chips / `TableTooltipOverlay` — same per-unit canvas overlay it slots into), #056 (presentation beats)

## Goal

A small health bar on each damaged unit on the table canvas so "who's hurt" reads at a glance — complementing the #151 status chips. Civ-style: **hidden on units at full strength**; appears (and drains) as the unit takes wounds. App-side only (`FdgRaylib`), reads `ITableState`.

## Requirements (from the user)

- **Total wounds, granular.** The bar reflects the unit's *total* remaining wounds vs total max, summed across models — so a `Tough` multi-wound model that takes a wound **but doesn't die** still drains the bar. (`UnitData.RemainingWounds`/`MaxWounds` are per-model sums of `TotalWounds`/`TotalWounds − WoundsDealt`.)
- **Floats, not ints.** Wound counts can be fractional on the deterministic (expected-value) dice path, so the fill is a float ratio — never rounded to int.
- **Hide at full strength** (Civ-style): no bar when remaining ≥ max (within a float epsilon).

## Decisions

- **Overkill-safe remaining.** The engine's `RemainingWounds` sums `TotalWounds − WoundsDealt` without a per-model floor, so an over-killed model (WoundsDealt > TotalWounds) subtracts negative health and could wrongly empty a partly-alive unit's bar. The bar computes remaining app-side as `Σ max(0, TotalWounds − WoundsDealt)` (still the granular float sum, just clamped) and uses `MaxWounds` (all models, incl. dead) as the constant denominator — so casualties show as the bar shrinking.
- **Placement (flag for GUI verify):** drawn just *below* the unit's footprint (the top of the unit is already crowded with the name + token chips). Width follows the unit's horizontal span (clamped to a minimum). Easy to move above if it reads better.
- Always shown when damaged (not gated on the `L` label toggle) — health is critical at-a-glance state, like the status chips.

## Notes

- 2026-06-30: Tweaks (user) — bar moved **above** the unit name (was below the footprint), and the colour now **snaps green→yellow at exactly 50%** (flat green above half, yellow→red at/below) instead of easing through it, so the half-strength morale cliff (`IsAtHalfStrength` = `remaining*2 <= max`) reads at a glance. +1 test (`FillColor_SnapsToYellowAtHalfStrength`); app suite 49/0, build clean, headless exit 0.
- 2026-06-30: **Built — awaiting GUI hand-verification.** `HealthBarRenderer` (pure: overkill-clamped float remaining/max from `unit.Models`; `ShouldShow` hides at full within an epsilon; `Fraction` clamps to [0,1]; green→yellow→red `FillColor`) drawn under each damaged unit in `TableTooltipOverlay.DrawUnitOverlays` (bar width = the unit's footprint span, min 22px; centred below the lowest model). Always shown when damaged (not gated on the `L` label toggle). 4 new `HealthBarRendererTests` (hide-at-full incl. float-epsilon + Tough/decimal cases, fraction clamp incl. overkill→0, color ramp endpoints); app suite **48/0**, build clean, headless exit 0. **Verify in GUI:** damage a unit (incl. a Tough monster taking a non-lethal wound) → a bar appears under it and drains; full-strength units show none.
- 2026-06-30: Opened at the user's request after the #151 visuals; branch `152-health-bars` (superproject, stacked on `151-token-display-metadata` so both visual slices are testable together until 151 merges). App-side only — no submodule change.

## Outcome

(TBD)
