# 459 — Tactician prices its own post-move state: assumed-token overlay for hypothetical rule queries (Artillery's unmoved -1)

**Status**: in-progress (built 2026-11-01, awaiting the orchestrator's verification and Chris's GUI glance)
**Related**: engine commit `88e5cabb`; #442 (D9, the source decision), #425 (Artillery 3.6, its D4 MovedThisRound stamp), #447 (D1 - `RuleInvocation.ParticipantModels`, the precedent this seam follows), #191 (Tactician umbrella), #333 (a 0" move is not a move)
**Branch**: `feature/opr-3.6` in both repos. Priority HIGH (Chris, 2026-11-01: "that should indeed be improved, but it can be an open, high-priority work item").

## Goal

The Tactician's incoming-fire estimate for a MOVE candidate prices its own unit as it will stand after the move,
not as it stands now. The concrete case is 3.6 Artillery's defensive arm (`Not(TokenPresent(MovedThisRound))`,
over 12"): the planner's move candidates kept the unmoved -1 to hit that the move forfeits, so moving an artillery
piece was undervalued (#442 D9). "Done" = (1) an engine seam - a read-only "assumed tokens" overlay a rule QUERY
carries, consulted by the token-reading conditions before the live tokens, structurally unable to reach live
resolution; (2) `CombatMath` / the planner pass "this defender will have moved" for a move candidate and nothing for
a Hold; (3) tests: overlay semantics, never-writes, live unchanged, Artillery estimate loses the -1 on a move and
keeps it on a hold, non-Artillery unchanged, a Shaken assumption to prove the seam generalises, and a planner pin
with a negative control. No bench (a pricing correction on a narrow case).

## Notes

- 2026-11-01 (built, option (a) of #442 D9): engine - `Rules/Definitions/AssumedTokens.cs` (new, immutable: entries
  of (UnitID, TokenType, count); `Present(unit, type, count = 1)`, `Absent(unit, type)`, `With(...)`, `TryGetCount`);
  `RuleInvocation.AssumedTokens` (new optional record parameter, after `ParticipantModels` exactly as #447 D1 added
  that one) plus `RuleInvocation.TokenCount(unit, type)` / `HasToken(unit, type)` - the overlay's count when it names
  the pair, the live container's otherwise. `RuleEvaluator.EvaluateAllNamed(context, AssumedTokens? assumed, params
  participants)` is the ONE entry point that accepts an overlay; it threads `assumed` through `CollectSurviving ->
  CollectTagged -> CollectFromRules / CollectGrantedRules -> new RuleInvocation(...)`, default null everywhere, so
  `Evaluate`, `EvaluateAll`, `EvaluateAllNamedLive`, `GatherOffers`, `ResolveAbility` (the live paths) cannot carry
  one. Conditions routed through the invocation: `TokenPresent`, `CountsAsFatiguedInMelee` (its static `Holds(IUnit)`
  stays the stage layer's live predicate), `TargetShakenOrFatigued` (now a plain `Condition` with the IHasTarget
  check written out, because `CapabilityCondition.Evaluate` is sealed and hides the invocation). `RuleFireLint` /
  `TokenReadership` unchanged - no new Condition type, and the lint seeds real tokens into its own world.
  AI - `AttackContext.DefenderMoved: bool?` (new trailing parameter; null = live, true = assume MovedThisRound, false
  = assume unmoved); `CombatMath.EstimateVolleyCore` builds `DefenderAssumptions(def, context)` once and hands it to
  the hit-modifier, hit-complete and save-complete dispatches (every dispatch of the volley the defender sits in);
  `TacticalAnalysis.ExpectedShootingAt(..., bool? defenderMoved = null)`; `TacticianPlanner.Score` computes
  `candidateMoves` (`Intent != Hold && Distance(now, end) > 0.0001"`, the MovementStage stamp criterion - a 0" plan
  is not a move, #333) and passes `DefenderMoved: candidateMoves ? true : null` on the per-enemy incoming estimate
  (the `ShotKey` memo includes the context record, so hold and move estimates are cached apart). Not changed: the
  live rule, the stage, the Combat Calculator (no defender-moved input exists - see D4), the two-moves-out forecast
  (next round, token cleared - D3), the activation urgency's `theirs` (candidate-independent).
  Tests: engine `Tests/AssumedTokenOverlayTests.cs` (+11): TokenPresent assumed present / assumed absent over a live
  token / no overlay / MinCount vs assumed count / scoped to the named unit and token (later entry wins); Shaken
  assumption reaches `CountsAsFatiguedInMelee` and `TargetShakenOrFatigued`, the target never written, the static
  predicate unchanged; the evaluator query drops Artillery's -1 as moved and keeps it live / as unmoved / null
  overlay; reflection pin that `EvaluateAllNamed` is the only public `RuleEvaluator` method taking `AssumedTokens`;
  `EstimateShooting` as moved == a plain unit, hold == 2/3 of it (Q4: 3/6 vs 2/6), explicit unmoved == hold,
  non-Artillery unchanged, no token stamped; an unmoved assumption over a LIVE moved token restores the -1 via
  `ExpectedShootingAt`; planner `Score`: Artillery vs an identical plain unit 30" from 30" rifles with a 12" pistol
  (no offense term) - Hold scores higher (the -1 priced), the closing move scores EQUAL (priced as moved). Negative
  control run by hand: with the planner passing `DefenderMoved: null` the Move line fails (artillery=-0.0243 vs
  plain=-0.0365); with it, the fixture is 11/11. First draft of the pin passed without the wiring because the move
  it picked closed to within 12" of the enemy's reach (the -1 off for both) - fixed by the 30" geometry and picking
  the candidate that closes most.
  Verification: engine suite 4507 -> 4518 passed / 1 skipped; clean full `dotnet build --no-incremental` 0 errors /
  208 warnings (= baseline); app suite 4143 -> 4143; headless smoke (`printf "2\n2\n" | FdgRaylib --headless`) exits
  0 with `Game result:`. ASCII grep over the added lines clean. Bench: not run (D5). One extra headless game with an
  Artillery-carrying 3.6 army (`FdgRaylib --headless --army "FdgLab/armies/Robot Legions 2k - Mixed.fdgarmy"
  --ai-profile tactician`, CLI EOF defaults vs the Tactician): exit 0, `Game result: outcome=Win winner="Player 2
  (AI)" rounds=4 scores=[0, 3]`, no exceptions / retries / fallbacks in the log - the AI still plays.

## Decisions

- **D1 - the overlay rides the read-only query, not the participant or the context (2026-11-01).** Three places
  could carry it: a field on `RuleParticipant` (per-bearer, but `TargetShakenOrFatigued` reads the TARGET's tokens,
  which is the other participant), a capability on the hook context (would make every context type grow a field the
  stages never set), or a parameter on the evaluator's query that every invocation of that walk receives. The last
  is the smallest and the only one that gives the structural guarantee the brief asked for: the overlay is a
  parameter of `EvaluateAllNamed` alone (the read-only query - no log, no one-shot grants spent), so no live path has
  a way to be handed one. A reflection pin keeps it that way. The overlay names units by `UnitID`, so one object can
  hold assumptions about both sides of an evaluation.
- **D2 - the conditions read through `RuleInvocation.TokenCount` / `HasToken`, not through a wrapped container
  (2026-11-01).** A fake `ITokenContainer` over the unit would have let every token reader see the overlay for free,
  but `IUnit.Tokens` is the shared, synced container and the hot path (`TokenContainer` is ~19% of the search's CPU
  per the #191 profile); wrapping it per invocation would allocate on every dispatch. Two small helpers on the
  invocation, consulted only by the three count-reading conditions, cost nothing when the overlay is null. Effects
  that read tokens for amounts (token-scaled markers) and `AnyoneListens`'s RuleGrant check read live - documented in
  the type summary; add a reader there when an estimate needs it.
- **D3 - a Hold candidate passes null (live), not false (2026-11-01).** A unit that already carries MovedThisRound
  (an activation-start free move, a Dash earlier in the round) keeps it through a Hold, so the live token is the
  truth there; `false` exists for a next-round what-if (the token clears at round end), pinned but not used by the
  planner. The two-moves-out forecast (`projValue`) is left live for the same reason: it is next round's fire.
- **D4 - the Combat Calculator is not wired (2026-11-01).** `CombatSituation` has `AttackerMoved` and
  `AttackerFatigued` but no defender-side moved input; the brief said wire it only if an obvious one existed. The
  calculator reads the defender's live tokens through the real stages, which is what it should show. If a "Defender
  moved" tick is ever wanted, `AttackContext.DefenderMoved` is the shape to copy and it should feed the stages a
  real token on a scratch unit, not the overlay (the calculator runs live stages, not queries).
- **D5 - no bench (2026-11-01).** The change only lowers the planner's incoming-fire estimate for an Artillery unit's
  move candidates (a few percent of the retaliation term for one rule); a 6,400-game bench cannot resolve it and is
  not owed. The planner pin with its negative control is the evidence that the term moved the right way.

## Outcome

Open: built 2026-11-01, awaiting the orchestrator's verification and Chris's GUI glance (nothing visible changes;
the glance is that an AI Artillery unit still plays - it moves when the objective asks and holds to shoot
otherwise). Not archived.
