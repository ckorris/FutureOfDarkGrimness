# 207 - AI movement/consolidation submits standoff-violating moves (pool games)

**Status:** CLOSED 2026-07-10 - root cause was engine core, not the AI. Fixed in engine
`ebd2c8f` (Chris-authorized).

## Outcome

Both fault flavors ("Moves through an enemy unit" AND "Ends within 1 inch without charging")
had one root cause: embarked models are parked at Position(0,0) (EmbarkStage), and
GetEnemyModelFootprints included every living enemy model with no on-battlefield filter - so a
loaded enemy transport left invisible passenger footprints at the table-origin corner that any
legal move sweeping or ending near (0,0) collided with. Fix: GetEnemyModelFootprints and
GetEnemyUnitsMovedThrough skip units where !GetIsOnBattlefield() (covers embarked, reserve,
off-table). Pinned by EnemyFootprintTests. Verified: pool baseline v3 (1800 solo games, seeds
1000+) - every seed listed below now clean, 1 remaining fault is #208's triggered-move
cohesion family; 100-game Hives-DE Tactician matchup 0 faults (was 12/50). The AI's move
submission and its G3 re-validation were correct all along.

**Status when filed:** open (filed 2026-07-09 from the first 2k pool baseline)
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

- 2026-07-10 - **ROOT CAUSE FOUND for the "Moves through an enemy unit" flavor (engine core,
  fix awaiting Chris's go-ahead).** `EmbarkStage.cs:61` (and `DefinePathStage.cs:145`) park
  embarked models at `Position(0,0)` - and `MovementUtilities.GetEnemyModelFootprints` includes
  every LIVING enemy model with no on-battlefield filter. A loaded enemy transport therefore
  leaves a stack of invisible passenger footprints at the table-origin corner, and ANY move
  (AI or human) whose swept path passes within (moverRadius + passengerRadius) of (0,0) is
  rejected by DefinePathStage/ConsolidateStage/PileInStage. The Tactician's re-validation
  (MovementPlanner.LiveEnemyFootprints) correctly filters (0,0) models, approves the legal
  move, the engine faults the game. Deterministic repro: seed 3000 Hives-vs-DE smoke, faulting
  side = Tactician's Assault Grunts (a legal move near the corner). Explains why every
  instance involves the Dark Elf TRANSPORT list. The #191 A4 approach term made Hives close
  distance along table edges, raising the hit rate (12 faults in 50 Hives-DE games vs ~3
  before). Candidate fix (one line, engine core): skip units where
  `TransportUtilities.IsEmbarked(...)` / `!GetIsOnBattlefield()` in GetEnemyModelFootprints
  (and audit GetEnemyUnitsMovedThrough, which has the same gap for Strafing triggers).
  The "Ends within 1 inch" flavor may be the same bug (a move ending near the corner) or the
  original rect-vs-circle hypothesis below - re-check after the footprint fix lands.
- 2026-07-09 - filed. The G3 ladder (MovementPlanner.ValidateWithBackoff) exists precisely to
  prevent this class - check whether the failing paths bypass it or whether MinEnemyGap's
  shape-aware measurement disagrees with the validator's for oriented rectangles.
