# 045 — Cover indication in targeting overlay and shot UI

**Status**: implementation complete, awaiting user test
**Related**: #041 (overlay it lives in), #044, #046

## Goal
The engine already distinguishes `Clear` / `Cover` / `Blocking` via `ESightLineEffect`, and `CoverCheckStage` applies +1 to the defender's roll when a majority of defending models are in cover. But the GUI was silent on cover: the movement-targeting overlay only ever drew a green fire line, and the ranged-attack picker just showed the bare word "Cover" without saying what it does. Done when the targeting overlay differentiates Cover from Clear for every fire line it draws, and the shot picker UI tells the player explicitly that Cover adds +1 to the defender's defense roll.

## Notes
- 2026-05-26: Implemented (no submodule changes — engine already had everything needed).
  - `GuiDefineMovementResolver.DrawTargeting`: in the per-selected-model fire-lines section, when picking the nearest in-range enemy model with sight, switched from `HasLineOfSight` to `EvaluateSightLine` so we know whether the chosen path goes through cover. The result is recorded in a `coverTargets` HashSet keyed by enemy model.
  - Each fire line whose target is in `coverTargets` renders as a dashed yellow line (via existing `AddDottedLine`) instead of solid green, and the per-line weapon labels append " (cover)" and render in the same yellow.
  - `GuiChooseRangedAttackResolver`: the per-target list now says `"… in range, Cover (+1 Def)"` (was "Cover"), and the right-pane summary now shows `"Cover  +1 to defense roll"` (was "Cover"). Same yellow.
  - Updated the "Show targeting" checkbox tooltip to call out the cover styling and the +1 modifier.

## Decisions
- **Per-shot cover indication, not per-unit.** The engine's cover rule is unit-wide (majority of defenders must be in cover for the +1). The overlay shows cover for each individual line because that's the actionable information for movement planning: "this particular shot would go through cover terrain". A shot can pass through cover without the unit-wide modifier kicking in (e.g., only one of four defenders in cover), but the visual still tells the truth about the geometry. Worth revisiting if it confuses players in playtest.
- **No change to the aggregate weapon-counts (per-enemy-unit) display.** Adding cover info there would either need a per-weapon breakdown (noisy) or a unit-level cover calc (more expensive and not the same question). Skipped.
- **No change to `CoverCheckStage`.** Reviewed; the majority-rule logic and the per-defender "any attacker sees them through cover" check look correct. Left as-is.
- **No engine changes at all in this item.** Per project memory, prefer engine-side fixes when better — but here the engine already exposes `EvaluateSightLine` returning the categorical effect, so the work is purely presentation.

## Outcome
(pending — implementation complete, awaiting user test before close-out)
