# 413 — Hive Guardians took shooting casualties but rolled no morale test

**Status**: filed (2026-10-28), not investigated
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

## Decisions

## Outcome
