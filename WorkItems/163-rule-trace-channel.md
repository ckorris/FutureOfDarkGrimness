# 163 — Rule-trace log channel

**Status**: done
**Related**: #166 (fire-lint, the static twin), #168 (RuleDiagnostics UI surfacing, still open), SpecialRulesAudit.md T2

## Goal
A verbosity toggle that narrates every live rule hook evaluation — fired / condition failed /
suppressed / ability offered-or-refused — so manual rule testing becomes "did the expected line
appear" instead of black-box number-squinting. The audit's best-leverage manual-testing tool.

## Notes

- 2026-07-08: Shipped. Engine: static `RuleTrace.Enabled` gate (`Rules/Dispatch/RuleTrace.cs`,
  mirrors the `RuleDiagnostics` pattern); `RuleEvaluator` emits `trace:`-prefixed lines through the
  EXISTING Debug log channel (`ITextOutput.LogDebug` -> isDebug relay -> GUI Debug view / headless
  `[LOG]`). Trace points: hook header with participants+seats+weapons (`CollectSurviving`), per-entry
  condition pass/fail with the condition description, fired ops by type, condition-passed-but-no-ops,
  dedup skips, suppression victims naming the suppressor, and `GatherOffers` offered / not-offered
  (availability vs cost). App: `--trace-rules` flag (Program.cs); the GUI console's Debug toggle
  flips the same switch at runtime (assign-on-change so a flag launch isn't clobbered).
  Tests: `Tests/RuleTraceTests.cs` (6) — incl. the two silence guarantees below. Verified: engine
  1288/1288, full build clean, headless smoke with `--trace-rules` exit 0 with 224 trace lines
  (headers, Scout/Ambush DeferDeployment, Takedown, Embark/Disembark offer decisions), smoke without
  the flag emits zero trace lines.

## Decisions

- **Rides the existing Debug log channel** rather than a new event: `ITextOutput.LogDebug` ->
  `IPlayerTextRelayer(isDebug)` -> GUI Debug view (hidden by default) / CLI `[LOG]` was already
  plumbed end-to-end, so the trace needed zero new transport and networked clients get host traces
  for free.
- **Process-local static gate** (`RuleTrace.Enabled`, `RuleDiagnostics` precedent), not GameSettings:
  it is a developer tool, and evaluations run host-side in networked games — only the host's toggle
  generates; clients' Debug toggles control their local view of the relayed lines.
- **Silence guarantees**: read-only `log:false` query paths (per-frame `EvaluateAllNamed` UI queries)
  never trace even when enabled, and the grant-consumption re-walk traces nothing (it re-visits the
  same hook). Both pinned by tests.
- Only rules with an entry at the firing hook+seat are narrated — tracing every rule past every hook
  would drown the signal in non-events.

## Outcome
Done 2026-07-08, together with #166a (the fire-lint) as the two halves of killing the silent-no-op
rule class: the lint proves a rule CAN fire, the trace shows whether it DID. Engine commit + app
commit (`--trace-rules`, GUI toggle wiring, CLAUDE.md flag doc). No deferred facets; the related
app-side surfacing of load-time diagnostics remains #168.
