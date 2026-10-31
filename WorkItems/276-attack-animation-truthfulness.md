# 276 — Attack animation truthfulness + occluded shooters still firing

**Status**: implemented — awaiting GUI hand-verification
**Related**: #157 (Takedown per-shot picks), #238/#239 (attack beat + truthful hit share), #017 (melee in-range gating)

## Goal

User-reported: (a) a 3-model Sniper/Takedown squad fires 3 split shots, but EACH shot draws beams
from all 3 models; (b) models with no line of sight to the target still show tracers passing through
the blocker. Audit found (b) is not just visual: the engine rolls attack dice for every copy of the
weapon in the unit as long as ANY model has LoS — the per-model eligibility the targeting UI shows
(`WeaponTargetStats.modelsThatCanShoot`) never trims the roll. GDF: individual models must be within
range and line of sight to fire. Melee is engine-correct (#017 in-range pool) but has the same
visual over-show from out-of-range carriers.

## Plan

Engine (submodule; user authorized engine changes):

1. **Trim ranged attack count to eligible shooters.** `ChooseRangedAttackStage` counts the chosen
   weapon's copies on `modelsThatCanShoot` for the chosen target and trims the queued attack via new
   `ICombatActionContext.TrimPendingAttack(int)`. The #157 Takedown split then splits only the
   trimmed count (an occluded sniper doesn't take a shot). Guarded: eligible == 0 leaves the count
   alone (resolver contract says such targets are unselectable).
2. **Truthful AttackBeat endpoints.** `AttackBeatPositions` gains an endpoints builder used by
   `RollToHitStage`: ranged From filters carriers to those with LoS (unless the weapon ignores LoS)
   AND effective range to some living defender model; melee From filters carriers to
   `MeleeRangeUtilities` melee range; From is then capped/rotated to `WeaponCount` by a new
   `ICombatMetadata.BurstShotIndex` (DIM, default 0) so each split Takedown shot draws ONE beam from
   a DIFFERENT sniper. A Takedown shot's To is the picked model (`IndividualTargetResult`, stashed
   by BuildTargetListStage before RollToHitStage); an ignore-LoS weapon's To is all alive placed
   models (was: LoS-filtered with fallback). Filters fall back to unfiltered when they'd empty.
3. No app-side changes: the GUI overlay/sound plan (`AttackShotPlan`) already keys off `From.Count`.

## Notes

- 2026-07-24: Implemented as planned. Engine: `TrimPendingAttack` +
  `CountEligibleCopies` (ChooseRangedAttackStage), `BurstShotIndex` threaded queue -> metadata,
  `AttackBeatPositions.Endpoints` (ranged LoS+range filter honoring ignore-LoS, melee-range filter,
  burst rotation, Takedown-pick aiming, ignore-LoS To = all placed). +8 tests (2 stage-level trim,
  1 split/burst-index plumbing, 5 endpoints/burst). Engine suite 2109/0, full build clean, headless
  smoke exit 0 (16 attacks resolve; trim never fires on the terrain-less default map, as expected).
  No app-side changes needed - `AttackShotPlan`/overlay/sounds all key off `AttackBeat.From.Count`.
  **Awaiting GUI hand-verify**: (a) HEF Snipers 3x Takedown volley - each shot ONE beam, from a
  different sniper, aimed at that shot's picked model; (b) unit half-hidden behind a wall - no
  tracers through the blocker, and the log shows "N of M ... copies have line of sight and range";
  (c) a wide melee charge - only models in the 2" scrum swing.
- 2026-07-24: Filed from the animation audit. Findings as above; spells unaffected (spell damage
  path skips RollToHitStage; CastSpellStage builds its own beat positions).
