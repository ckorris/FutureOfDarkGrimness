# 264 — Tactician: unit behind impassible terrain rushes sideways/backwards instead of advancing

**Status**: in-progress (11 pins green; all 8 issues fixed and merged to master; issue 1's melee half
folded in 2026-07-25. Remaining: issue 8b hysteresis - deliberate deferral, Chris's call - and the
GUI eyeball check)
**Related**: #256 (prior stuck-unit pass), #216 (silent solo fallback residual), #211 (solo impassible),
#191 (Tactician umbrella), #167 (scenario terrain = enabling tooling), #170 (deploy sibling)

## Goal

A Tactician-controlled unit deployed behind a large impassible terrain piece spends round 1 making
real progress around it (or holds for a justified reason) — never a full-distance rush sideways or
backwards, and never a sub-inch repack shuffle sold as an "advance". Done = the currently-failing
tests below pass, and Chris's Knight-Brothers-behind-wall game shape plays sanely end-to-end.

## Evidence (2026-07-23, Chris's GUI game — no save)

Knight Brothers 3k (root folder) as a Tactician bot vs Chris playing "Elves 3k Import Bug.fdgarmy"
(fdg-raylib_Green root). Round 1: Knight Battle Brothers (Combined), 10 models + joined Knight
Master Brother = 11, deployed behind a large impassible piece, moved sideways/backwards instead of
advancing. Same symptom class as pre-#256 reports but post-#256 engine. No save; army files exist.
Hero has same speed/base as the unit, so the per-model-budget mismatch (issue 6) was NOT the driver
this time.

## Issues found (code-read 2026-07-23; each with repro + fix sketch)

Attack order agreed: **1 -> 2 -> 3** (likely actual cause), then 5/6, then 4, 7, 8.
Enabling first slice: scenario terrain support (#167 facet) so every repro is a one-command scenario.

### 1. Euclidean gradient + flat Reachable bonus => retreating IS the argmax behind a wall
`TacticianPlanner.ObjectiveApproach` (lines ~584-612) and `MacroActionGenerator.Plan`'s
progress/feasibility labels are straight-line; walking around a large wall closes ~0 straight-line
distance (worked example: 8" rush around a 20"-wide wall closes 0.3" of a 25" gap -> approach
~0.006). Meanwhile FallBack/Concentrate/SeekCover/Block reach trivial goals and collect
MoveReachableBonus = 0.05 -> 0.05 beats 0.006 and the unit rushes backwards/sideways at full
distance. Negative approach clamps to 0, so retreat is never charged.
- Repro test: wall geometry above (unit (24,6), wall x 14-34 at z~10-12, enemy (24,40), objectives
  midline); BeginActivation -> ChooseAction -> TakePlannedMove; assert winner's end centroid closes
  >= 1" toward nearest objective. Twin: assert Score(RushObjective) > Score(FallBack). Both fail today.
- Fix: gradient on pathfound arc length (TerrainGrid/GridPathfinder route already shared per
  activation); demote MoveReachableBonus to epsilon tie-break or gate on substantive score > 0.

### 2. MoveReachableBonus rewards pointless-but-reachable goals (independently fixable half of 1)
- Fix: bonus only when the candidate's substantive terms are positive, or exclude the retreat
  family when no threat is in range.

### 3. FindPath failure => wall-piercing straight line; S4 snake follows the same line
`PlanMoveToward` (~552-560): null path falls back to {start, goal} straight through the wall; the
snake candidate snakes along that same line, so the #256 S4 rescue is structurally dead exactly
when pathfinding fails. FindPath returns null when the GOAL cell is inside the inflated blocked
region (`GridPathfinder.FindPath` ~107) — generator-invented goals do this (M8 lane midpoints
inside a piece, M4 band points, cover goals, objectives hugging terrain) — or when the start
pocket is sealed by inflation.
- Repro test: goal point within base-radius inflation of an impassible piece -> FindPath null;
  PlanMoveToward toward it asserting >= 2" net centroid progress (fails: near-stay).
- Fix: retarget blocked goal cell to nearest unblocked cell; when no path exists, plan toward the
  nearest REACHABLE point toward the goal or mark the candidate infeasible (never the straight line).

### 4. Waypoint funneling burns wide formations' budget
`BuildPathCandidate` (~519-522) routes every model through shared interior waypoints; flank models
backtrack to waypoint 1, worst-model metric shrinks the arc toward 0 -> near-stay. Distinct from
#256 S4 (slots-in-walls).
- Repro test: spread 11-model pack at a wall corner with waypoint ~4" lateral; assert PlanMoveToward
  centroid progress >= 2".
- Fix: each model joins the path at its nearest point AHEAD on the polyline; or snake earlier
  (funnel overhead > half budget).

### 5. Mixed validation errors defeat both rescue gates
S2 re-aim needs errors.All(EndedOnFriendlyUnit); S4 snake needs errors.All(MovingThroughImpassible)
(`MovementPlanner.ValidateWithBackoff` ~202-226). Round-1 density produces both at once -> plain
halving to sub-inch shuffle.
- Repro test: unit behind wall + adjacent friendly so the centered candidate carries both error
  types; assert ladder output nets >= 1".
- Fix: run gates on the filtered error set (snake when impassible errors present, then re-aim on
  the residue).

### 6. Silent solo fallback + solo skirting up to +/-100 degrees at full rush
Failed request-time re-validation silently plays the solo resolver (#216 residual). Solo skirting
(`AiDefineMovementResolver` SkirtAngleOffsetsDegrees ~187-188) tries up to +/-100 deg — past
perpendicular — at melee/hybrid FULL distance: an 8-12" lurch sideways-to-backwards. Fallback
amplifier for every issue above.
- Repro test: solo resolver behind a wall where only >= 80 deg angles are clear; assert chosen
  direction has dot(move, toEnemy) > 0 (fails at 100 deg). CAUTION: fix moves the pinned solo D1
  baseline -> benchmark re-pin required.
- Fix: cap skirt at ~+/-60 deg and/or scale step by forward component; longer-term route solo
  through GridPathfinder. Plus #216's repair pass + a log line whenever Tactician degrades to solo.

### 7. Latent: Plan() validates with a flat unit budget; resolver re-checks per-model
`MacroActionGenerator.Plan` (~374-376) passes `_ => new ModelMoveBudget(budget, budget)`;
`TacticianMovementResolver` re-checks with request.BudgetFor per model. A joined Slow/Fast hero
makes EVERY planned move fail re-check -> permanent silent solo fallback for that unit. Not live
in Knight Brothers (hero speed matches) but exactly the joined-hero shape.
- Repro test: Slow hero joined to a normal unit; assert the Tactician's planned move is submitted
  (no solo fallback).
- Fix: thread the request-accurate per-model budget function into Plan/BuildCharge.

### 8. Terrain-blind deployment self-traps big units; no cross-round hysteresis
`TacticianPlaceObjectsResolver` aims at objective lanes, only avoids OVERLAPPING terrain -> parks
an 11-model block in a pocket (this game). Separately, each activation re-argmaxes from scratch ->
possible oscillation (snake-stretch then re-grid; left-vs-right around the piece).
- Repro tests: (a) deployment with a wall across the zone's lane; assert chosen center's forward
  route is not blocked (or post-deploy best advance nets >= 2"); (b) drive 2-3 rounds headless in
  the walled scenario; assert cumulative progress toward one goal >= N inches.
- Fix: penalize centers whose route to the aim crosses impassible terrain (the deferred A4b
  "cover-aware centre" sub-slice is the natural home); small commitment bonus to last intent/goal.

## Tooling

- **Scenario terrain support** (#167 deferred facet) is the enabling slice: gives a committed
  knights-behind-wall scenario, `fdglab analyze` candidate-table dumps from it, and a `--scenario`
  GUI launch for the eyeball check. Engine-side (ScenarioCompiler); started 2026-07-23.
- Add #256's stuck-detector log ("every movement candidate nets < 1 inch") so future games
  self-report this class.

## Handoff: implementing the fixes (written 2026-07-23 for a fresh session)

Everything needed is in this file + the code; no conversation context required.

**State.** Superproject AND the `FutureOfDarkGrimness` submodule are both on branch
`264-walled-unit-pins` (stacked on `167-scenario-terrain`; neither pushed nor merged - master has
NO pins and NO scenario terrain). A fresh session opens the repo already on these branches.
The pins: `FutureOfDarkGrimness/Tests/TacticianWalledUnitTests.cs` - 9 tests, all red, tagged
`Category("Pending264")`. Their failure messages embed the planner's full scored candidate table.

**Authorization + rules of the road.**
- Chris authorized engine-submodule changes for this item (fixes live in `Ai/Tactician/` and
  `Ai/Resolvers/`). Submodule-first commit cadence per CLAUDE.md.
- Read `docs/ResolverGuide.md` BEFORE touching movement/resolver code (CLAUDE.md requirement).
- Exit gate per slice: its pin(s) flip green WITHOUT weakening any assert; move green tests out
  of the Pending264 category so they join the main suite. Until all slices land, verify with
  `dotnet test FutureOfDarkGrimness/FutureOfDarkGrimness.csproj --filter TestCategory!=Pending264`
  (1928 green today) plus a targeted run of the pin fixture.
- Any `TacticianWeights` constant change needs Chris's explicit sign-off AND a benchmark run
  attached to the commit (file-header policy).
- One slice per commit; update this file's Notes (newest on top) each slice; never silently cut.

**Slice order and concrete notes** (pin name in parentheses):
1. Issues 1+2 (`WalledUnit_ArgmaxMove...`, `WalledUnit_RushingTheObjective...`,
   `WalledUnit_ThreeActivations...`): `TacticianPlanner.ObjectiveApproach` (~line 584) measures
   the objective gap as straight-line Distance; make the gradient pathfound-route-aware (reuse
   `TerrainGrid.Build` + `GridPathfinder.FindPath`; cache per activation like
   MacroActionGenerator's `sharedGrid` - decisions have a ~0.5s budget, so avoid a fresh
   pathfind per (candidate x objective); one route per objective from the START, then measure
   candidate ends against that polyline, is likely enough). `MacroActionGenerator.Plan` (~374)
   grades progress/feasibility straight-line too. The Reachable-bonus half (issue 2): gate
   `MoveReachableBonus` on substantive terms > 0 (or demote to epsilon tie-break) - that is a
   weights-policy change, see above. CRITICAL refinement from the pin run: forward candidates
   also pay ~0.04 retaliation that the retreat dodges, so the pathwise approach credit must
   outweigh that asymmetry or fixing the bonus just converts retreat into freeze.
2. Issue 3 (`PlanMoveToward_GoalCellInsideWallInflation...`): `GridPathfinder.FindPath` (~107)
   returns null when the GOAL cell is inflation-blocked - retarget to the nearest unblocked
   cell instead. `MovementPlanner.PlanMoveToward` (~560) falls back to the straight
   `{start, goal}` line on null - plan toward the nearest REACHABLE point instead (never the
   wall-piercing straight line; it also structurally kills the S4 snake).
3. Issue 5 (`...MixedErrors_StillThreadsTheCorridor`): `ValidateWithBackoff` (~202-226) - the S2
   gate needs `errors.All(EndedOnFriendlyUnit)`, S4 needs `errors.All(MovingThroughImpassible)`;
   run the gates on the RELEVANT error subset instead (snake when impassible errors are present,
   then re-aim on the residue).
4. Issue 6 (`SoloResolver_OnlyWideSkirtAngles...`): `AiDefineMovementResolver.
   SkirtAngleOffsetsDegrees` (~187) tries up to +/-100 deg at full budget - cap ~+/-60 and/or
   scale the step by the forward component. CAUTION: this moves the pinned solo D1 baseline -
   re-pin the benchmark hashes (current: 3674C906996F34CC mirror / CE3DC8150005FF2C vs basic,
   200 games, DOP 16 - see the MovementPlanner file header and the #191 ledger;
   `dotnet run --project FdgLab -- bench ...`). Same slice: add the #216 log line whenever the
   Tactician silently degrades to the solo resolver, and #256's stuck-detector log ("every
   movement candidate nets < 1 inch").
5. Issue 4 (`...WideFormationAtWallCorner...`): `BuildPathCandidate` (~519) prepends the SAME
   passed-waypoint list to every model's path - have each model join the polyline at its nearest
   point AHEAD instead. Note the pin's geometry: the burn only bites when the first bend is
   inside the budget (corner-hugging); don't regress the centered case that works today.
6. Issue 7 (`PlannedMove_UnitWithASlowModel...`): `MacroActionGenerator.Plan`/`BuildCharge`
   validate with a flat `_ => new ModelMoveBudget(budget, budget)`; thread a request-accurate
   per-model budget function in (derive from the same `MovementRuleQueries` DefinePathStage
   uses, or plan at the min per-model budget - the resolver re-checks per-model and silently
   solo-falls-back on any mismatch).
7. Issue 8 (`Deployment_ObjectiveLaneWalledOff...` + the ThreeActivations pin doubles as 8b):
   `TacticianPlaceObjectsResolver` - penalize candidate centers whose route to their aim point
   crosses impassible terrain (path-distance vs Euclid, like the pin's PathDistance helper);
   cross-activation hysteresis = small commitment bonus toward the previous intent/goal.

**Tools.** `Scenarios/example-walled-advance.json` -> `--make-scenario` -> `fdglab analyze`
prints the same candidate table as the pin failures, for eyeballing scenes; `--scenario` launches
one in GUI. When done: merge cadence 167-scenario-terrain -> 264-walled-unit-pins -> master, and
Chris still owes the GUI terrain-render hand pass (#167 note).

## Notes

- 2026-07-25 (issue 1, the melee half): **FIXED - the planner's melee APPROACH term is now
  route-aware.** Slice 1 made `ObjectiveApproach` measure walking distance and deliberately left its
  melee twin on straight-line distance ("still owed" item 3). Two findings while folding it in:
  - **The residual as recorded was INERT, and the real one was a level up.** The owed item named
    `MacroActionGenerator.BuildCharge`'s `progress` (~line 417). That value only ever separates
    `Blocked` from `BudgetClipped`, and EVERY consumer tests `== Reachable`
    (`ActionNameFor`, the offense branch of `Score`, the reachable tie-break) - while `Enumerate`
    itself discards the charge candidate outright unless it is already Reachable, substituting a
    rush-budget approach. So no straight-line grade there is observable, and "fixing" it would have
    changed nothing. NOT changed, deliberately: adding route machinery to feed an unread value is
    exactly the dead-tunable the slice-3 cleanup removed. The mechanism the note was reaching for
    lives in `TacticianPlanner.Score`'s `approach` term - the ONLY term that pays a melee unit for
    crossing the table - which measured the charge gap as `Distance(now/end, enemyPos)`.
  - **The fix.** New `RouteToEnemy` (the melee twin of `RouteToObjective`, cached per enemy per
    activation, sharing `_routeGrid`); `gapNow`/`gapEnd` measure along that route. The A5-6 stage gap
    stays straight-line on purpose - it models weapon threat, which does not walk around walls - and
    since route distance >= straight-line it can only hold the approach back, never flatter it.
  - **Gated on an actual DETOUR (`routeLength > straight + 0.01`), and this mattered.** The first cut
    used `RemainingFrom` unconditionally and moved the no-detour case too, because that measure is
    "offset onto the route + route remainder": on a clear lane it charges LATERAL displacement as
    distance not closed, so a flanking step reads as a wasted one. Straight-line is the exact answer
    there, and `RemainingFrom` only earns its approximation error where the detour it approximates
    exists. Gated, the slice is strictly additive - scoring is untouched wherever no impassible piece
    stands between the unit and its target.
  - **New pin**, red-by-design first: `TacticianWalledUnitTests.
    WalledMeleeUnit_ApproachingItsTarget_OutscoresFullDistanceRetreat`. 11 blades behind the 20" wall,
    an enemy gunline beyond it, and NO objectives on the table, so the melee gradient is the only
    substantive term and the pin cannot pass on slice 1's objective fix. Old code: the detour ends at
    (14.5,8.7), which LENGTHENS the straight-line gap, so approach paid exactly 0.0000 and the
    retreat tied it at an identical -0.0612. Carries a geometry guard (route distance closed must
    exceed 4x the straight-line distance closed) so a refactor cannot make it vacuously pass.
    Suite 2145/2145; all 11 walled pins green; full `dotnet build` + headless smoke exit 0.
  - **TWO RECORDED BASELINES IN THIS FILE ARE STALE - do not diff against them.** Master moved
    through #266-#280 since the slice work. Re-derived on current master: solo D1 builtin mirror is
    `0CBA6DA5E9DD658A` (NOT `F82D5A91B0119955`), builtin vs builtin-basic `F9B57FB951EE5F0A`, and the
    8-army pool aggregate is 84.0% / hash `B33FC1161F52B3A0` (NOT 84.7%). Every benchmark below was
    re-run as a matched control on current master rather than compared to the recorded numbers.
  - **Solo D1 bit-identical**: `0CBA6DA5E9DD658A`, unchanged. Structural as well as measured - with
    both profiles defaulting to SoloRules the `TacticianPlanner` is never constructed.
  - **Correction to a premise this file implies**: the bench TABLE is not terrain-free.
    `GameSettings.GetDefault()` places 20 pieces via `AutoFromLayout`; "the builtin bench army has no
    terrain" is about the army list. So the Tactician mirror legitimately moves and bit-identity was
    never achievable there: 92.7% (177/6/17) -> 92.0% (175/7/18), inside noise for 200 games.
  - **Pool re-gate, 8-army pool, Tactician vs SoloRules, 64 matchups x 50 games = 3200 games each
    side, DOP 12** (baseline `B33FC1161F52B3A0` vs `3E48E05C84E2F476`): aggregate **84.0% -> 84.2%**
    (+0.2pp against a 0.65pp aggregate sigma - flat), faults **1 -> 0**, six of eight army rows up or
    level (Hives +1.5, HEF +0.9, DE +0.6, BB/Dwarf +0.1, HDF 0.0; Orks -0.2, RL -1.4). Worst cell
    57% -> 51% is RL-vs-Hives moving -9.0pp, which is 1.3 sigma on a 50-game cell (1 sigma = 6.9pp) -
    noise, not a collapse. Flat is the expected shape and matches slice 1's own reading: this pool's
    terrain barely exercises the walled pathology, so the pool is a NO-REGRESSION gate and the pin is
    what evidences the fix. Decision cost mean 26.26 -> 25.38ms, worst p95 579.7 -> 572.2ms (the
    per-enemy A* costs nothing measurable; both runs exceed the ~0.5s budget at p95, pre-existing).
  - **Still owed after this slice**: issue 8b cross-activation hysteresis (unchanged deliberate
    deferral, Chris's call) and the GUI eyeball check. The `BuildCharge` line item is now CLOSED as
    not-a-defect per the inertness finding above, not by fixing it.

- 2026-07-23 (issue 5, the real fix): **ISSUE 5 FIXED - the mixed-error rescue gate.** Engine
  `50dce66`. `MovementPlanner.ValidateWithBackoff`'s two #256 rescues were all-or-nothing: the S4
  snake fired only when `errors.All(MovingThroughImpassibleTerrain)` and the S2 re-aim only when
  `errors.All(EndedOnFriendlyUnit)`, so a candidate carrying BOTH faults (round-1 density: a wall
  plus a friendly parked in the pocket) shut both gates and the ladder halved. Now: the snake goes
  FIRST and fires on the PRESENCE of any impassible fault (`errors.Any`), and the re-aim fires on
  sole-friendly (unchanged) OR - only when a snake exists to have taken the impassible fault first -
  on the friendly RESIDUE of a mixed fault. Both rescues already re-validate their candidate, so
  widening when they fire can never submit an illegal move; it only gives them a chance in a mixed
  round instead of surrendering to halving.
  - **Solo bot bit-identical, NO D1 re-pin.** The solo resolver (`AiDefineMovementResolver`) passes
    `reaimAt` but NEVER `snakeAt`. The mixed re-aim arm is gated on `snakeAt != null`, so for the solo
    bot the re-aim predicate collapses to exactly the old `FriendlyStackingIsSoleObstacle` and the
    snake block never runs. Confirmed: builtin mirror `F82D5A91B0119955` and builtin vs builtin-basic
    `A7EEB33FD9CEFC6A` (200 games DOP 16) - both bit-identical to the slice-5 baseline, re-run against
    a fresh Release build that links the change. The fix is Tactician-only.
  - **New pin, reached the gate directly.** `TacticianWalledUnitTests.
    PlanMoveToward_MixedFaultsAtFullArc_ThreadsWithoutSurrenderingBudget`. The old issue-5 pin
    (`WallAndFriendlyMixedErrors`) had gone green only as a slice-3 side effect: in a tight corridor
    halving eventually separates the two faults (the grid endpoint climbs past the friendly), so the
    snake still fires - just at a shorter arc - and the pin passed without ever exercising the mixed
    gate. The new scene parks the friendly UP the corridor at the far end (east wall), so the
    FULL-BUDGET pack carries both faults at once. Old code: 9 of the 10 pins pass, this one nets only
    3.3" of 12" (rounds the corner but surrenders most of its budget to halving); the fix threads the
    corridor single-file at the top arc and nets 9.25". Assert: net >= 6". Verified red-by-design
    (stash the fix -> only this pin fails, other 9 stay green) and green with it. Geometry guard
    asserts the full-budget pack really carries BOTH fault types, so a refactor cannot make it
    vacuously pass. Full suite 1938/1938 (was 1937).
  - **"Re-aim on the residue" - built but not separately pinned.** The mixed re-aim arm (side-step
    the pack when a friendly fault remains after the snake could not thread) is in place, but the new
    pin is rescued by the SNAKE alone (the friendly is off the single file's lane). A scene where the
    snake's own lane is blocked by a friendly AND lateral room exists to side-step it is the residual
    case; in a tight corridor there is no such room (a friendly within one base-width of the route
    cannot be cleared laterally - you cannot walk through it), so that case is often genuinely
    unsolvable that turn. Left unpinned deliberately; flag if a natural scene turns up.

- 2026-07-23 (implementation, slices 2-6): **ALL 9 PINS GREEN.** `Category("Pending264")` removed;
  full suite 1937/1937 with no filter. Engine `693d1d2`..`d6c22de`. Slice order was CHANGED mid-run
  on evidence - see below.
  - **Slice 2 = issue 3** (`693d1d2`). `GridPathfinder.FindPath` returned null whenever the GOAL's
    own cell was inflation-blocked; the grid tests CELL CENTRES, so that fires for goals a base can
    legally stand on. It now retargets to the nearest cell a base can centre in (bounded ring
    search) and keeps the true goal as the last hop when that hop is walkable. New
    `FindPathToNearestReachable` (uniform-cost flood) handles the sealed-pocket case, so
    `PlanMoveToward` never concedes to the wall-piercing straight line. Its pin did NOT flip on this
    slice: the unit went from crawling into the wall face to ROUNDING the wall and stopping 6.65"
    short, because each 12" move only netted ~6.4".
  - **ORDER CHANGE (call made, flagging it):** that residue was issue 4, and issue 4 turned out to
    gate THREE pins. Taken next, ahead of the planned 5/6. The handoff's order was a proposal made
    before the fixes existed; the evidence promoted 4.
  - **Slice 3 = issue 4** (`c02a6b2`). The handoff called this a funnel burning the budget.
    Measurement says the consequence is worse: the moves were ILLEGAL, not merely long. Two
    mechanisms. (a) Every model was prefixed with the same traversed-waypoint list - told to hop to
    the route's first bend - and for a rank drawn up across the route's mouth that hop CLIPS the
    piece the route detours around. Each model now joins the route at the point nearest it,
    string-pulled. (b) The snake's parallel files STRADDLED the route, but a pathfound route only
    guarantees one base-width of clearance, so at a corner the inboard file sits inside the wall.
    File 0 now rides the route; the rest fan to whichever side a terrain check says is open.
    Measured on the pin geometry: pack and snake both faulted at 12", 6" and 3", the snake
    validating only at a 1.06" shuffle; now it validates at full arc and travels 7.04".
    Flipped FOUR pins: WideFormationAtWallCorner, GoalCellInsideWallInflation, ThreeActivations
    (the unit now rounds the 20" wall and takes the marker), WallAndFriendlyMixedErrors.
  - **ISSUE 5 IS NOT FIXED - only unpinned. Needs Chris's call.** [SUPERSEDED 2026-07-23 - fixed,
    see the top note "issue 5, the real fix".] Its pin went green as a side
    effect of slice 3: that scene no longer reaches the rescue gates. The gates themselves are
    still all-or-nothing (`ValidateWithBackoff`: S2 needs `errors.All(EndedOnFriendlyUnit)`, S4
    needs `errors.All(MovingThroughImpassibleTerrain)`), so a candidate carrying both error types
    still disables both rescues. Either fix the gate on the filtered error subset as originally
    planned, or write a pin that reaches it. Not cut, not done.
  - **Slice 4 = issue 7** (`76c3c48`). Both ends, because they fail independently: new public
    `MovementRuleQueries.PerModelMoveBudgets` lets the generator plan at the SLOWEST model's
    allowance and validate each model against its OWN cap (the unit scalars take the MAX across
    models, which is the whole bug); and `TacticianMovementResolver` now RE-PLANS toward the same
    destination under the request's budgets before conceding to solo (#216's repair pass). The
    resolver half is what covers budgets the planner cannot derive - the request is authoritative.
  - **Slice 5 = issue 6** (`39c2c49`). Solo skirt capped at +/-60 degrees (was +/-100 - past
    perpendicular a "skirt" is a retreat, taken at the FULL rush budget). Plus the two diagnostics:
    a log line whenever the Tactician degrades to solo, with the reason, and #256's stuck detector
    ("every movement candidate nets < 1 inch"). **Caveat: both ride `TacticianOptions.DecisionLog`,
    the only sink the AI resolvers have, so they appear under fdglab and `--log-decisions` but NOT
    in an ordinary GUI game log.** Routing them to the normal log needs a channel the AI resolvers
    do not receive today (the same gap the registry factory already notes for the rule evaluator).
  - **D1 BASELINE RE-PINNED** (the skirt change is deliberately solo-bot behavior), 200 games DOP
    16, reproducible across duplicate runs, zero faults, zero timeouts:
    builtin mirror `F82D5A91B0119955` (27/27/146; was `3674C906996F34CC`, 29/29/142) and builtin vs
    builtin-basic `A7EEB33FD9CEFC6A` (36/25/139; was `CE3DC8150005FF2C`, 40/25/135). The mirror
    staying perfectly symmetric is the sanity check. MovementPlanner's file header updated; #191
    ledger note added. Every hash reference older than this refers to the previous pins.
  - **Slice 6 = issue 8a** (`d6c22de`). `TacticianPlaceObjectsResolver` scores a candidate lane by
    path distance vs straight-line distance to its aim and probes laterally for one under a 4"
    detour - the same measure the movement gradient uses, so deployment and movement now agree
    about what "toward the marker" means.
  - **Issue 8b (cross-activation hysteresis) NOT built.** The ThreeActivations pin covers 8b
    behaviorally - no oscillation across three activations - so the commitment-bonus term was not
    needed to make the metric, and adding an unpinned scoring term was not worth the benchmark
    risk. Deliberate deferral, Chris's call whether it is still wanted.
  - **Two cleanups after the slices** (engine `142bb4f`, `6302993`): the six slices each grew their
    own copy of the same impassible-sweep predicate, now one public `GridPathfinder.SegmentClear`
    (routing, leg joins and the score gradient MUST agree on what "clear" means). And slice 3's
    early-snake funnel guard (`FunnelStallFraction`/`routeBends`/`CentroidTravel`) was REMOVED: it
    encoded the first hypothesis for issue 4 - that funnelled moves were short but legal - which
    measurement disproved (they were illegal). Setting its fraction to 0, disabling it entirely,
    changed nothing in all 1937 tests, so it was dead code with a tunable constant that a future
    reader would take for load-bearing.
  - **Still owed / open questions for Chris:**
    1. ~~Sign-off on the `MoveReachableBonus` gate (slice 1).~~ SIGNED OFF (Chris, 2026-07-23) - the
       gate stays: the bonus is a tie-break among positive-scoring plans, not a flat reward for any
       reachable goal. (Issue 5's disposition is also resolved - fixed 2026-07-23, see the top note.)
    2. The GUI eyeball check on the walled scenario (`--scenario Scenarios/example-walled-advance.json`)
       and the real Knight-Brothers-behind-wall game shape - the Goal's second half, which no test
       can settle. #167 also still owes the GUI terrain-render hand pass.
    3. `BuildCharge` still grades its blocked-lane approach with straight-line progress (noted in
       slice 1) - issue 1's mechanism on the melee side, never folded in.
  - **Behavioral check on the committed repro** (`Scenarios/example-walled-advance.json` ->
    `--make-scenario` -> `fdglab analyze --unit Dummies`), against the pre-fix table recorded in the
    2026-07-23 "scenario terrain landed" note below:
    - BEFORE: `RushObjective(24,24)` came back **Blocked with NEGATIVE Euclidean progress** (its
      end (33.6,9.2) is farther from the marker than the start), Hold and a full-8" backwards
      FallBack scored IDENTICALLY at 0.0300 (the reachable bonus), and the top three candidates sat
      within 0.0008 of each other - noise.
    - AFTER: the SAME endpoint (33.6,9.2) - the correct detour around the wall's east end - is now
      graded `BudgetClipped` and scores **0.1027, top of the table**; FallBack is now the WORST
      candidate at **-0.0200** (bonus gone) and Hold is -0.0165. The spread from best to retreat is
      0.12 instead of 0.0008. Exactly the inversion the issue described, inverted back.
  - **Pool re-gate, 8-army pool, Tactician vs SoloRules, 64 matchups x 50 games = 3200 games each
    side, DOP 12** (pre-#264 `CB789773649FF9E9` vs slice-1 `3C9390C7F2F9B747`): aggregate **82.8%
    -> 82.8%**, 0 faults both runs. Per-army rows move within +/-1.1 (HEF +1.1, HDF +0.8, RL +0.6;
    Dwarf -0.5, Orks -0.6, DE -0.4), no cell collapse. Flat is the expected and correct result: this
    pool's terrain barely exercises the walled pathology, so the gate is a no-regression check, not
    a win. Same gate on the FINAL state (all six slices, `F8EF5299BC0D4115`, DOP 16) is a real
    improvement: aggregate **82.8% -> 84.7%**, 0 faults, worst single cell **52% -> 57%**, and
    SEVEN of the eight army rows up - HDF +3.5, DE +3.1, RL +2.6, HEF +2.5, BB +1.8, Hives +1.5,
    Orks +1.2; only Dwarf Guilds down (-1.1). Best cells RL-vs-Orks +13, BB-vs-Orks +12,
    HDF-vs-RL +11; worst Dwarf-vs-Hives -6. That the win comes from slices 2-6 rather than slice 1
    is the expected shape: slice 1 is scoring (it only bites behind a wall), while the movement
    fixes - blocked-goal pathfinding, per-model route joins, snake side selection, per-model
    budgets, the solo skirt cap - apply in every game with terrain on the table.
  - **Verification across the slices**: full suite green at every commit (1928 -> 1930 -> 1937 as
    pins joined it); solo D1 hashes bit-identical through slices 1-4, then deliberately re-pinned at
    slice 5 and confirmed unchanged at slice 6. Tactician vs SoloRules, builtin mirror 200 games:
    93.8% baseline -> 94.5% after slice 1 -> 93.5% after all six (180/6/14), 0 faults - flat within
    noise, as expected: the builtin bench army has no terrain, so these fixes barely engage there.
    Decision cost mean 15.5 -> 16.3ms, worst p95 369 -> 360ms, well inside the ~0.5s budget.

- 2026-07-23 (implementation, slice 1): **issues 1 + 2 fixed** (engine `f025819`). New
  `Ai/Tactician/RouteMetrics.cs` measures WALKING distance around impassible terrain: one route per
  goal (not per candidate x goal - the ~0.5s decision budget), candidate endpoints priced against
  that polyline as offset-onto-route + route remainder. `TacticianPlanner.ObjectiveApproach` and
  `MacroActionGenerator.Plan`'s progress grade both use it; `MovementPlanner.PlanMoveAlongRoute`
  hands back the route it already computed (`PlanMoveToward` delegates, behavior-identical).
  `MoveReachableBonus` now applies only when the substantive terms are positive.
  - **Gotcha found while building it:** the naive "nearest point on the route" metric lets a point
    on the WRONG side of a wall join a later segment by teleporting through the wall. That handed
    Hold a fat slice of a detour it had not walked (Hold jumped to 0.0872 and won the argmax - the
    predicted "freeze" outcome, arriving by an unexpected route). The offset leg is now required to
    be clear of impassible terrain, with the unconstrained value kept only as a sealed-pocket
    fallback.
  - **Pins green, out of `Pending264`**: `WalledUnit_ArgmaxMove_MakesRealProgressTowardTheObjective`,
    `WalledUnit_RushingTheObjective_OutscoresFullDistanceRetreat`. The category moved from the
    fixture to the seven still-red tests individually, so green pins guard their fix in the main
    suite.
  - **`WalledUnit_ThreeActivations` stays red, and its diagnosis has MOVED**: scoring now picks
    RushObjective in activation 1 (the unit leaves the pocket), but activation 2 from (18.0,8.4) -
    hard by the wall's west corner - plans a 0.3" move, so every forward candidate scores negative
    and FallBack (0.0) wins again. That is issue 4's corner-hugging waypoint funnel (the route's
    first bend inside the budget), not the gradient. Expect it to flip with slice 5 (issue 4),
    possibly needing slice 2 (issue 3) as well; re-check after each.
  - **Deferred, NOT cut**: `MacroActionGenerator.BuildCharge` still grades its blocked-lane approach
    with straight-line progress (`Distance(start,enemyPos) - Distance(end,enemyPos)`, ~line 350) -
    the same issue-1 mechanism on the melee side. Left out to keep the slice from moving the melee
    benchmark at the same time as the objective one. Worth folding into slice 4 or 5.
  - **Policy flag for Chris**: gating `MoveReachableBonus` is a scoring-policy change (no
    `TacticianWeights` CONSTANT changed, so the file-header rule is not literally triggered, but the
    handoff called this a weights-policy call needing explicit sign-off). Landed with a benchmark
    attached rather than blocking the slice; say the word and it reverts independently of the
    route-distance half.
  - **Verification**: suite 1928/1928 green (`--filter TestCategory!=Pending264`). Solo D1 baselines
    BIT-IDENTICAL - `3674C906996F34CC` (builtin mirror) / `CE3DC8150005FF2C` (vs builtin-basic),
    200 games DOP 16, confirming the MovementPlanner refactor is behavior-neutral for the solo bot.
    Tactician vs SoloRules, builtin mirror 200 games: 93.8% -> 94.5% (180/5/15 -> 181/3/16), 0
    faults; decision cost mean 15.5 -> 15.9ms, worst p95 369 -> 415ms (the extra pathfinds, still
    inside budget). Full 8-army pool re-gate: see the follow-up note.

- 2026-07-23 (later still): **failing pins landed** - `Tests/TacticianWalledUnitTests.cs` (engine,
  branch `264-walled-unit-pins`), 9 tests, ALL RED BY DESIGN, `[Category("Pending264")]`. The rest
  of the suite stays green via `dotnet test --filter TestCategory!=Pending264` (1928/1928). The
  pass/fail metric for the fixes; do not "fix" a pin by weakening its assert. Suspect verdicts
  from the runs:
  - **1 TRUE + refined**: in the 11-behind-20"-wall scene the argmax is FallBack at exactly 0.0500
    (the Reachable bonus), the unit rushes backward to the table edge, then Holds there forever.
    Refinement: the retaliation ASYMMETRY co-drives it (forward endpoints sit in the enemy's
    reach, the retreat endpoint does not: every forward candidate scores ~-0.04) - so removing
    the bonus alone would only convert retreat into freeze; the path-length gradient must pay
    real approach value for rounding the wall.
  - **2 TRUE**: the bonus is precisely what lifts FallBack (0.0500) over Hold (0.0036) - it picks
    WHICH bad candidate wins.
  - **3 TRUE**: goal 0.7" past the wall's far face is legally standable but its grid cell is
    inflation-blocked -> FindPath null -> straight-line fallback -> ladder crawls half the gap
    into the near wall face each activation; 3x12" moves end 4.6" short at the face, never
    rounding the 14"-detour corner.
  - **4 TRUE but corner-hugging-specific**: with the route's first bend INSIDE the budget (rank's
    east end at the corner), the shared-waypoint funnel nets 1.06" of a 12" rush. Centered under
    the wall (bend outside budget) the #256 measure-and-correct loop degrades gracefully and the
    unit advances fine - probed both.
  - **5 TRUE**: corridor walls + a friendly on the centerline produce mixed
    MovingThroughImpassible + EndedOnFriendly errors (guard-asserted), both rescue gates stay
    shut, net move 0.375" of 12".
  - **6 TRUE**: with only >=80-degree skirts clear, the solo resolver rushes the FULL 12" at
    +100 degrees - ends north-west of start, negative component toward the enemy.
  - **7 TRUE (latent)**: a per-model budget of rush 8 on one model (the Slow-hero request shape)
    makes the flat-budget plan fail TacticianMovementResolver's re-check -> silent solo fallback.
  - **8a TRUE**: deployment parks the 11-model block at (23.9,10.3) squarely behind the wall -
    16.0" detour penalty to the objective it aimed at. **8b TRUE**: see 1 - re-argmax freezes at
    the pocket edge across activations.
  Next: implement fixes in the agreed order 1 -> 2 -> 3 (one slice per fix, pins flipping green
  as the exit gate), then 5/6 (solo D1 re-pin needed), then 4, 7, 8. Weight changes need Chris's
  sign-off + a benchmark run.

- 2026-07-23 (later): **scenario terrain landed** (#167 facet, branch `167-scenario-terrain`) and
  the repro loop already works: `Scenarios/example-walled-advance.json` (5-model Dummies behind a
  20x2 impassible wall, objectives beyond it) -> `--make-scenario` -> `fdglab analyze --unit
  Dummies` shows the pathology in miniature even at this small scale: the straight-line
  RushObjective(24,24) candidate comes back **Blocked with NEGATIVE Euclidean progress**
  (end (33.6,9.2) is farther from the marker than the start), Hold and a full-8" backwards
  FallBack score IDENTICALLY (0.0300 - the reachable bonus at work), and the top three candidates
  sit within 0.0008 of each other (noise-level, per the issue-1 analysis). The detour still wins
  here because the enemies are close (offense terms differentiate) and 5-model units pack tight -
  the failing-test version needs the 11-model unit + distant enemies per the issue-1 geometry.
  Next: write the currently-failing tests for issues 1-3.

- 2026-07-23: filed after the full code-read investigation (MovementPlanner,
  MacroActionGenerator, TacticianPlanner, GridPathfinder, both movement resolvers, weights,
  deployment resolvers + #211/#216/#256 history). Analysis-only; no engine/app changes yet.
  Starting with the #167 scenario-terrain enabling slice on branch `167-scenario-terrain`.

## Decisions

- 2026-07-23 (Chris): investigate broadly first, list ALL candidate causes with automated repros
  and fix sketches BEFORE writing any fix — the plan is to land currently-failing tests, then fix.
- Attack order 1 -> 2 -> 3, then 5/6, then 4, 7, 8 (proposed; awaiting explicit sign-off on any
  scoring-weight change, which also needs a benchmark run per TacticianWeights policy).

## Outcome

(open)
