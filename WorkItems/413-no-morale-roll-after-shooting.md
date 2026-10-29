# 413 — Hive Guardians took shooting casualties but rolled no morale test

**Status**: implemented (2026-10-28) - skip reason now logged; awaiting owner verify
**Related**: `ResolveRangedMoraleStage` (ShootStage), `ResolveRangedMoraleStageTests`

## Report (Chris, in-game, 2026-10-28)

- The player's APC shot the Hive Guardians (Alien Hives), bringing the unit to **4/9 wounds**.
- No morale test was rolled. The log shows `Resolving ranged morale`, then no roll. The next entries
  were `Entered Choose Action`, then a line saying the APC has no actions remaining.
- Repro save: **`NoMoraleRollForHiveGuardians.fdgsave`** in the root of the `fdg-raylib_Green` clone
  (untracked, saved 20:52 shortly after the shot). The save holds game state only, not the log.

## Goal

Find out why the stage entered and exited without a roll, and either fix it or confirm the skip was
correct and make the log say why. "Done" = a test that loads this situation (or a minimal
reconstruction) and either rolls the test or logs the reason it was skipped.

## Open questions (check before calling it a bug)

- Was a test actually owed? Settle what "4/9" means (wounds remaining or taken) and which threshold
  applies. Hive Guardians are Tough models, so the rule could count models, or wounds for a lone Tough
  model.
- Could the unit be exempt? Candidates: already Shaken, Fearless or a similar rule, or an aura from a
  nearby Hive unit. The save includes Shaken and Fearless tokens, so check what the Guardians carry.
- If it was skipped legitimately, the log should say why rather than going silent after
  `Resolving ranged morale`.

## Notes

- 2026-10-28: Owner agreed: no rules change, just log why the test was skipped. Added
  `MoraleUtilities.DescribeSkippedWoundDrivenMorale`. The shooting and strafing morale stages now log
  `<unit> took no wounds - no morale test.` or `<unit> took wounds but is above half strength
  (2/3 models | 4/6 wounds) - no morale test.` A unit destroyed outright logs nothing extra, since its
  death is already logged. 4 new tests in `ResolveRangedMoraleStageTests` (the #413 repro shape:
  3x Tough(3) shot to 4/9; a lone Tough model; unwounded; destroyed). Engine suite 3390 passed /
  1 skipped. Headless smoke exits 0 and logs the new "took no wounds" line live. Awaiting owner GUI check.

- 2026-10-28: Investigated against the repro save. **No test was owed; the skip was correct.**
  Hive Guardians are 3 models with Tough(3) (9 wounds). In the save, model 1 is dead (3/3 dealt),
  model 2 has 1 wound left, and model 3 is untouched. That is 4/9 wounds remaining but **2 of 3 models
  alive**. `IUnit.GetIsAtHalfStrength` counts models for a multi-model unit (`living*2 <= starting`,
  so 4 <= 3 is false) and counts wounds only for a lone model. That is the GF v3.5.1 rule and was a
  deliberate #091 decision. No Shaken token was involved: the unit carries only
  MovedThisRound/ActivatedThisRound. Hive Bond (+1 morale) only changes the roll, not whether one is
  taken. The real defect is the silent log: `MoraleUtilities.ResolveWoundDrivenMorale` returns `null`
  for three different reasons (destroyed, unwounded, above half), and neither
  `ResolveRangedMoraleStage` nor `ResolveStrafeMoraleStage` logs the `null` case.

## Decisions

## Outcome
