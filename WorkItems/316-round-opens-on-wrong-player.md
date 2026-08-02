# 316 — Every round opened with the wrong player

**Status**: in-progress (implemented + tested + headless-verified; awaiting confirmation in a live multiplayer game)
**Related**: #052 (save/load rolling snapshot), #167 (scenario compiler), #197 P19 (ActivatesNext cursor override)

## Goal

"Each round, players alternate in activating one unit each, starting with the player that won the
deployment roll-off. Each new round, the player that finished activating first on the last round gets to
go first." Reported from a 2026-08-02 multiplayer game as "often, one of us would go first on a new round
when the other should have". Done means the round's OPENING activation goes to the head of the activation
order - the roll-off winner in round 1, the previous round's first-finisher thereafter - in fresh games,
resumed saves, and multi-player teams.

## Notes

- 2026-08-02: Not "often" - in a two-player game the wrong player opened **every** round, including
  round 1. The reporter saw it as intermittent, most likely because the alternation after the opening
  pick was correct and a #197 ActivatesNext override or a player with nothing left to activate can
  incidentally restore the expected order.

- 2026-08-02: Root cause is a single missing first-turn guard, not a broken handoff. The
  order-carrying half was already right: `MainPhaseContext` seeds round 1 from
  `FirstDeploymentRollOrder` and thereafter takes `SingleRoundContext.CurrentRoundTeamFinishOrder`, so
  index 0 of `TeamActivateOrder` is always the team that should open. But every round builds a fresh
  `TeamPlayerAlternationCursor` at index 0, and `TryAdvance` means "move PAST the current position"
  (`firstTeamToCheck = (CurrentTeamIndex + 1) % teamCount`). `DeterminePlayerTurnStage` advances before
  the round's first activation, so the opening pick landed on index 1 - the team that should have gone
  second. Same off-by-one within a team: a two-player team opened with its second-listed player.

- 2026-08-02: Fixed by parking the round's cursor one step short of the head of the order at
  construction (`TeamPlayerAlternationCursor.ParkBeforeFirstTurn`, called from `SingleRoundContext`'s
  fresh constructor only). Engine suite 2593/2593; `dotnet build` clean; headless smoke exits 0 and the
  log now shows the roll-off winner both deploying and activating first, with each subsequent round led
  by the previous round's first-finisher.

## Decisions

- **Why the cursor's own `TryAdvance` semantics were left alone.** Three call sites
  (`PlaceTerrainStage` x2, `TerrainPointsLedger`) READ the current position and then advance, so for
  them a fresh cursor pointing AT the first turn is correct; two others
  (`DetermineNextDeployPlayerStage`, `DetermineNextObjectivePlacerStage`) advance before every turn and
  hand-roll a `HasStarted` guard to skip the first advance. Making a fresh cursor mean "not yet started"
  globally would have fixed the round loop but broken the read-then-advance three, so six call sites
  would have had to move for a bug in one. Parking is opt-in instead: the two idioms are now documented
  together on the cursor, and only the round opts into the second one.

- **Why parking is a cursor POSITION and not a `HasStarted` flag.** The #052 rolling snapshot is written
  at the TOP of `DeterminePlayerTurnStage`, i.e. before the round's first advance, so "nobody has
  activated yet" is a state that has to survive save/load - otherwise a game saved at the start of a
  round resumes on the wrong player, which is the same bug on the resume path. A flag would have meant a
  new `GameProgressData` field (and old saves defaulting it wrongly); parking at the end of the order,
  which `TryAdvance` wraps off, round-trips through the existing `CurrentTeamIndex` int for free and
  keeps old saves reading exactly as before. `ScenarioCompiler.WriteProgress` already hand-rolled the
  same parking trick to seed a scenario mid-round - the fix just gives it a name and a second user.

- **Two existing tests were asserting the buggy order incidentally.**
  `ActivateUnitNextRuleIntegrationTests`' two-team fixture put the player's own team at the head, so once
  the round opened correctly an honoured ActivatesNext flag and an ordinary advance both landed on the
  same player and the assertions held either way. The fixture now leads with the ENEMY team so the
  override stays distinguishable from the alternation. `GameProgressTests` pinned a fresh round's
  captured `CurrentTeamIndex` as 0; it is now the parked value, which is the assertion that actually
  guards the resume path.

- **Deferred: nothing.** The GUI needs no change - it renders whoever the engine names as active.

## Outcome

Pending: written when the reporter confirms correct opening order in a live multiplayer game.
