# 207 - AI movement/consolidation submits standoff-violating moves (pool games)

**Status:** open (filed 2026-07-09 from the first 2k pool baseline)
**Related:** #206 (standoff semantics vs big rect bases), #159 (AI cohesion crash family), #150

## Symptom

~0.2% of 2k pool baseline games fault with `Ends within 1" of an enemy without charging it` -
from DefinePathStage (AiDefineMovementResolver's submitted move) AND from ConsolidateStage.
Every instance involves the Dark Elf Raiders transport list on one side (large rect-based
transports), echoing #206's rect-base standoff suspicion: if the validator measures the 1" band
against the true oriented rectangle but the AI's gap-targeting measures the circumscribing
circle (or vice versa), the AI aims for a legal-looking gap the validator rejects.

## Repro (deterministic)

`fdglab bench --a "FdgLab/armies/Alien Hives 2k - Horde Melee.fdgarmy" --b "FdgLab/armies/Dark Elf
Raiders 2k - Transport.fdgarmy" --games 100 --seed-base 1000` -> seeds 1002(swapped)/1008/1028
fault in DefinePathStage; 1011(swapped) in ConsolidateStage. Also Dark Elf mirror seed 1024.

## Notes

- 2026-07-09 - filed. The G3 ladder (MovementPlanner.ValidateWithBackoff) exists precisely to
  prevent this class - check whether the failing paths bypass it or whether MinEnemyGap's
  shape-aware measurement disagrees with the validator's for oriented rectangles.
