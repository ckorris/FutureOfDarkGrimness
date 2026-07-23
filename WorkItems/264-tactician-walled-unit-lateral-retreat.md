# 264 — Tactician: unit behind impassible terrain rushes sideways/backwards instead of advancing

**Status**: in-progress (investigation complete 2026-07-23; fixes not started)
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

## Notes

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
