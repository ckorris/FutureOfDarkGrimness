# 422 — Impact before either unit attacks + Fatigue gate; Counter = strike-first only; new Brace(X)

**Status**: todo
**Related**: #415, #030 (Impact/Counter), #184 (Counter strike sequencing), #355 (Impact unreachable on vehicles - 3.6 replaces vehicle Impact with Overrun weapons, so #355 may close), #435 (Brace in Melee), #442
**Branch**: `feature/opr-3.6`. Model/effort: Opus 5.5 / high.

## Goal

Core Rules v3.6.0:
- Impact(X): "When charging, before either unit attacks, roll X dice, unless this unit is Fatigued. For each
  2+ the target takes 1 hit."
- Counter: "May strike first with this weapon when charged, unless this unit is Fatigued."
- Brace(X) (new): "Enemies charging this unit get -X total Impact rolls, unless this unit is Fatigued."
- Book grants: "Counter in Melee" = "This model gets Counter on its weapons in melee"; "Brace in Melee" (AoF)
  = "This model gets Brace(1) on its weapons in melee"; plus Aura/Buff variants of both.

In 3.5.1 Counter carried both facets (strike first + "-1 total Impact rolls per model with Counter") and
Impact was rolled "when attacking after charging". "Done" = Impact hits resolve (hit -> save -> wounds) before
any strike, including before Counter strikes; a Fatigued charger rolls no Impact; Counter only reorders the
strike (and only when the countering unit is not Fatigued); Brace(X) reduces the charger's Impact dice by X
total per Brace rule instance on the defending side (sum over models carrying it, floor 0), not when the
Brace unit is Fatigued; Brace is a unit-or-weapon-scoped rule (AoF puts Brace(1) on weapons via "Brace in
Melee"), so the catalog entry must accept both attachment scopes like Counter does.

## What changes

1. Engine: `ImpactSink` / Impact catalog entry: move the hook to a pre-strike point in the melee stage
   (before `PreAttackStage` resolves Counter ordering); gate on not-Fatigued. Counter: delete the
   `ReduceImpactDicePerModel` facet. New `CoreRuleCatalog.Brace` with the reduction, Subject seat, Fatigue
   gate. Supplement: `Counter in Melee`, `Counter in Melee Aura/Buff`, `Brace in Melee` (+Aura) as grants.
   Remove `CounterAttack` (3.5-only, dropped by 3.6) - coordinate with #436.
2. Tests: Impact resolves before Counter strikes and its wounds count for #419; Fatigued charger = 0 Impact
   dice; Brace(2) on a 3-model unit vs Impact(6) = 4 dice (or confirm "total" means per rule instance - read
   the AoF Brace in Melee text: "Brace(1) on its weapons", i.e. per model-weapon, so total = sum); Fatigued
   Brace unit = no reduction; Counter unaffected by Impact.
3. Resolvers/presentation: the impact dice beat plays before the strike beats; the melee panel shows Brace on
   weapon rows (#336 structured rules); AI expected-damage (#442) told.

## Acceptance

Tests green; a hand-verified charge shows Impact dice first, then Counter strikes, then charger strikes.

## Code anchors (survey 2026-10-29)

- `Rules/Dispatch/ImpactSink.cs`, `CoreRuleCatalog.Impact` / `.Counter` / `.CounterAttack`, `PreAttackStage`,
  melee strike ordering in `MeleeStage`. See the appended survey rows below for exact lines.

### Survey rows (code survey 2026-10-29)

Findings that shape the item: Impact ALREADY resolves before the Counter swap and all strikes (pipeline
`MeleeStage.cs:53-58, :81-133`: ChooseMeleeDefender -> ResolveImpactHits -> DetermineStrikeOrder -> ResolveRavage
-> PileIn -> in-range sets -> swings -> DetermineMeleeWinner -> morale -> ApplyFatigue -> Consolidate). What is
left: the Impact Fatigue gate, deleting Counter's impact facet, adding Brace(X), and re-authoring AoF's
"Counter in Melee" which today carries `reduceImpactDicePerModel`.

- Impact: `CoreRuleCatalog.cs:557-567` (doc :555 says the not-fatigued gate is deferred); `Effect.cs:961-967`;
  `Rules/Dispatch/ImpactSink.cs` (AddDice :13, TotalDice :47, AP max :21); `ResolveImpactHitsStage.cs` (:23 no
  gate, :50-56 seats, :69-70 roll, :93 synthetic AP-0 weapon); AI `CombatMath.cs:~240`.
- Counter: `CoreRuleCatalog.cs:599-616` (:602-606 StrikeFirst on `Melee_OnCounterTrigger`; :607-611
  `ReduceImpactDicePerModel` on `Melee_OnChargeContact`; doc :590-598); Counter-Attack :624-635 (dropped by 3.6);
  `Effect.cs:979-993` `ReduceImpactDicePerModel`; `StrikeFirst` `Effect.cs:1013-1019`, `RuleOperation.cs:825-833`;
  swap `DetermineStrikeOrderStage.cs:42-66`; AI `CombatMath.cs:244-253`.
- Data: `AofRuleSupplement.json:2332` "Counter in Melee" (uses `reduceImpactDicePerModel` at :2351). Brace: absent.
- Tests: `CounterRuleIntegrationTests.cs`, `ImpactRuleIntegrationTests.cs` (CounterDefender_ReducesImpactDice_*),
  `WeaponScopedCounterAndSightTests.cs`, `FatigueTests.cs` (CounterSwap_*), `ImpactOnlyChargeRuleIntegrationTests.cs`.

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
