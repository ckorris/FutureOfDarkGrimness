# 283 — Consolidation group rotation was preview-only

**Status**: in-progress (implemented + tested; awaiting GUI hand-verify)
**Related**: #282 (spawned from its findings), #215 (group consolidation), #250 (slide without rotating)

## Goal
The wheel rotation in group-mode consolidation must reach the executed move: models end the
consolidation facing the way the phantoms showed, not snapped back to their pre-move facing.

## Notes
- 2026-07-26: Found while fixing #282, fixed on owner's go-ahead. Root cause was two-layered:
  (1) the resolver passed its facing offsets to `GetResultsAsList` without ever setting
  `travelDirectionFacing`, so the argument was a silent no-op and the entries carried no facings;
  (2) even with facings, `ConsolidateStage.ApplyMovements` only ever called `SetPosition` - it
  ignored `ModelMoveEntry.Facings` entirely (unlike `MovementExecutor`).
- Fix (engine): `GetResultsAsList`'s bool param became `EPathFacingDerivation` (None /
  TravelDirection / RotateInPlace); new `MovementFacingUtilities.RotateInPlaceFacings` keeps the
  model's own facing rotated by each waypoint's stored offset (#282's per-waypoint capture);
  `ConsolidateStage.ApplyMovements` now applies entry facings, same contract as `MovementExecutor`.
  CLI/AI entries carry no facings, so those paths are untouched.
- Fix (app): group commit stores `_groupFacingAngle` per step; Done completes with `RotateInPlace`;
  the committed final marker and the remote-preview base slot draw with the last STORED offset (no
  more snap-back after a rotated commit, and a late wheel turn only moves the phantoms - the #282
  discipline); the ghost/phantom slot keeps the live angle.
- Side effect (intended): the Done gate and the authoritative stage now validate rotated rect bases
  by their true oriented swept footprint, since the entries carry facings.
- Verified: engine suite 2179/2179 (3 new: RotateInPlaceFacings, PathTemplate RotateInPlace
  derivation, ConsolidateStage applies facings), full build green, headless smoke exit 0.

## Decisions
- Kept consolidation's rotate-in-place semantic (#250: consolidation slides without facing its
  travel direction) rather than reusing movement's travel-direction derivation - WYSIWYG with the
  phantom preview was the whole point. The derivation choice lives in the engine
  (`EPathFacingDerivation`) because both real semantics now exist; no third variant until demanded.
- Single-mode consolidation has no rotation input; its stored offsets are all 0, so RotateInPlace
  yields the model's own facing - behavior identical, entries just carry explicit facings now.

## Outcome
(pending GUI hand-verify)

### GUI hand-verify checklist
- Group-mode consolidation (win a melee, wipeout = 3" cap), rect-base unit: rotate the wheel, commit
  a step - the committed marker keeps the rotation (no snap-back), further scrolling only turns the
  phantoms.
- Done: the models actually END rotated on the table (before #283 they snapped back to the pre-move
  facing).
- Second client: the mover's committed endpoint holds its rotation while the wheel turns.
