# 212 — GUI won't let a unit move THROUGH friendly units

**Status:** DONE 2026-07-11 (app-side only, no engine change). Together with #205 (engine/AI/CLI) and the
already-fine CLI resolver, this completes #182 - the umbrella can be archived.
**Related:** #205 (engine: end-on-friendly forbidden, pass-through allowed - the AI/engine half shipped),
#182 (the original "move through friendlies, don't stop on them"), #011 (ending-stacked checks)

## Report

In the GUI you can't move a unit THROUGH a friendly unit. You should be able to - the rule is only that
you may not FINISH your move on top of a friendly. #205 already made the engine + AI resolvers honour
"pass through OK, end-on not OK"; the GUI movement resolver still blocks the pass-through.

## Where to look

- `GuiDefineMovementResolver` - `WouldOverlapAnyModel` is meant to block only the END position (the ghost),
  but something in the GUI path (ghost validation, the clamp, or the move-preview `ValidatePaths` call) is
  rejecting a path whose interior crosses a friendly. Confirm whether it's the end-overlap check firing
  mid-path, or the path validator treating friendlies as blockers.
- Mirror #205's engine rule exactly: friendlies are NOT pass-through blockers (only enemies are, absent
  Strafing), and only ENDING base-overlapped on a friendly is illegal.
- The GUI already prevents ending-on (WouldOverlapAnyModel) - keep that; just stop blocking pass-through.

## Notes

- 2026-07-11 — filed. This is the GUI counterpart to #205's engine/AI fix; #182 is the umbrella.
- 2026-07-11 — **fixed.** Root cause: `GuiDefineMovementResolver.EnemyClampTravel` (used by BOTH single-mode
  ghost sliding and group-mode feasibility) clamped the move short of EVERY other unit's base - friendlies
  included - so the ghost couldn't slide through a teammate. And the Done gate's `engineValid` used only
  enemy footprints, so removing the friendly clamp would have let a GROUP move end on a friendly with Done
  still enabled -> the engine (#205) would then reject it and throw.

## Outcome

App-side only (`GuiDefineMovementResolver`); no engine change.

- New team-based `IsEnemyUnit` helper (matches the engine's team logic + the resolver's existing enemy-pin
  test). `EnemyClampTravel` now skips non-enemy units, so a unit slides freely THROUGH friendlies in both
  single and group mode. `GetEnemyFootprintsForRequest` switched from player-based to team-based so allied
  units are never pass-through blockers either.
- Ending ON a friendly is still blocked: single-mode placement by `WouldOverlapAnyModel` (unchanged), and the
  Done gate now passes friendly footprints to `ValidatePaths` (new `GetFriendlyFootprintsForRequest`) so a
  group translate that finishes on a friendly disables Done with "Ends stacked on top of a friendly unit" -
  matching the authoritative `DefinePathStage` check (#205), so the GUI can never submit a move the engine
  would reject.

**Verify:** app suite 327/0; build clean. GUI behavior (slide through a teammate, blocked from ending on
one, in both single and group mode) is for Chris to eyeball - it can't be driven headlessly.

Commit: this superproject commit (no submodule bump).
