# 195 — Resumed games play four MORE rounds instead of finishing the four-round game

**Symptom.** A game resumed from a save/scenario at round N plays rounds N..N+3, not N..4. A scenario
resumed at round 2 plays rounds 2,3,4,5 and `GameResult.RoundsPlayed` reports **5** (fresh games
report 4). The end-of-round log also mislabels the round ("Reconciling objectives (end of round 1)"
while the game is on round 2), and the final banner still claims "4 rounds complete".

**Root cause.** `ReconcileObjectivesStage._timesEntered` (`StateMachine/MainPhaseRoundStage/
ReconcileObjectivesStage/ReconcileObjectivesStage.cs:18,28,75`) is a per-stage-instance counter that
decides when to stop:

```csharp
if (_timesEntered < GameWideConstants.NUMBER_OF_ROUNDS) -> next round
else -> victory calculation
```

On resume the state machine builds a fresh stage instance, so the counter restarts at 0 and is never
seeded from the resumed `GameProgressData.RoundCount` / `IMainPhaseContext.RoundCount` (which IS
restored correctly, hence the 5). It should terminate on the real round number, not on how many times
this instance happened to be entered.

**Impact.** Any resumed game is a round too long (and a game resumed at round 4 would play 4..7).
Objectives are reconciled an extra time, so end-of-game scoring can differ from a fresh game's.
Directly hurts #191: scenario-based strategy probes and (later) search rollouts resumed from snapshots
would silently play the wrong number of rounds and score the wrong board.

**How found.** #193's determinism tests asserted "a completed game reached round 4"; the resume-based
fixture reported 5. `ScenarioCompilerTests`'s own comment says the resumed scenario should play
"rounds 2..4", so the intent is clear and this is a real regression, not a design choice.

**Fix sketch (not yet done).** Terminate on `context.RoundCount >= GameWideConstants.NUMBER_OF_ROUNDS`
rather than an instance counter, or seed `_timesEntered` from the resumed round. Prefer reading the
authoritative round number — the instance counter is the bug. Keep the log line's round number honest
(it should print the game round, not the entry count). Behavior change, so: sign-off before building.

**Verify.** A scenario compiled at round 2 ends after round 4 with `rounds=4`; a fresh game still ends
at 4; `DeterminismTests` resume fixture can then assert `RoundsPlayed == NUMBER_OF_ROUNDS` (drop the
`expectedRounds: null` exemption + its #195 comment); add a test resuming at round 4 that plays exactly
one more round.

## Notes (newest first)

**2026-07-09 — filed** while implementing #193 (determinism pass). Not fixed there: it changes game
behavior and is unrelated to seeding. Relates #052 (save/load), #167 (scenario compiler), #192
(`RoundsPlayed` surfaced it), #191 (probes/rollouts depend on correct round counts).

## Outcome

(open)
