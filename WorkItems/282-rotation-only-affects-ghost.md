# 282 — Movement rotation retroactively re-oriented committed waypoints

**Status**: in-progress (implemented + tested; awaiting GUI hand-verify)
**Related**: #150 (travel-direction facing), #277 (GroupInput), #215 (consolidation group move)

## Goal
Rotating while planning a move (Wheel/R in group mode, R/Shift+R in single mode) must only affect the
live ghost — how the NEXT waypoint will be oriented. Committed waypoints keep the facing they were
placed with, both on screen and in the executed move. Reported while navigating vehicles: rotating
after placing path markers visibly re-oriented the already-placed ones.

## Notes
- 2026-07-26: Confirmed and fixed. Root cause: the manual rotation was a single scalar — per model in
  single mode (`_manualOffsets`), per unit in group mode (`_groupFacingAngle`, accumulated across the
  whole move) — and `PathTemplate.GetResultsAsList`/`MovementFacingUtilities.WaypointFacings` applied
  that one scalar to EVERY waypoint's travel facing. So a late rotation re-oriented the whole committed
  path: the final-ghost marker, the swept-base impassible check (a placed-legal leg could turn
  red/illegal for rect bases), and the executed facings.
- Fix: `PathTemplate.AddStep` now captures the offset per waypoint (`_facingOffsets`, kept in sync by
  RemoveLastStep/ClearModelSteps/ClearAllSteps); `GetResultsAsList(travelDirectionFacing: true)` uses
  the stored per-waypoint offsets (the old `facingOffsets` dict param is gone); new
  `WaypointFacings(..., IReadOnlyList<float> offsetsRadians)` overload; new
  `GetModelFacingOffsets(model)` accessor. GUI resolver: AddStep passes the live offset at commit
  (single: the model's manual offset; group: the accumulated angle); committed final ghost + preview
  endpoint draw with the last STORED offset; the live impassible check uses stored offsets for
  committed waypoints + the live offset for the ghost node only.
- Verified: engine suite 2176/2176 (5 new tests: MovementFacingTests per-waypoint overload x2,
  PathTemplateFacingOffsetTests x3), full build green, headless smoke exit 0.

## Decisions
- Engine-side home: the offsets live in `PathTemplate` next to the waypoints (undo/clear sync comes
  free), not as parallel app-side state.
- Semantics change (intended, per the report): rotating AFTER the last placed waypoint no longer
  changes the final facing — rotate first, then place. A rotation with no subsequent placement does
  nothing, and the preview now shows exactly what will execute.
- The live rotation deliberately persists across placements and undo (attitude control, not per-step
  state): undoing a waypoint does not rewind the wheel.
- Found adjacent, NOT fixed here (needs an owner ruling):
  1. **Consolidation group rotation is preview-only.** `GuiConsolidationMoveResolver` passed
     `facingOffsets` to `GetResultsAsList` without `travelDirectionFacing: true` — a silent no-op, so
     the executed consolidation never carried the rotation the phantoms showed. Also its facing
     semantic differs (rotates `m.Facing` in place, not travel-direction), so wiring it up is a design
     choice, not a mechanical fix. The dead dict code was removed with a comment; behavior unchanged.
  2. **Group-mode phantom impassible check ignores orientation.** The #213 per-phantom path check
     builds `ModelMoveEntry`s without facings, so for rect bases it can disagree with the Done gate
     (which validates with facings). Pre-existing; single mode already carries facings.

## Outcome
(pending GUI hand-verify)

### GUI hand-verify checklist
- Single mode, rect-base vehicle: place 2-3 waypoints, then press R — the committed final ghost must
  NOT rotate; only the mouse ghost does. Place the next waypoint: it takes the new attitude, earlier
  markers keep theirs.
- Group mode (wheel): same — scroll after committing a step; committed markers hold, phantom rotates.
- Executed move (Done): the model turns through the corners with the facings the markers showed at
  placement time (watch the beat playback).
- Undo (right-click/Backspace) then re-place: the re-placed waypoint uses the CURRENT wheel attitude,
  not the undone one's.
- Remote preview (second client): committed endpoint facing holds while the mover scrolls the wheel.
