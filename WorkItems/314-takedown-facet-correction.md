# 314 — Takedown: wrong LoS/cover facets removed, ordering facet implemented

**Status**: in-progress (implemented + tested; awaiting GUI hand-verify)
**Number**: filed as #311, renumbered #311 -> #313 -> #314 by reconciliation 38 (origin/master had
already merged #311 = pass confirmation, then #313 = shot-eligibility preview parity, while this was
local). Commit messages saying #311 / #313 predate the renumbers.
**Related**: #042 (rule architecture + the checklist row that caused it), #027 (weapon scope), #028 (Deadly-first gating, the mechanism reused), #157 (per-shot Takedown picks)

## Goal
Takedown matches its v3.5.1 rule text and nothing more:

> Takedown: This model may pick any model in the target unit as its individual target, which is
> resolved as if it was a unit of [1]. Takedown attacks must be resolved before other weapons.

That is two facets — the individual-target re-scope (already shipped, #042/#157) and the ordering
clause (never implemented). It is NOT three: the rule grants no line-of-sight bypass and no
cover bypass, and the engine granted both.

## Notes

- 2026-08-02: **Implemented; engine 2557/0, app 872/0, full build clean, default headless smoke exit 0.**
  - **CATALOG** (`CoreRuleCatalog.Takedown`): dropped the two `Shooting_OnSaveRollModifier` HookEntries
    (`Effect.IgnoreLineOfSight`, `Effect.IgnoreCover`). Only the
    `Shooting_OnShootTargetsSelected` -> `Effect.TargetIndividualModel` entry remains. Description
    restated (it advertised the bypasses, and now states the ordering clause instead). Indirect is
    untouched — its own rule text does grant both, and it was the source they were copied from.
  - **ORDERING**: new `WoundPriorityQueries.ShootingResolveFirstSource` = the existing wound-multiplier
    source OR the new `SightRuleQueries.IndividualTargetSource` (named variant of
    `TargetsIndividualModels`, so the reason text is alias-aware). `ChooseRangedAttackStage`'s
    `ApplyDeadlyFirstGating` renamed `ApplyResolveFirstGating` and switched to it; the reason string is
    now built from the rules actually gating ("Must fire Takedown weapons first." /
    "Must fire Deadly(3) / Takedown weapons first.") instead of the hardcoded "Deadly".
    `MustResolveFirst` is now a thin wrapper over a new named `ResolveFirstSource`.
  - **MELEE DELIBERATELY UNCHANGED**: `ChooseMeleeWeaponStage` still asks the wound-only
    `MustResolveFirst`. Takedown's hook is `Shooting_OnShootTargetsSelected`, so a melee weapon carrying
    it re-scopes nothing and must not gate the unit's other melee weapons. (Takedown Strike's melee
    grant runs through `ResolveExtraAttackStage`, not the melee picker, so it is unaffected.)
  - **TESTS**: `IndirectLineOfSightRuleIntegrationTests` — the Takedown occlusion case inverted
    (`Takedown_BehindWall_ShotIsOccluded`, through the REAL `OcclusionCheckStage`) and a new
    `SightRuleQueries_Takedown_IgnoresNeitherLoSNorCover` pins the resolver-facing flags; the Indirect
    derivation test lost its now-wrong Takedown half. `ChooseRangedAttackStageTests` +3 through the real
    stage: Takedown gates the ordinary weapon and names itself in the reason; an out-of-range Takedown
    weapon does NOT gate (mirrors #028's edge, exercising `anyPriorityFireable` on the new source);
    Deadly + Takedown gate the ordinary weapon but not each other.
  - **PROBE** (scratchpad, not committed): a 2-model Snipers unit (Sniper Rifle[Takedown] + Carbine,
    both 30") at z=10, a 5-model squad behind a Blocking+Impassible wall at z=22, and a lone
    unblocked target off to the side. Headless `--scenario`, exit 0. The picker shows
    `[-] Sniper Rifle -> Dummies (5 models, 0 shooters in range...)` — the wall-blocked squad is
    unselectable, which is the reported bug — while both Carbine rows read
    `[unavailable: Must fire Takedown weapons first.]` and become selectable
    (`[2] Carbine -> Tough Dummy`) on the next pass, once the Sniper Rifle has fired.
  - **Comment sweep**: every "Indirect/Takedown" LoS comment corrected across
    `OcclusionCheckStage`, `ChooseRangedAttackStage`, `SightRuleQueries`, `CoverIgnoreContext`,
    `Effect.IgnoreLineOfSight`, `RuleOperation.IgnoreLineOfSight`, and app-side `SightRuleLabel`
    (its "one rule ignores both" example is now Indirect, which is the only such rule).
  - `WorkItems/042-implementation-checklist.txt`'s per-rule mapping row for Takedown corrected in place
    (it is a live reference table, not a dated note); the cont. 30 progress entry stays as written —
    it is history — with a pointer to this item.

## Decisions

- **Origin: a checklist over-read, not a coding slip.** `042-implementation-checklist.txt`'s PER-RULE
  MAPPING listed `Takedown [atk] -> W9 [target individual model, resolve as unit of [1]; ignore
  intervening LoS/cover]`. The bracketed LoS/cover clause belongs to the row above it (Indirect,
  whose text genuinely reads "target non-LoS as if LoS; ignore cover"); nothing in Takedown's text
  grants it. Everything downstream — the catalog hooks, the resolver flags, `SightRuleLabel`'s
  worked example, an integration test asserting the wrong behavior, and the cont. 30 sign-off
  "Takedown and Indirect are now FULLY implemented" — was faithful to that one wrong row. W9 itself
  is just a hook slot ("when this unit selects a shooting target"), not a facet.
- **Cover-ignore removed too, though only the LoS bypass was reported.** Same row, same absence from
  the rule text. Confirmed with the user before changing it, since it is balance-affecting beyond
  the reported bug.
- **Deadly and Takedown share ONE priority class.** Neither rule text claims precedence over the
  other ("resolved first" vs "resolved before other weapons"), so inventing an order between them
  would be a house rule. Both gate the unit's ordinary weapons; neither gates the other; the player
  picks the order between them. This also needed no new machinery — the existing gate is a SET of
  priority weapon keys, so widening the predicate was the whole change.
- **The defender is a neutral stand-in in the priority query**, matching Deadly's existing precedent:
  the gate is per weapon row, not per target row, and Takedown's condition is unconditional. A future
  individual-target rule gated on the defender would need the real target threaded through.

## Outcome
(open — awaiting GUI hand-verify: a sniper with a Blocking wall between it and a target unit should
show that unit greyed out in the shooting picker, and the unit's other weapons should read
"Must fire Takedown weapons first." until the sniper rifle has fired.)
