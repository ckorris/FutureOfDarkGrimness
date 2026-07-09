# 192 — Structured game result

**Goal:** replace the string-only end-of-game signal with a structured result. Engine:
`GameResult` record — `EGameOutcome { Win, Tie, Fault }`, `PlayerID? Winner`, per-player final
scores, rounds played — built by `VictoryCalculationStage`, surfaced as
`FDGServer.OnGameCompleted : Action<GameResult>`. The existing `OnGameEnded(string)` stays.
Headless CLI prints one structured summary line.

**Why:** Tactician prerequisite P1 (`docs/ai-agent-plan.md` sec. 7) — benchmarks, self-play
training data, and search rollout rewards all need a machine-readable winner + score margin.

**Verify:** integration test mirroring the nearest `*RuleIntegrationTests` — winner matches the
objective tally, including tie and zero-objective cases; headless smoke exits 0 with the line.

## Notes (newest first)

**2026-07-09 — filed** (Tactician prerequisite, plan sec. 7 P1).

## Outcome

(open)
