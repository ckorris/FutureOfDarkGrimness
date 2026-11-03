# 362 — Move/placement input fixes: self-click places, rotation gate split, one group heading

## Goal

Three input defects Chris hit in one play session (2026-08-05):

1. **Single-mode move: clicking the model you are moving ate the click.** The #295 click-to-select
   branch ran for ANY hit model, including the already-selected one, so a large base could never
   take a step shorter than its own extent (the whole footprint was a click-swallowing hotspot,
   following the ghost per #312 as the path grew). Reported as "could not move a large vehicle a
   small amount without switching to group mode".
2. **Individual placement mode: rotation (Wheel / R) stopped responding.** Reported as broken
   "since a recent fix that involved rotations" (#341) - but #341 never touched the placement
   resolver; the gate was the defect: wheel AND R sat behind one combined
   `!WantCaptureMouse && !WantCaptureKeyboard` flag, so either capture (hovering the panel kills
   the keyboard path; a latched keyboard capture kills the wheel) silenced both.
   `GuiPlaceOneTerrainResolver` already split them correctly.
3. **Group-mode facings scattered when re-forming a unit deployed with mixed facings** (Chris's
   bikers, Shift+Wheel formation gesture). Each phantom faced its OWN direction of travel, so a
   formation morph - which sends every model a different way - committed a different heading per
   model. #341 made it visible (facings now stick and animate); the derivation was the bug.

Also: Tab/Up/Down model cycling for single-mode consolidation - never existed there (movement got
it in #326; the guide listed consolidation as "the next slice"), Chris expected it.

## Decisions

- **A click on the ALREADY-selected model places a waypoint; a click on a different model still
  switches.** Removing the switch gesture entirely would have stranded consolidation (no roster
  panel there) and broken advertised UI text; the narrow rule fixes the small-move bug and keeps
  every existing surface true. Guide updated both places.
- **`GroupInput.Read(wantMouse, wantKeyboard)`** - wheel gated by mouse capture only, R by keyboard
  capture only, both muted on `WantTextInput`/Esc-menu (same mute as `ResolverHotkeys`). All four
  call sites (placement single+group, movement group, consolidation group) updated.
- **Group mode faces the whole unit along ONE heading**: the centroid travel direction, manual
  wheel offset on top; in-place morphs (no centroid travel) fall back to the average current
  facing, exact-opposed averages to the first model's. Lives in
  `GroupFormationUtilities.GroupHeading` (pure, unit-tested). The committed step stores a per-model
  offset that lands that model's travel-derived facing (`MovementFacingUtilities.WaypointFacings`,
  the executed derivation) on the shared heading, so the executed move matches the phantoms exactly;
  the impassible-sweep preview uses the same per-model offsets. Rigid translations are unchanged
  (per-model travel == centroid travel).
- **Consolidation group mode left alone on purpose**: #250's "slides without rotating" semantic
  preserves each model's own facing; aligning facings there would change a deliberate design. If
  scattered facings annoy in consolidation formations too, that is a fresh decision.

## Notes

- 2026-08-05 — Implemented all four; app suite 1128/1128 (includes 3 new `GroupHeading` tests),
  engine suite 2893/2893, headless smoke exit 0. Awaiting Chris's GUI hand-verify: (a) tiny move of a large vehicle
  in single mode, (b) Wheel/R in individual placement after clicking panel buttons, (c) re-forming
  the scattered bikers - all phantoms and the executed move should share one heading, (d) Tab cycling
  in single-mode consolidation. Not covered by tests (GUI input paths, same gap the ledger notes for
  #341's clamp): the click routing branch and the GroupInput gates themselves.

## Outcome

(open - awaiting GUI hand-verify)
