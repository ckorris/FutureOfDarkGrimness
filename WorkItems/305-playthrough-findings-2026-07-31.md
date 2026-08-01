# 305 — Playthrough findings, 2026-07-31

**Status**: in-progress
**Related**: #197 (Mobile Artillery / MovedThisRound), #151 (token display), #248 (resolver Back), #237 (shoot pre-select)

## Goal
Six observations from a 2026-07-31 GUI playthrough, each resolved or explicitly parked:

1. **"Moved" token visible in game.** It should be hidden unless the bearer actually carries a rule that reads it.
2. **Cannot back out of shooting.** Back must be offered while nothing has fired this shoot action — and likewise after picking a unit to deploy.
3. **Target does not carry across weapons.** After the first weapon fires, the next weapon should start with the same target pre-selected when it is still a legal target.
4. **Deadly appeared to lose save dice.** Traced to Blast (see 5); no separate Deadly change.
5. **Blast's model-count cap was applied to the volley total, not per hit.** Owner ruling: the cap bounds ONE hit's fan-out and the multiplied hits stack.
6. **Morale/Fearless dice on an already-Shaken defender.** Parked — the engine's Shaken auto-fail short-circuit is present and tested; needs a repro save.

## Notes

- 2026-07-31: **(5) Blast — shipped.** `RollToHitStage` computed `min(hits * X, livingModels)`, so an A3 Blast(3) into a 3-model unit produced 3 hits instead of 9, deleting save dice the defender owed. Now `hits * min(X, livingModels)` (floored at 1 so a 0-living-model target is a no-op rather than an erasure). The `HitGroupSource` carries the EFFECTIVE multiplier so the save beat's arithmetic adds up ("3 hits x2 (Blast) = 6"). `Ai/Tactician/CombatMath` mirrors the same change — `CombatMathPinTests.Blast_CappedAtModelCount_MatchesEngine` pins the two against each other and caught the drift immediately. Four new cases in `BlastRuleIntegrationTests` cover the owner's exact examples (9 on 3 models, 6 on 2 models, effective-multiplier tag, dead models don't widen the cap).
  - Test-double gotcha: `FixedDiceRoller` reports `TotalRolls == 1` for ANY roll count, which silently collapses a multi-attack volley to one hit and hides per-hit stacking. The fixture had to move to `FixedFaceDiceRoller`, which honours the count.

- 2026-07-31: **(4) Deadly — no change.** Saves are rolled per HIT (`DetermineSaveRollsNeededStage` emits one `PendingSaveRolls` per `SuccessfulHitInfo`); Deadly multiplies WOUNDS afterwards, at `Shooting_OnPreApplyWound`, and `ConfineToClumps` applies the no-carry-over cap after the saves are already rolled. Nothing in the shooting chain clamps save dice to the target's remaining wounds. Owner agreed the observed "2 saves where 3 were expected" is the Blast total-cap above.

## Decisions

- **Blast's cap is per hit, and multiplied hits stack** (owner-ruled 2026-07-31). An A3 Blast(3) deals 9 into a 3-model unit and 6 into a 2-model unit. The rulebook wording ("no more times than there are models in the target unit") bounds a single hit's fan-out, not the volley.
- The beat carries the **effective** multiplier, not the authored one, so a capped Blast reads truthfully on screen; the authored value stays in the text log.

## Outcome
_(pending)_
