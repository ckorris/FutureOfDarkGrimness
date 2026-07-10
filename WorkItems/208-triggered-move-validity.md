# 208 - Triggered moves (reposition-at-activation) submit invalid moves

**Status:** open (filed 2026-07-09 from the first 2k pool baseline)
**Related:** #197 (reposition-at-activation primitives: Rapid Blink/Bounding/Wolfborn), #159

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
