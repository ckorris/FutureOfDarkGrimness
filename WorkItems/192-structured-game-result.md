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

**2026-07-09 — implemented.** Engine `9b1c0ba`. `GameModel/GameResult.cs`: `EGameOutcome
{ Win, Tie, Fault }` + `GameResult` record (outcome, winner, winner name, per-slot scores, rounds
played, message) + `ToSummaryLine()`. `IGameContext.NotifyGameEnded(string)` had exactly one caller
(`VictoryCalculationStage`), so it was **replaced** by `NotifyGameCompleted(GameResult)` rather than
left as dead vocabulary — `GameResult.Message` carries the string forward. `FDGServer` gained
`OnGameCompleted` and a single `RaiseGameCompleted` fan-out point used by victory, disconnect, and
engine-fault paths, so the structured and legacy events can never disagree or fire without each other
(structured raised first: `OnGameEnded` subscribers tear the game down). Scores/rounds read from the
live `TableState.Progress` read model, so the result agrees with the last scoreboard the player saw.

Design notes: `WinnerName` added beyond the spec (already computed for the banner; useful for reports).
Fault carries no scores by construction. Nothing crosses the wire — `GameEndedMessage` still carries
only the string, so no wire-format change (would have been a stop-and-ask).

**2026-07-09 — filed** (Tactician prerequisite, plan sec. 7 P1).

## Outcome

**Done 2026-07-09.** Engine `9b1c0ba`, superproject bump + CLI line in the following commit.

Verified: engine suite **1349/1349 green**; full `dotnet build` clean. Headless smoke exits 0 and
prints `Game result: outcome=Tie winner=none rounds=4 scores=[0, 0]` immediately before the legacy
`Game ended: It's a tie!` — confirmed correct against the log (all 4 objectives stayed neutral all
game; the solo-rules bot never contests, which is exactly the gap #191 exists to close). The Win path
was exercised end-to-end too, via `--headless --scenario Scenarios/example-shootout.json`:
`Game result: outcome=Win winner="Player 1" rounds=4 scores=[1, 0]`, matching `Player 1 wins!`. Both
CLI subscription sites (`RunAsync`, `RunScenarioAsync`) therefore ran for real.

Tests: `VictoryCalculationStageTests` keeps its five pre-existing message pins and adds six structured
facets — scores in slot order, winner == top of the result's own score tally, rounds-played from
progress (and 0 without a progress record), a stable ASCII summary line, and the Fault factory.

No GUI hand-verification needed: the GUI and network consume `OnGameEnded(string)`, whose text and
firing order are unchanged.
