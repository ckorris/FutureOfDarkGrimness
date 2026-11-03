# 157 — Takedown: per-shot target picks

**Status**: implemented — awaiting GUI hand-verification
**Related**: #042 (Takedown rule), #156 (hand-verify round 2 origin), #158 (shooting-target chooser bugs, separate)

## Goal

A unit firing several Takedown weapons (e.g. HEF Snipers — the bundled book's Sniper Rifle carries
Takedown) got ONE "choose the target model" pick that governed the entire volley. Each Takedown shot
should get its own pick, so a volley can spread across different enemy models (OPR: each sniper picks
its own victim).

## How it works

Same-name weapons fire as one batched attack (`SetAttackWeapon` → one `CombatMetadata` with
`WeaponCount = N`), and `BuildTargetListStage.MaybePickIndividualTarget` runs once per attack — hence
one pick per volley. Fix: split the batch into single-shot attacks and loop the fire pipeline.

- `CombatActionContext` now queues pending attacks (`Queue<(Weapon, int)>`) instead of a single
  weapon+count pair; `ConsumeAttackIntoContext` dequeues one per FireStage entry. New surface:
  `HasPendingAttack`, `SplitPendingAttackIntoSingleShots()`, `ClearPendingAttacks()`. Melee and normal
  shooting queue exactly one entry — behaviour unchanged.
- `ChooseRangedAttackStage`: after the weapon/target commit, if the weapon re-scopes to an individual
  model (new non-consuming `SightRuleQueries.TargetsIndividualModels`, so a one-shot granted Takedown
  is still spent by BuildTargetListStage's own evaluation, not the query) and count > 1, the batch is
  split into N single-shot attacks.
- New `DetermineMorePendingShotsStage` between FireStage and ResolveRangedMoraleStage: fires the next
  queued shot while any remain; if the burst's target unit died mid-volley the leftover shots are
  discarded ("Target destroyed - the burst's remaining shots are discarded."). Morale still runs once,
  after the whole volley.
- Each split shot runs the full per-attack pipeline (its own pick, hit/save rolls, wound confinement,
  occlusion) — so per-shot resolution is exact, not an aggregate approximation.

## Notes

- 2026-07-03: Implemented as above. +3 integration tests in `TakedownRuleIntegrationTests` (volley of 3
  splits and each shot picks its own model through the REAL BuildTargetListStage; non-Takedown volley
  stays batched; dead-target fizzle drains the queue). Engine 1092/0, full build clean; headless smokes:
  crafted Takedown-volley army (split fired 3x per volley, exit 0), default EOF game, Alien Hives army —
  all exit 0. The user's "three different units with Takedown, one pick for all shots" observation is
  explained by same-name batching within each unit's volley. **Awaiting GUI hand-verification**: fire a
  multi-sniper volley (e.g. HEF Snipers), expect one pick dialog per shot — each canvas-clickable via the
  #156 round-2 model-pick resolver.

## Decisions

- **Split into real single-shot attacks** rather than collecting N picks up front and distributing the
  batched wounds: hits/saves roll in aggregate for a batch, so wound-to-shot attribution would be an
  approximation; per-shot pipelines reuse the existing machinery exactly.
- **Mid-volley dead target fizzles the rest** — the volley was declared at that unit; remaining shots
  are not retargetable.
- Split only when `count > 1`; a single-copy Takedown weapon behaves exactly as before.
