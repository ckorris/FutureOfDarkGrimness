# 447 — Impact(X) is rolled per unit; the rule is per model

**Status**: todo (filed 2026-10-29 from #422's D8)
**Related**: #422 (Impact Fatigue gate, Brace; landed), #355 (Impact unreachable on vehicles - 3.6 replaced vehicle Impact with Overrun weapons), #442 (AI parity), #438 (re-bake brings the 3.6 Impact carriers)
**Branch**: `feature/opr-3.6`. Model/effort: Opus 5.5 / medium. Schedule in phase 2, after #422.

## Goal

Core Rules v3.6.0, Impact(X): "When charging, before either unit attacks, roll X dice, unless this unit is
Fatigued. For each 2+ the target takes 1 hit." Like every "this model" rule, Impact is carried per model, so a
unit of 5 models with Impact(1) rolls 5 dice. The engine's `ImpactSink` pools the rule once per unit, so that
unit rolls 1 die. #422's census of the 3.6.0 snapshots found 42 multi-model units carrying Impact (e.g. Orcs
Iron Tusk-Beast Riders [5] Impact(1)), all under-rolling today. Pre-existing bug, not a 3.6 change.

"Done" = Impact dice = sum over living, charging models that carry the rule of their X (a joined hero with
Impact(3) in a unit without it adds 3; a unit-scoped Impact granted by an aura counts every model); Brace(X)
(#422) still subtracts from that total; Heavy Impact and the Impact-granting supplement rules follow;
`CombatMath` and the Combat Calculator mirror it; the impact dice beat shows the real count.

## What changes

1. Engine: `Rules/Dispatch/ImpactSink.cs` / `Effect.ChargeImpactHits` evaluation per carrying model (the
   `RuleInvocation.Attachment` field #422 added distinguishes per-model copies from unit-wide grants), summed in
   `ResolveImpactHitsStage`; keep the AP-max fold.
2. Tests: 5 models Impact(1) -> 5 dice; 3 models of which 2 carry Impact(2) -> 4 dice; hero Impact(3) joined to a
   5-model Impact(1) unit -> 8; casualties before the charge reduce it; Brace subtracts from the sum; Fatigued
   charger still 0; a `CombatMathPinTests` case.
3. Presentation: dice beat count; log line "X's Impact rolled N dice".

## Code anchors

- `Rules/Dispatch/ImpactSink.cs` (AddDice :13, TotalDice :47), `CoreRuleCatalog.Impact` (~:557), `Effect.cs`
  `ChargeImpactHits` (~:961), `MeleeStage/ResolveImpactHitsStage.cs` (:50-56 seats, :69-70 roll),
  `Ai/Tactician/CombatMath.cs` (~:240), `Tests/ImpactRuleIntegrationTests.cs`, `BraceRuleIntegrationTests.cs`.

## Notes

- 2026-10-29: filed. See `WorkItems/422-impact-counter-brace.md` D8.

## Decisions

(none yet)

## Outcome

(open)

## Rules for the agent (plan section 1, restated)

- Branch `feature/opr-3.6` in BOTH repos; never commit, merge or push `master`. Submodule-first cadence;
  subjects start with `447:`. 3.6 text from `~/Projects/GDF Armies/Rules 3.6.0/` only; no 3.5 fallbacks.
- Vertical slice (engine + presentation + AI parity + integration test); verify per CLAUDE.md plus
  `--rule-coverage` non-regression; never commit red; keep this file and the plan's status table current.
