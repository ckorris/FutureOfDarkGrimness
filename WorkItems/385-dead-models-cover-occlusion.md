# 385 — Dead models swayed the cover roll / kept occluded shots alive; share preview+resolution code

**Status**: done (awaiting GUI hand-verify)
**Related**: #158 (dead models must not contaminate targeting - the principle these stages violated), #045/#055 (preview truthfulness), #201 (proximity exceptions ride the shared call), #384 (found during its investigation)

## Goal
`CoverCheckStage` and `OcclusionCheckStage` iterated raw `ModelBindings()` with no `GetIsAlive()`
filter, unlike the targeting stage's preview helpers (which filter alive and whose #158 comment says
they "must mirror CoverCheckStage exactly"). After mid-activation casualties (the OneAtATime flow's
normal state), dead defenders counted in the cover majority's numerator AND denominator, and a dead
model's sight line could keep an occluded volley alive - so the targeting panel and the dice
disagreed. Fix it, and per Chris's direction take the opportunity to make preview and resolution
share one engine-side implementation instead of mirroring by hand.

## What changed (engine)
- **`CoverMajority`** (new, ShootStage/): the one cover-majority computation (living models both
  sides, #201 proximity exceptions threaded through). `CoverCheckStage` rolls it AND the targeting
  stage's cover flag reads it - `ChooseRangedAttackStage.ComputeHasCover` deleted, its logic and
  doc absorbed. The stage's log line now reports living counts.
- **`ShotEligibility.UnitSeesUnit`** (new): the unit-level occlusion gate - any living, placed
  attacker model sees any living, placed defender model, via the same `NearestVisibleModel` the
  previews and attack animation use. `OcclusionCheckStage`'s hand-rolled double loop replaced.
- **`CanWeaponShootAtUnit`** (targeting) is now a thin front on `ShotEligibility.CanHitAny` (its
  doc already claimed they mirrored; now they are one function). Dropped its per-model LoS cache
  (negligible work) and its hand-rolled LoS/range helpers.

After this, targeting eligibility, fire-line previews, attack-beat endpoints, occlusion, and cover
all resolve sight through `ShotEligibility`/`LineOfSightUtilities`, and cover majority through
`CoverMajority` - no hand-mirrored copies left in the shooting flow.

## Notes
- 2026-08-23: implemented + tested. New tests: `CoverMajorityTests` +3 (dead defenders in cover
  don't grant the bonus, dead defenders in the open don't deny it, dead attacker's line grants
  nothing), new `OcclusionDeadModelTests` +3 (dead attacker's clear line doesn't save the volley,
  living converse, dead defender doesn't keep the unit visible);
  `ChooseRangedAttackStageTests.ComputeHasCover_CountsOnlyLivingModels` re-pinned onto
  `CoverMajority`. Engine suite 3004 green; app 1319 green; headless smoke exit 0.
- Deliberate side effect: occlusion/targeting now also skip origin-parked (reserve/embarked)
  models as sight ENDPOINTS, matching `NearestVisibleModel`'s placed filter - previously a parked
  model's phantom position could in principle hold a sight line. Blockers already skipped them.
- GUI hand-verify: after casualties mid-volley, the cover log line ("X/Y defending models in
  cover") counts only living models and matches the targeting panel's cover tag.
