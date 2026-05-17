# 001 — D3+2 objective placement (alternating, interactive)

**Status**: in-progress
**Related**: branch `ObjectivePlacement`

## Goal

Replace `PlaceObjectivesStage`'s grid-RNG auto-placer with the interactive
alternating-placement procedure described on page 6 of the GF Beginner's
Guide v3.5.1: each team takes turns placing one marker until D3+2 markers
are on the table, subject to all of:

- outside both deployment zones (12" from each long table edge),
- >9" away from every previously-placed marker,
- not inside impassable terrain,
- not in a position too tight to reach (no model could legally stand within
  3" of it).

The current auto-placer is retained behind a debug toggle so headless /
automated play continues to work without prompting.

Done = a human (CLI or GUI) places objectives one at a time on both the
host and any clients, in alternating team order, with full validation and
the existing seizure / 4-round / victory pipeline still passing.

## Rules summary (page 6, verbatim where relevant)

> "After the table has been prepared, you and your opponent must set up
> D3+2 objective markers on the battlefield. The players roll-off and the
> winner picks who places the first objective marker. Then the players
> alternate in placing one marker each outside of the deployment zones,
> and over 9" away from other markers (note that markers can't be placed
> in unreachable positions, like impassable terrain or spots too tight
> to get to)."

Mission rule (also page 6):

> "At the end of each round, if a unit is within 3" of a marker whilst no
> enemies are, then it counts as being seized. Markers stay under the
> player's control even if the unit moves away, but if units from both
> sides contest a marker at the end of a round, then it becomes neutral
> again. After 4 rounds have been played, the game ends, and the player
> that controls most markers wins."

Battlefield: 6'×4' (72"×48"), deployment "fully within 12" of [chosen]
table edge" — so the legal objective band is the middle 24" between the
long edges.

## Current gap analysis (2026-05-17)

Already correct:
- `RollForObjectiveCountStage` — D3+2 via d6 (1→1, 2-3→2, 4-6→3) plus 2.
- `RollForFirstObjectivePlacementStage` — team-level roll-off picks who
  starts.
- `ReconcileObjectivesStage` — 3" seizure, contest→neutral, sticky
  ownership, 4-round termination → `VictoryCalculationStage`.

Wrong / missing:
- `PlaceObjectivesStage` does the entire placement itself with a shuffled
  grid; no player ever places anything. Has to become an alternating
  driver that emits one `PlaceObjectiveRequest` per turn until the count
  is reached.
- No `PlaceObjectiveRequest` / resolver exists yet. Need CLI + GUI
  resolvers parallel to the existing `PlaceObjectsRequest<T>` resolvers,
  but for a single point in the legal band (not for placing models inside
  a deployment zone).
- `DEPLOYMENT_DISTANCE_INCHES` is 9 in `GameWideConstants`. Rules say 12.
  The auto-placer uses 9, so its "outside the deployment zone" band is
  6" too wide. **Don't just bump the constant** — it's also referenced
  by deployment code, and changing it shifts where units deploy. Treat
  as a separate flag/decision (see Decisions).
- The "too tight to reach" constraint isn't checked. Cheap proxy:
  reject candidates where a circle of radius (smallest_base + small
  buffer) at the click position would overlap impassable terrain or
  fall outside the table — i.e., no model could legally end its move
  with base center within 3" of the marker.
- No debug toggle / setting; the auto-placer is currently the only
  behavior, so we need a settings hook (lobby option? CLI flag?) before
  we can hide it behind one.

Multi-player-per-team: `TeamData.Players` is a list. Rules are written
1v1. Decision needed before implementation — see Decisions.

AI players: `EPlayerType.AI` exists in the enum but every switch on it
throws `NotImplementedException` (e.g. `LobbyViewModel_Host.cs:289, :402`).
An unmerged `origin/ComputerPlayer` branch may contain related work —
check before duplicating effort. Within scope of 001 we only need an
AI resolver for `PlaceObjectiveRequest`; sentience elsewhere is out
of scope.

## Subtasks

1. Add `PlaceObjectiveRequest` (request + result types) in the engine.
   Result is a single `Position` plus a "this is unreachable, retry"
   sentinel for clients that picked an invalid spot due to stale state.
2. Validation helper `ObjectivePlacementValidator` covering:
   - inside legal band (z ∈ [12, tableH-12] in current orientation),
   - >9" from each already-placed objective,
   - outside impassable terrain footprints,
   - reachability check (see "too tight" above).
   Pure function so it can be reused for the auto-placer and for resolver
   client-side previews.
3. Rewrite `PlaceObjectivesStage` as a loop that:
   - tracks starting team from the roll-off,
   - resolves players in team-round-robin order (see Decisions),
   - emits `PlaceObjectiveRequest`, validates the result, on failure
     re-prompts the same player,
   - creates the `ObjectiveData` on success, advances the turn,
   - exits when D3+2 markers exist.
4. CLI resolver: print legal band + existing markers, parse "x,z" coords.
   EOF → fall back to auto-placement for that marker (preserves the
   piped-stdin headless run).
5. GUI resolver (`GuiPlaceObjectiveResolver : IGuiResolver, IGuiCanvasOverlay`):
   - draws the legal band, existing markers + 9" exclusion rings, and a
     ghost marker that turns red when the cursor is in an illegal spot,
   - click in a legal spot commits.
6. Debug toggle: `GameSettings.AutoPlaceObjectives` (or similar — pick
   wherever lobby settings live). When true, `PlaceObjectivesStage`
   skips requests and runs the existing grid-RNG auto-placer end to
   end. Default: false in GUI mode, true under `--headless` so piped
   tests don't break.
7. AI resolver for `PlaceObjectiveRequest`:
   - Picks a legal position using the same validator from subtask 2
     so by construction it cannot pick an invalid spot.
   - Strategy v1 (intentionally dumb — beating humans isn't the goal
     here, just plausible play): score every grid candidate by
     (distance to own team's nearest table edge, weighted negatively)
     + (distance to nearest existing marker, weighted positively) +
     small RNG jitter, pick the argmax. This gently biases toward the
     AI's own half without being mechanical. Same strategy can later
     swap in for the debug auto-placer to make it more interesting.
   - Routing: the `StageResolverRegistry` is keyed by request type
     only, but the resolver needs to know whether the current placer
     is AI. Simplest path: registry-level dispatcher resolver for
     `PlaceObjectiveRequest` that inspects the request's
     `PlacerPlayerID` (need to add this field) and forwards to either
     the human resolver or the AI one. This pattern will recur for
     every other request once full AI lands, so build it in a way that
     generalizes — but only wire it for objectives now.
   - Tests: AI placing under tight conditions (one legal spot left,
     all-impassable-but-one, etc.) should still find the legal spot.
8. Tests:
   - `ObjectiveOwnershipTests` already exists for seizure; add
     `ObjectivePlacementValidatorTests` for each constraint and an
     integration test that drives `PlaceObjectivesStage` end to end
     with a scripted resolver.

## Decisions

- **Multi-player-per-team placement order**: round-robin within the
  team. When team T's turn to place comes up, the player at index
  `placementsByTeam[T] % team.Players.Count` places. Rationale: cleanest
  generalization of "alternate" — every player on both teams gets equal
  air time. Captain-style ("player 0 always places for the team") was
  rejected because it gives non-captains no agency, and "anyone on the
  team can grab it" was rejected because it requires a UI race / claim
  mechanism that nothing else in the codebase has.
- **`DEPLOYMENT_DISTANCE_INCHES` mismatch (9 vs rules-12)**: do NOT
  change the constant as part of this work item. The deployment code
  reads it too, and shifting deployment is out of scope. Instead the
  validator uses a local `OBJECTIVE_LEGAL_BAND_MARGIN_INCHES = 12`. File
  a follow-up to reconcile the deployment constant against rules under
  #004 / #005 (deployment work).
- **Debug auto-place default**: on in `--headless`, off in GUI. The
  piped-stdin smoke test (`printf "2\n2\n" | dotnet run -- --headless`)
  must keep working without supplying objective coordinates.
- **Network**: no new wiring needed — `PlaceObjectiveRequest` rides the
  existing `StageTaskRequestMessage` bus and
  `NetworkedRequestMessageReceiver` already routes it to the right
  client's resolver registry.
- **AI dispatch**: introduce a `PlacerPlayerID` (or
  `RequestingPlayerID`) field on `PlaceObjectiveRequest` plus a small
  dispatcher resolver that picks human vs AI based on the player's
  `EPlayerType`. Rejected alternative: per-player resolver registries —
  too invasive for one request type, and the `StageResolverRegistry`
  is shared infrastructure used by all stages. The dispatcher pattern
  scales naturally to other requests later, but we only wire it for
  objectives in this item.
- **AI strategy ambition**: deliberately low. The point of objective
  placement is to set up a game, not to demonstrate AI prowess. Smart
  play is a separate concern from "is there a player at this seat at
  all." A meta-comment in the AI resolver should make this explicit
  so future readers don't try to make it competitive.

## Notes

- 2026-05-17: Added AI placement subtask + dispatcher decision. Check
  `origin/ComputerPlayer` for prior AI work before starting subtask 7.
- 2026-05-17: Branch created, gap analysis written, design decisions
  recorded. Next session: start with subtask 1 (`PlaceObjectiveRequest`
  + validator) before touching the stage itself.
