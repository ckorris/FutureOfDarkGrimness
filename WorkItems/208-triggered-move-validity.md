# 208 - Triggered moves (reposition-at-activation) submit invalid moves

**Status:** DONE 2026-07-11 (engine change; app pointer bump)
**Related:** #197 (reposition-at-activation primitives: Rapid Blink/Bounding/Wolfborn), #159, #191 (the shared `MovementPlanner.ValidateWithBackoff` G3 ladder this rides on)

## Symptom

Pool baseline games fault with `Triggered move for Witches (Combined) was invalid: Breaks
cohesion` and `...: Moves through an enemy unit, Moves through an enemy unit` - the #197
reposition-at-activation triggered moves construct destinations that fail the movement
validator, and unlike the AI resolvers there appears to be no validate-and-backoff ladder in
that path, so the game faults instead of skipping the ability.

## Repro (deterministic)

`fdglab bench --a "FdgLab/armies/Battle Brothers 2k - Elite Shooting.fdgarmy" --b "FdgLab/armies/
Dark Elf Raiders 2k - Transport.fdgarmy" --games 100 --seed-base 1000` -> seed 1021 (cohesion);
Dark Elf mirror seed 1039 swapped (move-through).

## Notes

- 2026-07-09 - filed. Direction: route triggered-move destination construction through
  MovementPlanner.ValidateWithBackoff (shared G3 ladder), or decline the ability when no valid
  destination exists.
- 2026-07-11 - **fixed.** Root cause confirmed by repro (seed 1021): the post-combat move family
  (Harassing / Hit & Run) is OPTIONAL ("decline = submit a zero move"), but when a unit is still
  intermingled with the enemy after melee its living models are spread >1" apart and cannot re-pack
  into cohesion without a model crossing an enemy base. The AI ladder
  (`MovementPlanner.ValidateWithBackoff`) then bottoms out at its last resort `HoldExactPositions`,
  which is move-through-valid but NOT cohesion-valid - and `GameOperationServices.MoveUnit` THREW on
  that instead of declining. When the unit's current positions already break cohesion there is no
  valid zero-move, so "decline" was unrepresentable. Signed off (owner): the "Both" approach.

## Outcome

Engine-only fix (both files in the `FutureOfDarkGrimness` submodule), owner-authorized for this item:

1. **`GameOperationServices.MoveUnit`** now sets `allowCancel: isOptional` on the movement request
   (the flag was already threaded in but unused), giving `isOptional` meaning: an optional "may move"
   is cancellable, a forced move (spell) is not. On a Cancelled reply OR a path the executor rejects,
   an **optional** move is now DECLINED (logged, unit unmoved, budget kept) instead of faulting; a
   **forced** move still throws, surfacing a genuine engine bug. The invalid-path decline is the
   safety net for resolvers with no cancel channel (headless CLI auto-play).
2. **`AiDefineMovementResolver`** re-validates the ladder's final candidate; when it is invalid AND
   the request is cancellable, it replies `Cancelled` (the clean decline) rather than submitting the
   cohesion-breaking hold. The shared, behavior-pinned `MovementPlanner.ValidateWithBackoff` was left
   untouched (the #191 benchmark-hash pin only affects previously-valid moves; the changed path was
   previously a crash). GUI/CLI human resolvers already handled `AllowCancel`, so they now offer a
   Back-to-decline on optional triggered moves with no code change.

**Tests** (engine): 4 deterministic MoveUnit-level cases in `TriggeredMoveRuleIntegrationTests`
(optional invalid-path -> decline; forced invalid-path -> fault; optional Cancelled -> decline;
forced Cancelled -> fault) + 2 resolver-level cases in `AiDefineMovementResolverTests` (stuck +
cancellable -> Cancelled; stuck + non-cancellable -> still a path). Mutation-checked (neuter the
optional invalid-path skip -> the invalid-path test goes red).

**Verify:** repro seed 1021 (cohesion) and mirror seed 1039 (move-through) now complete cleanly
(logs `Witches (Combined) declines its triggered move - no legal destination.`); the cited
100-game bench (Battle Brothers vs Dark Elf Raiders, seed-base 1000) runs **0 faults**. Full engine
suite 1577/0; app suite 325/0; headless smoke exits 0.

Engine commit: `1e8010e`; superproject pointer bump: this commit.
