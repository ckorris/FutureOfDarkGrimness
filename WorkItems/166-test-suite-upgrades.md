# 166 — Test-suite upgrades umbrella

**Status**: in-progress
**Related**: SpecialRulesAudit.md (sections 3.2/4, Phase 3), #163 (rule-trace channel), #164, #175

## Goal
The strategic test-suite upgrades from the 2026-07-06 special-rules audit: (a) catalog-wide "every
rule fires" lint, (b) `RuleInteractionTests` for the ~10 real rule pairings, (c) `SaveLoadRoundTrip`
harness helper folded into existing rule tests, (d) probabilistic-dice variants of key rule tests,
(e) one wire-crossing rule request test, (f) real Tough in `ToughWoundOrderingRuleIntegrationTests`.
Done when each facet has landed (or is explicitly re-filed) and the suite catches the silent-no-op
rule class automatically.

## Notes

- 2026-07-08: **Facet (a) shipped — the fire-lint.** Engine: `Rules/Dispatch/RuleFireLint.cs`
  (public, beside `RuleValidator`, so both test projects share it), `Tests/RuleCatalogLintTests.cs`
  (one case per `CoreRuleCatalog.All` rule + the standalone Disembark/Embark = 114 rules),
  `Tests/RuleFireLintSelfTests.cs` (8 negative tests pinning each detection class, incl. a
  reconstructed Breath-Attack analog: `Effect.Reactivate` offered at pre-attack -> "silently
  dropped"). App: `FdgRaylib.Tests/RuleSupplementLintTests.cs` lints all 14 `GdfRuleSupplement.json`
  definitions. Allowlists (= the documented not-covered ledger, stale entries fail): engine — Hero,
  Transport, Limited (engine-markers), Disembark, Embark (stage-enacted no-op effects); app — Unique
  (ListValidator build-time marker). Verified: engine 1282/1282, `dotnet build` clean,
  FdgRaylib.Tests 120/120, headless smoke exit 0.

## Decisions

- **Passive entries are checked by DIRECT invocation** (build hook-context variants, evaluate the
  entry's Condition, apply its Effect, require >=1 op) rather than through `RuleEvaluator` dispatch:
  dispatch is generic machinery already covered by the integration suite, and going through it blurs
  attribution when condition-satisfier helper rules share a hook. Activated abilities DO go through
  the real `GatherOffers`.
- **Ability ops are checked against a hand-maintained handled-op map** (`IsOpHandledAtAbilityHook`):
  every offering stage runs OperationApplier + OperationExecutor; `InvokeDealHits` only on
  pre-attack/strafing, `InvokeReactivate` only on next-activator. Drift direction is a loud false
  FAILURE (stage learns an op, map not updated), never a silent false pass.
- **Composition gates are satisfied from the condition tree** (UnitHasRule/TargetHasRule -> stub
  attachment by name, TokenPresent -> seeded tokens), positive-polarity leaves only; capability
  gates (distance/melee/moved/faces/action type) are covered by context variants instead.
- `CapabilityCondition` THROWS on a missing capability (doesn't return false); the lint reports the
  throw alongside the RuleValidator capability violation.

## Deferred (recorded, not silently cut)

- **Passive-op consumption wiring**: the lint proves a passive entry *produces* ops, not that the
  stages at that hook *consume* them (the sink/query side). Needs a per-hook consumed-op map like
  the ability one — worth doing after #163's trace channel makes the wiring observable.
- Supplement **spells** (`SpellDefinition`) are not linted — different authoring shape; the cast
  path pattern-matches effects directly.
- Facets (b)-(f) of this umbrella remain open.
