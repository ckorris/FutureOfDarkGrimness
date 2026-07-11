# 212 — GUI won't let a unit move THROUGH friendly units

**Status:** open (filed 2026-07-11 from Chris's play report)
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
