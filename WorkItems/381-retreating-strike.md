# 381 — AoF Retreating Strike: post-melee move-end strike

**Status**: todo - BLOCKED on an owner ruling (the trigger question below) before building.
**Related**: #376 (hands this off as its last unresolved name; all recon dates 2026-08-22), #377
(spells may grant it), mirrors the #197 primitive discipline. Engine submodule work expected.

## Goal

The last dead AoF rule name: **Retreating Strike, 14 refs** (the only remaining dead census
entry after #376's five slices). Mechanic, paraphrased: once per round, when the unit ends a
move within 3" of enemy units after having been in melee, its owner picks one of those enemies
and rolls X dice per carrying model; each 6+ deals one wound (unsaveable-style, like Ravage).
Done means: the trigger is owner-ruled, the primitive built with an integration test mirroring
the nearest existing (`CrossingAttackStage` is the near-exact template - move-triggered,
once-per-X, pick-an-enemy, roll-at-6+ wounds via `SyntheticWoundResolution` + the save-skipping
assign/apply child chain), the def authored in `AofRuleSupplement.json`, and the AoF census
reports 0 dead refs.

## The owner ruling needed (deferred 2026-08-22: "I need to look into the rule more")

Engine facts that make this a genuine fork (#376 recon):
- The charger's mandatory 1" post-melee move-back (`ConsolidateStage`) writes positions
  directly - no hook, no MovementExecutor, no MovedThisRound stamp.
- Only the CHARGED unit gets the post-melee triggered move (`PostCombatMoveGate`, the
  Harassing family funnel - which already snapshots before/after positions and owns the
  once-per-round marker discipline).
- `Movement_OnMoveResolved` is a DEAD hook: declared and documented, never fired, no context
  type, no lint entry. Lighting it up generally is a materially bigger slice.

Options (recommendation was A):
- **A. Both combatants, move required**: new stage after PostMeleeStage; ConsolidateStage and
  PostCombatMoveGate record that a unit actually moved post-melee; fires for either combatant
  that carries the rule, moved, and ended within 3" of an enemy. Covers the charger's move-back
  (the rule's likeliest primary use) and Harassing-style moves.
- **B. Charged unit only**: ride PostCombatMoveGate alone. Least wiring; a unit that CHARGES
  with the rule never triggers it - a large silent scope cut vs the text.
- **C. Proximity only**: fire at melee end for any carrier within 3", no move requirement.
  Simplest; over-grants for a stationary defender that never moved.

## Notes

- 2026-08-22 (filed from #376's close; number 381 taken after `git fetch origin`, see
  Reconciliations). Implementation notes banked from the #376 recon:
  - Once-per-round must be a RoundEnd-cleared marker, NEVER ActivationEnd: the trigger fires
    during the ENEMY's activation and the end-of-activation sweep only visits the activated
    unit (the RegenerativeStrengthSpent trap, documented on TokenType).
  - `Cost.OncePerRound` machinery exists but has NO shipped production user yet - if used,
    it needs an integration test.
  - Target pick: `CancellableSelectionRequest<UnitData>` has resolvers on every profile, but
    the solo AI answers EVERY such request with AiChooseMeleeDefenderResolver
    (fewest-living-models) - acceptable for a wound pick, not rule-aware.
  - Proximity: `AbilityTargeting.EligibleTargets` with `TargetSelector(3f, 1, 1, Foe, false)`
    is the canonical "enemies within N inches, on the battlefield" query (the off-battlefield
    guard matters - reserves park at the origin).
  - "Roll X per model with this rule": Effect.DealAutoWounds' carrier counting is exactly this
    (Ravage precedent); X is the rule argument.
  - Dice invariant applies (histogram pools, fractional wound totals, decisive picks).

## Decisions

## Outcome
