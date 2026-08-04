# 330 — End a match once the result can no longer change

**Status**: in-progress (implemented + tested; awaiting GUI hand-verify)
**Related**: #257 (team victory scoring), #195 (authoritative round number in `ReconcileObjectivesStage`), #040 (game-over card), #191 (benchmark harness reads `RoundsPlayed`)

## Goal
When a side has been tabled and the survivor already leads on objectives, the match ends at that
end-of-round instead of grinding through the remaining rounds with only one player acting. Reported
by a playtester who held every objective and had tabled their opponent at the end of round 3, then
had to hold every move through round 4 to reach a foregone conclusion.

Done looks like: `ReconcileObjectivesStage` takes its existing `ToVictoryCalculation` edge early when
the result is provably fixed; a Headline banner plus a log line make it clear to *both* players why
the game stopped short; and the check has no false positives — it must never end a match that could
still change.

## Notes

- 2026-08-04: Implemented. `MatchDecision.IsResultFixed` (new, beside the stage) + the early exit in
  `ReconcileObjectivesStage`, which now returns early on the round limit so the decided check reads as
  its own branch. 18 new tests in `MatchDecisionTests` — 15 on the pure helper, 3 driving the real stage
  through a recording `IStateMachineLayer` (early exit fires, still-live game starts the next round as
  before, round-limit ending unchanged and not mislabelled as an early call). Engine suite 2679/2679,
  full `dotnet build` clean, headless smoke exits 0 with the round-limit path untouched.
  **Mutation-verified**, since the tests only earn their keep if they fail on the two mistakes that
  matter: skipping reserve units in the living scan fails both off-table tests, and relaxing the sole-lead
  check from `>=` to `>` fails the tied and 0-0 tests. 4 failures, exactly the relevant ones.
  End-to-end via `Scenarios/330-match-already-decided.json` (new): round 3, opponent pre-killed through
  `woundsDealt`, and the run ends there - `rounds=3`, round 4 never played, both the log line and the
  banner text present. Note the compiler rejects a scenario whose active player has nothing to do, so one
  distant unit is left unactivated.
- 2026-08-04: Filed. Design settled with the user before implementation (see Decisions).

## Decisions

**The decidability rule, and why it is exact rather than heuristic.** Two properties of the current
rules make this safe without any reachability analysis:

1. Objective ownership is **sticky**. `ITeamExtensions.ReconcileObjectiveOwner` returns the current
   owner when nobody is in range, and `ReconcileObjectivesStage` does not even call `SetOwner` in
   that case. A marker you hold stays yours until an enemy physically gets within 3".
2. Therefore a side with no living models has a **non-increasing** score (it can never seize or
   contest again), and the surviving side's score is **non-decreasing**.

So: if every side but at most one has no living models, and the survivor is already the sole leader,
no legal sequence of remaining events can change the winner. If *all* sides are dead the state is
frozen entirely, so the current result (win or tie) is final. Anything beyond this — "they are 3-1 up
and the enemy cannot physically reach two markers" — needs movement budgets, terrain, Ambush arrival
anywhere on a board edge, transports and Aircraft, which is exactly where false positives would come
from. Deliberately not built.

**"Tabled" means no living units anywhere, not no models on the table.** This is the whole
false-positive surface, and there are four ways to get it wrong — all of which end a match that is
still live:

- **Reserves.** An Ambush unit held back is alive and off-table (`ReserveRules.IsInReserve`).
- **Reinforcement.** `GameOperationServices.ReinforceUnit` creates a *full-strength copy* of a
  destroyed unit, alive, in reserve, carrying `PendingReinforcementArrival`. A player can look
  tabled and have an army arriving next round.
- **Embarked** units inside a transport.
- **Aircraft** that flew off the edge (`OffTableFromForcedMove`).

Reading `unit.GetIsAlive()` across the whole `ArmyData.UnitBindings` and never consulting
`GetIsOnBattlefield()` handles all four at once.

**Presentation: banner + log, no wire change (user's call, 2026-08-04).** The Game Over card already
has a dimmed note slot (`RaylibRenderer.DrawGameOverOverlay`), but it is fed from `OnGameCompleted`,
which is host-side only and deliberately never crosses the wire — so a note fed that way would be
invisible to exactly the networked client who most needs the explanation. Options weighed were
(A) structured `EndReason` on `GameResult` + a widened `GameEndedMessage` so clients get the note,
(B) folding the reason into `GameResult.Message`, and (C) banner + log only.

The user chose **C plus an extra banner beat**. It turns out to need no plumbing at all:
`IGameContextAccessor.Announce` writes the log line *and* presents the banner in one call, and both
channels already replicate — `PresentationRelayer` fans beats to remote players as
`PresentBeatMessage`, and `PlayerLogSender` pushes log lines over `LogChatNetworkMessage`. So
`GameResult`, `GameEndedMessage`, `ILobbyViewModel` and all client code stay untouched. Headline is
also the documented "allowed to stop the game" tier. Accepted tradeoff: the Game Over card itself
stays bare, so a player who clicks past the banner has only the log line as a durable record.

**Not deferred, just out of scope:** the Aircraft-only refinement. A side whose only survivors are
Aircraft also has a permanently non-increasing score (Aircraft can neither seize nor contest, per
`ReconcileObjectivesStage`), so it could be treated as inert too. Sound, but exotic enough that it
adds a rules-coupling risk for no practical gain; left out on purpose.

## Outcome
