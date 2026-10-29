# 425 — Cumbersome (new core); Artillery and Indirect 3.6; Quick Throw

**Status**: todo
**Related**: #415, #360 (Indirect/Mobile Artillery move penalty in AI), #290 (advance-and-shoot gate), #436 (Mobile Artillery / Re-Position Artillery dropped)
**Branch**: `feature/opr-3.6`. Model/effort: Opus 5.5 / medium.

## Goal

Core Rules v3.6.0:
- Cumbersome (new): "May only use Hold or Advance actions, but can't shoot when moving." Carried by artillery
  weapons/units (18 GDF + 19 AoF books).
- Indirect: "When using Hold actions and shooting, may target enemies that are not in line of sight as if in
  line of sight, and ignores Cover from Sight Blockers." (3.5: -1 to hit after moving, LoS/cover bypass
  unconditional.)
- Artillery: "When this model uses Hold actions and shoots at enemies over 12" away, it gets +1 to hit rolls.
  If most models in this unit have this rule, as long as they haven't moved during this round, enemy units
  shooting at them from over 12" away get -1 to hit rolls."
- Book rule Quick Throw: "This model may always target enemies that are not in line of sight when shooting with
  Indirect weapons (instead of only when using Hold actions)."
- Dropped in 3.6: Mobile Artillery, Re-Position Artillery (#436 removes them).

"Done" = a Cumbersome unit's action menu offers Hold and Advance only, and Advance is move-only (no shoot
after move; "can't shoot when moving" - a Cumbersome unit that Advances 0" is still a Hold, record the
ruling); Indirect's LoS bypass and Sight-Blocker-cover bypass apply only on Hold (move-then-shoot needs LoS
and gets cover normally), the -1 after moving is gone; Artillery's defensive -1 is gated on most models +
unmoved this round; Quick Throw lifts Indirect's Hold condition.

## What changes

1. Engine: new `CoreRuleCatalog.Cumbersome` (action gating via the capability query the Immobile rule uses;
   shoot-after-move denied); Indirect re-authored with an `IsHoldAction` condition on both facets;
   Artillery's defensive facet re-gated (most models via #428's condition + unmoved token); `Quick Throw`
   supplement definition as an Indirect-condition override (simplest: a weapon/unit marker read by the
   Indirect condition).
2. Resolvers: Choose Action menu greys Rush/Charge for Cumbersome with the reason text; the shooting target
   list hides out-of-LoS targets for a moved Indirect unit; CLI equivalents.
3. AI: the Indirect/Mobile Artillery pricing in #360 becomes Hold-vs-Advance pricing for Indirect and
   Cumbersome.
4. Tests: Cumbersome action set; Cumbersome Advance cannot shoot; Indirect on Hold targets out of LoS; after
   Advance it cannot; Artillery -1 only unmoved and most-models; Quick Throw lifts the Hold gate.

## Acceptance

Tests green; smoke exit 0; hand-verify (Chris): an artillery unit's action menu and a moved Indirect unit's
target list.

## Code anchors (survey 2026-10-29)

- `CoreRuleCatalog.Artillery` / `.Indirect` / `.Immobile`, `ChooseActionStage` action gating, `ShootAfterRushRules`,
  `SightRuleQueries`, LoS/cover evaluation in the shooting stage. See the appended survey rows below.

### Survey rows (code survey 2026-10-29)

Findings that shape the item: Indirect today ignores ALL cover (3.6: only cover from Sight Blockers) and
has no Hold condition; Immobile's Hold-only gating is the precedent for Cumbersome; Quick Readjustment
(dropped by 3.6) has tests that must go with #436.

- Indirect `CoreRuleCatalog.cs:140-159` (:143-146 -1 when `AfterMoving && !IsMelee`; :147-150 IgnoreLineOfSight
  Always; :151-154 IgnoreCover Always). `Condition.AfterMoving` `Condition.cs:405-408`. Queries
  `Rules/Dispatch/SightRuleQueries.cs` (IgnoresCover :35, LineOfSightIgnoreSource :45, IgnoresTerrain :60).
  Sight-blocker flag `TableState/Zones/Terrain/ETerrainType.cs:12` (`Blocking`). AI `CombatMath.cs:21/:119`,
  `TacticianWeights.cs:220`.
- Artillery `CoreRuleCatalog.cs:~115-125` (Subject part gated all-models at :122). Immobile :2280.
  Action gating sites in `ChooseActionStage.cs` (:656 charge, :708 Pass, :825 shoot-before-move, :849
  move-and-shoot cap - the Aircraft pattern to mirror for Cumbersome).
- Tests: `IndirectLineOfSightRuleIntegrationTests.cs`, `HitRollRuleIntegrationTests.cs`
  (IndirectAttacker_AfterMoving_RaisesHitThreshold), `SpecialRuleTests.cs:598`,
  `QuickReadjustmentRuleIntegrationTests.cs`, `WeaponScopedCounterAndSightTests.cs`.

## Notes

- 2026-10-29: filed with the plan.

## Decisions

(none yet)

## Outcome

(open)

## Rules for the agent (plan section 1, restated)

- Branch `feature/opr-3.6` in BOTH repos; check `git branch --show-current` in each before committing.
  Never commit, merge or push `master`. Submodule-first commit cadence; subjects start with `NNN:`.
- 3.6 rule text: `~/Projects/GDF Armies/Rules 3.6.0/` (core PDFs' text extracts, common-rules JSON) and the
  3.6.0 book snapshots. Never commit anything from that folder. No 3.5 fallbacks or version flags.
- Vertical slice: engine change + CLI resolver + GUI resolver/overlay + AI resolver legality + an
  integration test mirroring the nearest `*RuleIntegrationTests` + the `docs/` line if a number changed.
- Verify per CLAUDE.md (engine suite green, full build, headless smoke exit 0) and
  `FdgRaylib --rule-coverage FdgRaylib/Assets/Books` not regressing. Never commit red.
- Keep this file's Notes/Decisions current and flip the row in `docs/opr-3.6-upgrade-plan.md` section 5.
  A design fork stops for Chris's sign-off with options + a recommendation written here.
