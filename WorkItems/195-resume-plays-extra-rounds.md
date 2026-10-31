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

## Notes (newest first)

**2026-07-09 — fixed** (Chris signed off). `_timesEntered` deleted; the stage now terminates on the
authoritative round number. Ordering detail that makes it work: `SingleRoundStage` advances
`MainPhaseContext.RoundCount` in `ReconcileChildContextBeforeLeaving` (`OnEndOfRound`) on its way out,
and `ReconcileObjectivesStage` is its sibling successor — so on entry the round that just finished is
`RoundCount - 1`. That value drives both the log line and the end-of-game test, and it is restored from
the save, so resume behaves exactly like a fresh game. Verified against the pre-fix code: the new tests
report 5 rounds (resume at 2) and 7 rounds (resume at 4) before the change.

**2026-07-09 — filed** while implementing #193 (determinism pass). Not fixed there: it changes game
behavior and is unrelated to seeding. Relates #052 (save/load), #167 (scenario compiler), #192
(`RoundsPlayed` surfaced it), #191 (probes/rollouts depend on correct round counts).

## Outcome

**Done 2026-07-09.** Engine `a19e6ab`. One-line semantic fix in `ReconcileObjectivesStage`: terminate
on `roundJustFinished < NUMBER_OF_ROUNDS` (where `roundJustFinished = context.RoundCount - 1`) instead
of an entry counter, and log the real round number.

New `ResumeRoundCountTests` (3): resume at round 2 and at round 4 both finish the four-round game;
resume at round 1 is unchanged. **Mutation-verified** — reverting the stage makes the first two fail
with 5 and 7 rounds. Round-1 resume passes either way, which is precisely why this survived: the only
shipped example scenario (`Scenarios/example-shootout.json`) resumes at round 1.

`DeterminismTests` resume fixture tightened to assert the full `NUMBER_OF_ROUNDS` (the `#195` exemption
is gone). Suite **1370/1370**, full build clean. End-to-end: a scenario resumed at round 3 headless now
logs "end of round 3", "end of round 4", "4 rounds complete", `rounds=4`, exit 0; a fresh game still
logs rounds 1-4 identically to before.

Scoring note: objectives are now reconciled exactly four times per game regardless of resume point.
Previously a resumed game reconciled extra times, so end-of-game ownership could differ from a fresh
game's - resumed saves played before this fix are not comparable to fresh ones.
