# 458 — A rejected player reply no longer ends the game

**Status**: in-progress (implemented, suite green - awaiting GUI verify)
**Related**: #159 (lenient coherency), #312 (end-facing cohesion), #283 (consolidation rotation), #205/#212 (friendlies), #208 (triggered-move decline), #029 (Aircraft)

## Goal
No player decision the client accepts can come back from the engine as a game-ending error. The client
gates (GUI Done buttons) call the engine's own validator instead of hand-mirroring it, and the engine
answers an invalid reply by asking again and then falling back to a no-op (the unit holds), never by
throwing.

## Notes
- 2026-10-31: Discord report (v0.3.0, platform unconfirmed; no in-app report filed - checked the #226
  drop box, newest is 2026-09-29): "game crash when invalid reposition ... When repositioning charging
  unit after combat". Screenshot: Game Over, "Response to ConsolidateStage was invalid: Breaks cohesion"
  x3, the consolidating unit was Great Tusk-Beast Guard (three rectangular bases, a wipeout after
  strike-back). Exact click path not reproduced (no save); most likely a Group-mode rotation.
- 2026-10-31: Root cause - GuiConsolidationMoveResolver's Done gate ran ValidatePaths, dropped its
  coherency errors, and substituted a hand-mirrored STRICT cohesion check at each base's RESTING facing.
  ConsolidateStage validates LENIENTLY (#159) at the END facing (#312). Rectangles turned in group mode
  could pass the panel and fail the stage. The gate also skipped the end-on-friendly check.
- 2026-10-31: Same hand-mirror in GuiDefineMovementResolver (strict, resting facing, while movement
  turns every base to its travel direction). Its Auto-advance button submitted an unvalidated
  straight-line move. Triggered moves validated STRICTLY in MovementExecutor.TryMove while the CLI
  resolver (and DefinePathStage) were lenient.
- 2026-10-31: Fixed (see Decisions). Audit of every other request type (subagent, read-only) found no
  other client-accepted reply the engine throws on; it did find two engine throws reachable from play
  (Aircraft divergent facing, wound overshoot) - both fixed - and the leftovers under Deferred.
- 2026-10-31: Verified: engine 3393 (1 skipped) and app 3137 tests green, full build, three headless
  games exit 0 with zero re-asks logged. GUI not hand-checked.

## Decisions
- **Gates call the engine validator.** GuiConsolidationMoveResolver.ValidateForDone =
  MovementUtilities.ValidateConsolidationPaths with enemy + friendly + terrain inputs.
  GuiDefineMovementResolver.ValidateForDone = ValidatePaths with per-model budgets, friendlies and
  lenientCoherency. Tooltips now show the engine's reasons (deduplicated) instead of the old
  distance readouts. Auto-advance is disabled (with the reason) when the gate rejects it.
- **Engine re-asks, then holds** (StageResolution/ValidatedRequesting): up to 3 attempts with a fresh
  request each (own TaskID), logging "X: that move is not allowed (reasons). Choose again."; after
  the last, the fallback. Fallbacks are deliberately no-ops, so the fallback can never itself be an
  illegal move:
  - ConsolidateStage: hold (= "Stay in place").
  - DefinePathStage: hold (= "Skip all", a 0" move that spends the Move action). Not Back - a
    deterministic AI handed the action menu again would pick the same move forever.
  - Forced triggered move (spell push): the move fizzles. Cancelled counts as invalid there.
  - Optional triggered moves keep #208's decline-on-invalid (no re-ask), unchanged.
- **Triggered moves are lenient now** (MovementExecutor.ValidateMove), matching DefinePathStage,
  ConsolidateStage and the CLI resolver. Only relaxes for a unit already out of coherency at the start.
  A spread unit's hold, which a forced move used to fault on, is now legal.
- **Aircraft heading**: ForcedAircraftMove.GetHeading threw on models facing apart (reachable via
  single-model deployment in a hand-built list; no bundled book has a multi-model Aircraft). It now
  flies the unit on the first living model's facing.
- **Wound overshoot**: ApplyWoundsStage threw on a tally above a model's remaining wounds with zero
  tolerance, while the assigner clamps with WoundEpsilon. Now clamped (Debug log). Not proven
  reachable; the throw ended the game for a float hair.
- No "slightly illegal move" was needed: every fallback leaves the board as it was.

## Deferred (from the 2026-10-31 audit, not fixed here)
- **Mandatory placement can HANG** (no exit, no throw): spillout / Scout / Ambush / Reinforcements /
  auxiliary spawn via RequestClearPlacement. The GUI only enables Done once every model is placed
  validly and offers no Back; a crowded spillout ring or a late Ambush with nowhere >9" from enemies
  leaves the player stuck. Back won't do (PlacementRequesting throws on a Cancelled mandatory
  placement); needs an auto-place / "commit least-bad" exit using PlacementCommitGuard's fallback.
  Design fork - needs sign-off.
- Deployment cohesion: GUI checks only "within 1" of some placed model" (2D, no 9" rule) and the engine
  checks nothing - illegal deployments are silently accepted (no crash).
- GUI enemy footprints for the move/consolidation gates include off-table (0,0)-parked units the engine
  skips - stricter than the engine, so it can only block, not crash.
- Group-mode coherency repair and the red cohesion hints still measure at resting facing (display only).
- The replay branch (#449) records requests: a re-asked stage now issues up to 3 requests for one
  decision - worth a look when that branch merges.

## Verify (GUI)
1. Consolidate a rectangular-base unit (e.g. Great Tusk-Beast Guard) after a wipeout: rotate it in Group
   mode, mix in Single-mode nudges - Done is greyed with a reason whenever the move is illegal, and the
   game never ends on Done.
2. Move a rectangular-base unit sideways so its bases turn - same: Done greys out rather than erroring.
3. Auto-advance next to impassible terrain or an enemy: the button greys out with the reason.

## Outcome
