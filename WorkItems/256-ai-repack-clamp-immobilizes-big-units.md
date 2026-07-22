# 256 — AI movement: repack clamp + stacking backoff immobilize big/clustered units

**Status**: done (GUI-verified by Chris 2026-07-22: "It did much, much better")
**Related**: #216 (solo-fallback residual), #211 (solo mover impassible), #191 (Tactician umbrella), #170 (deploy sibling)

## Goal

AI-controlled multi-model units (both solo-rules bot and Tactician - they share
`MovementPlanner`) must be able to actually spend their movement budget. Done = (a) a
spread-out combined unit advances close to its real Advance/Rush distance instead of 0-1",
(b) a unit whose formation-translate lands on a friendly re-aims around it instead of
halving toward zero, and (c) a walled-in cluster un-jams over a round or two instead of
staying wedged for the whole game; the WayTooManyInBack save's stuck units make real
progress toward objectives when re-analyzed.

## Evidence (2026-07-21, Chris's 4-player game: Chris = Machine Cults, 3 TacticalBots)

Save: `WayTooManyInBack.fdgsave` (Chris's desktop), round 3/4; screenshot
`WayTooManyInBack.PNG`. `fdglab analyze` + an instrumented `PlanMoveToward` replay
(rebuild: replicate PlanMoveToward, print `ValidatePaths` errors per ladder rung) found
three distinct stuck classes, all on bot-controlled units:

**1. Pure clamp immobility - no obstacles at all.** `CohesiveFormation.ClampRepackStep`
(`budget - currentSpread - gridRadius - 0.05`) zeroes big units' advances:
- Warriors (Combined), 11 models, 4" advance, open field: clamp 4.00 -> **0.00**. Every
  advance candidate toward every objective nets 0.12". Never advanced all game.
- Dwarf Warriors (Combined), 11 models, 4" advance: clamp 4.00 -> 0.09. Nets ~0.1".
- The formula is triangle-inequality worst-case (model at spread-distance behind the
  centroid travels to a slot at gridRadius on the far side); PackGrid's greedy
  nearest-model-to-slot assignment means a translation really costs ~step per model.
  An 11-model unit has gridRadius ~2", so any spread >= ~2" eats a 4" budget whole.
- Rush partially survives the clamp (8 - ~4 = ~4"), but Rush forfeits shooting, so the
  argmax takes Hold+Shoot and the unit stands forever.

**2. Corner jam - the trapped Battle Brothers pocket (bottom-left).** Battle Brothers at
(7.7,0.5): every ladder rung from 6" down to 0.09" fails "Ends stacked on top of a
friendly unit" (the other Battle Brothers unit + APC boxing them against the walls +
table edge - even the re-pack's own grid slots overlap neighbors), so the plan collapses
to hold-exact. The sibling Battle Brothers unit crawls 0.70"/turn (same stacking
failure), the APC caps at 2"/turn (impassible walls), so the pocket never drains.
Chris's point, correct: activation ORDER could drain it (move the APC/tank out first,
then the infantry) - the Tactician's activation-order score (kill opportunity /
objective flip / under-threat) has no "I am blocking friendlies" / "my mover is blocked"
term, so nothing sequences the un-jam. And the halving backoff can't side-step even when
a gap exists.

**3. Narrow corridors x wide formations.** Path-following candidates funnel every model
through shared waypoints; a wall-hugging route (GridPathfinder is centroid+radius, not
formation-width) makes flank models' segments clip impassible terrain -> "Moves through
impassible terrain" -> halved to sub-inch (Battle Brothers #1 toward (9,16): 0.45" of
a clear 3.10" post-clamp step).

Scoring is blind to all three: candidates come back labeled BudgetClipped/Blocked, the
ObjectiveApproach gradient pays by actual gap closed (~0 for a 0.1" move), so
shoot-in-place ties or beats every "advance". The visible backwards twitches are PackGrid
re-centering during sub-inch shuffles. (Chris's units - the Machine Cults cluster
bottom-right - show the same clamp numbers under analyze, but those were human-moved;
the bot evidence above is the authoritative set.)

## Plan (agreed 2026-07-21, slices in order; 1 first, 3/4 deferrable pending evidence)

- **S1 - measure-and-correct instead of the a-priori clamp** (engine, authorized): in
  `BuildCandidate` and `BuildPathCandidate`, build the pack at the desired step, measure
  the actual worst per-model move (full path length, matching ValidateOutOfMoveRange),
  and shrink the step by the overshoot for a couple of iterations (~1:1 step<->move
  response, same assumption as RefineStepTowardGap); the existing ladder catches any
  residue. Pin test: an 11-model unit in open field advances most of its budget.
  Verify against the save: `fdglab analyze` net moves jump from ~0.1" to real distances.
- **S2 - re-aim instead of halve on friendly stacking**: `ValidateWithBackoff` tries a
  few lateral offsets before shrinking the step; must preserve the G3 always-valid
  guarantee. (#216 family.)
- **S3 (deferred) - activation-order un-jam**: cheap version - deprioritize a unit whose
  best movement candidate nets < ~1" so neighbors activate first. Revisit after S1/S2
  evidence.
- **S4 (deferred) - corridor width**: inflate pathfinding radius near impassible terrain
  for wide formations, or single-file waypoint fallback.
- Also consider a stuck-detector in scoring (log when every movement candidate nets < 1").

Affects the solo bot too (`AiDefineMovementResolver` shares `BuildCandidate`) - fixing it
moves the pinned solo baseline (#191 D1 hashes). **Benchmark rerun deliberately deferred**
(low-power PC right now); the D1 pins are stale from S1 onward until it runs.

## Notes

- 2026-07-22 (evening, powerful desktop): **D1 benchmark rerun DONE** (engine `f7b6d78`). First run
  surfaced a REGRESSION-shaped fault (seed 1051, both swap sides: DefinePathStage "Ends stacked on
  top of a friendly unit") - traced with a temp stage/ladder diagnostic to a LATENT pre-#256 G3 gap
  the new trajectories exposed: the solo resolver's early-outs (all enemies dead - objectives keep
  the game going - or already-at-target) answered with an UNVALIDATED `StayInPlace` reform whose
  re-pack slot landed on an adjacent friendly. (The reform slot set is identical pre/post #256; the
  old baseline simply never wiped a side at those seeds.) Fixed: `MovementPlanner.StayInPlaceValidated`
  (reform validated, degrades to hold-exact) + the resolver early-outs use it; pin test copies the
  fault geometry (`Resolve_AllEnemiesDead_StandStillNextToFriendly_ResultIsEngineValid`). Suite
  1816/1816. **New D1 pins** (200 games, DOP 16, reproducible across duplicate runs, ZERO faults):
  builtin mirror `3674C906996F34CC` (29/29 wins, 142 ties; old `B05AA1D810364C6B` was 37/37/125 -
  slightly tie-heavier, still perfectly symmetric), builtin vs builtin-basic `CE3DC8150005FF2C`
  (40/25/135; old `F4318EF0D91161F5`). Hash change is EXPECTED - S1/S2/S4 deliberately moved the
  baseline. 14.3 games/s on the Threadripper (old note: 5.25). MovementPlanner's doc comment updated
  to the new hashes. **Remaining residual: only Chris's GUI session.**

- 2026-07-22 (later still): **real-save re-probe done; S3 refuted by evidence; S4 landed.** Chris
  dropped `WayTooManyInBack.fdgsave` in the repo root (untracked). Real-save S2 verification:
  Warriors (Combined) advances 3.2-3.4" + ChooseAction Move (was 0.12" + shoot-in-place), Dwarf
  Warriors 3-4", Battle Brothers #2's best advance 5.4". Forward-driving the save all-AI (scratchpad
  DriveProbe: ScenarioLauncher-style resume, Tactician on every slot) first hit the #258 sniper
  crash (fixed, see that item), then showed rounds 3-4 drain everything EXCEPT the walled Battle
  Brothers - which stayed frozen even after every blocker vacated. End-state rung probe: every arc
  toward both objectives failed ONLY MovingThroughImpassibleTerrain - stuck class 3 (corridor
  width), NOT activation order. **S3 decision: refuted as the binding constraint, not built** (the
  cheap deprioritize-jammed-movers version would not have freed the unit). S4 probes: a straight
  end-line column still clips (tail sticks through walls when the path bends); per-model A* repair
  fails because the packers place flank SLOTS inside walls (unreachable goals); the on-path snake
  (destinations ON the pathfound polyline, staggered by base-width) validates at arc 3 where the
  grid pack needed 0.19". Landed as `BuildSnakeCandidate` + an impassible-only ladder fallback
  (mirrors the S2 re-aim gate; head must thread >= half the step + real centroid progress; >8-model
  units wrap into parallel files under the 9" rule). Re-driven on the real save: pocket DRAINS -
  BB#2 (7.7,0.5) -> (14.7,19.3), tank parks ON objective (9,16), APC out to (28.5,9.2), the walled
  BB moving out at (8.1,4.8) and its end-state snake rungs valid from arc 6 down. Verified:
  1815/1815 green (2 new pins: corridor-narrower-than-formation snakes through > 3" head progress;
  11-model snake wraps files and stays cohesive), full build, smoke exit 0. Remaining: #191 D1
  benchmark rerun (still deferred, low-power PC) + Chris's own GUI session as the human-eye check.

- 2026-07-22 (later): **S2 re-probed and tuned.** The original `WayTooManyInBack.fdgsave` is NOT on
  this machine (full-disk + old-drive search: zero .fdgsave anywhere), so the re-probe ran on a
  reconstruction: `Scenarios/256-friendly-in-lane.json` (committed) - two spread 11-model Warriors
  units, one with a friendly APC parked ~6" up its advance lane, `fdglab analyze` + a recreated
  instrumented rung-replay (scratchpad-only, per the handoff note). Findings, each fixed in-session:
  (1) as-landed, the re-aim NEVER fired on the reconstruction - the +/-1/+/-2-base-width offsets all
  clipped the blocker because an 11-model pack is ~1.7 widths from center to edge (blocked advance:
  3.1" via one halving vs 4.7" control); (2) naively widening the schedule to +/-3/+/-4 made it WORSE
  (0.1" stays): stacking the offset on top of the full step made the measure-and-correct loop absorb
  the whole lateral cost, blow the budget, and degrade to a valid-but-useless StayInPlace which the
  ladder happily returned. Fixes: (a) a forward-progress gate in TryLateralReaim (accept only >= half
  the blocked candidate's forward progress - never trade an advance for a stay); (b) probe at
  forward = sqrt(step^2 - lat^2), trading forward for lateral INSIDE the budget circle; (c) offset
  schedule densified to half-width steps past 2 (probe showed the clearing window can be < half a
  width wide: 2.2 collided, 2.8 cleared); (d) RepackCorrectionAttempts 4 -> 8 - with a lateral
  offset the step<->move response flattens and pairing flips bump the measure mid-descent, so 4
  attempts gave up on feasible side-steps (engine-wide constant; centered candidates converge in <= 2
  attempts, so S1 behavior is unchanged). Result on the reconstruction: blocked advance 3.11" -> 
  **4.40" with a 2.8" side-step** (control 4.71"), candidate score 0.0409 -> 0.0557; clean-lane unit
  byte-identical. Verified: 1811/1811 green, full build, headless smoke exit 0. The committed
  scenario + `fdglab analyze` is now the cheap re-probe loop; re-running against the REAL save when
  it's copied over remains open (and the walled Battle Brothers pocket still wants S3/S4).

- 2026-07-22: **S2 landed** (engine, app-side pointer bump pending): `ValidateWithBackoff` now
  side-steps the pack anchor before halving when a candidate's SOLE fault is ending stacked on a
  friendly (`EErrorReasonType.EndedOnFriendlyUnit`). Mechanics: `BuildCandidate` gained a
  `lateralOffsetInches` param that shifts the anchor perpendicular to the move (`(-ndz, ndx)`); the
  ladder, at each rung where friendly-stacking is the only error, probes offsets of +/-1 and +/-2
  base widths (nearest-first, alternating sides) at the SAME step and returns the first that
  validates. The measure-and-correct loop absorbs the extra travel (a side-step trades forward
  advance for clearance, never exceeding the per-model budget), so the G3 always-valid fallbacks are
  untouched. Wired at ALL THREE ladder call sites: the two straight-candidate ones (solo
  `AiDefineMovementResolver` + Tactician charge in `MacroActionGenerator`) and `PlanMoveToward`'s
  path candidate (`BuildPathCandidate` gained the same param; the offset shifts the endpoint fan-out
  anchor perpendicular to the path's FINAL segment while the funnelled waypoints stay on-route).
  The path-candidate wiring matters most: the Tactician's objective advances - the actual
  Warriors-toward-(7,30) acceptance row - route through PlanMoveToward even in an open field (trivial
  2-point path), so a straight-candidate-only re-aim would have missed the headline case (caught
  in-session before push; an earlier draft of this note wrongly deferred it to S4 - S4 remains only
  the corridor-WIDTH problem, mid-path clipping of wide formations). A "sole obstacle" gate keeps
  every non-stacking case byte-identical, so the solo-bot behavior pins are unaffected (benchmark
  rerun still deferred per S1). Verified: 1811/1811 engine tests green (2 new pins:
  `ValidateWithBackoff_FriendlyBlocksCenteredAdvance_SideStepsAndKeepsAdvance` - 6-model unit, centered
  4" advance lands on a friendly, re-aims to >2.5" net with a real lateral component, within budget;
  `PlanMoveToward_FriendlyOnArrivalSpot_SideStepsAndKeepsAdvance` - same via the path route end-to-end,
  where pre-fix the ladder halves to ~1"), full `dotnet build` clean, headless smoke exit 0.
  **Save-level re-probe deferred**: `WayTooManyInBack.fdgsave` + screenshot live on the OLD desktop,
  not copied to this machine yet - the S2/S3 acceptance rows (Warriors toward (7,30): 4" -> was
  halved to 0.96"; the Battle Brothers corner pocket) still want a `fdglab analyze` re-run once the
  save is here. Remaining: the walled Battle Brothers pocket needs S3 (activation order) and/or S4
  (corridor width) - a lateral side-step alone can't drain a fully boxed cluster.

- 2026-07-22 (handoff, Chris switching machines): renumbered 254 -> 256 (reconciliation 18;
  origin's #254 wound-morale + #255 lobby-team landed first). Everything S1 is pushed on
  both masters. **Next: S2** - re-aim instead of halve on the "Ends stacked on top of a
  friendly unit" ladder failure, in `MovementPlanner.ValidateWithBackoff` (try a few
  lateral offsets of the pack anchor before shrinking the step; keep the G3 always-valid
  guarantee). Verification assets live on the OLD machine: the parked save
  `WayTooManyInBack.fdgsave` + screenshot on the desktop - copy them over to re-run the
  cheap loop (`fdglab analyze <save>`, plus the instrumented-replay probe described below;
  its source was scratchpad-only, recreate by replicating PlanMoveToward and printing
  ValidatePaths errors per ladder rung). S1's save-level numbers to beat are in the S1
  note; the friendly-stacking rows (Warriors toward (7,30): 4" -> halved to 0.96") and the
  Battle Brothers corner pocket are the S2/S3 acceptance cases.
- 2026-07-22: **S1 landed** (engine `eb38407`): BuildCandidate/BuildPathCandidate now pack at
  the desired step, measure the actual worst per-model path length (the ValidateOutOfMoveRange
  metric), and shrink by the overshoot (4 attempts, then StayInPlace). Two pairing pathologies
  surfaced by the measure loop and fixed with a bottleneck-2-opt cleanup (`ImprovePairing`,
  also applied to StayInPlace): (a) the packers' greedy nearest-model-to-slot assignment
  REVERSES rank order once the step exceeds grid spacing; (b) fixed rank-pairing (first
  attempt) forced step-independent cross-row leaps when current vs canonical row counts
  mismatch (4/4/3 vs 3/4/4), which never converged. Criterion is worst-move-first, sum as
  tie-break - sum-first re-broke the Block line test by inflating the max.
  Verified: 1795/1795 engine tests green (3 new pins: BuildCandidate / BuildPathCandidate /
  PlanMoveToward, 11-model open-field advance > 3" of 4"), headless smoke exit 0, and the
  WayTooManyInBack save re-probed - Warriors (Combined) 0.12" -> 3.1-3.6" per advance,
  Dwarf Warriors 0.1" -> 3.9-4.0" on clear directions. Remaining crawl rows are friendly-
  stacking halvings (S2) and the walled Battle Brothers pocket (S2+S3/S4), as planned.
  A first-move formation-canonicalization toll (~1.5" for a 3-wide -> 4-wide reshape) is
  intrinsic and accepted; units cruise at ~full budget from the second move.

- 2026-07-21 (later): corrected player attribution - the first pass probed Chris's own
  Machine Cults units by mistake; re-probed the three bot players and identified the three
  stuck classes above (the trapped Battle Brothers, and the never-advancing Warriors /
  Dwarf Warriors, matching Chris's report). Added the activation-ordering fix element per
  Chris's suggestion.
- 2026-07-21: filed from the WayTooManyInBack investigation (analyze dump + instrumented
  ladder replay; probe source lived in the session scratchpad, trivially recreatable).

## Decisions

- 2026-07-22 (Chris): evidence-first bar for S3 - only build it if the pocket demonstrably fails to
  drain under S1+S2. The forward-run showed the walled unit stays stuck for corridor-width reasons
  with all blockers gone, so S3 was refuted and S4 built instead.

## Outcome

All three stuck classes fixed; **GUI-verified by Chris 2026-07-22** ("It did much, much better").

- **S1** - measure-and-correct candidate budgets replaced the worst-case repack pre-clamp
  (+ bottleneck-2-opt pairing cleanup): big combined units spend ~their full budget (engine
  `eb38407`). Real-save numbers: Warriors 0.12" -> 3.2-3.4", Dwarf Warriors 0.1" -> 3.9-4.0".
- **S2** - re-aim instead of halve when ending stacked on a friendly is the sole ladder fault:
  budget-circle side-step (forward = sqrt(step^2 - lat^2)), half-width offset schedule to 4 base
  widths, forward-progress gate, RepackCorrectionAttempts 4 -> 8; wired at all three ladder call
  sites (engine `9679a58`, `64131b2`, `356df03`). Reconstruction scenario committed
  (`Scenarios/256-friendly-in-lane.json`): blocked advance 3.11" -> 4.40" (4.71" control).
- **S3** - refuted by evidence, not built: forward-driving the real save showed the walled unit
  stayed stuck AFTER every blocker vacated - corridor width, not activation order, was binding.
- **S4** - on-path snake fallback for corridors narrower than the formation (destinations ON the
  pathfound polyline, parallel files past the 9" rule; engine `19ce09e`). The WayTooManyInBack
  pocket drains over rounds 3-4: tank parks on objective (9,16), both infantry units out/moving.
- **D1 benchmark re-pinned** (engine `f7b6d78`): builtin mirror `3674C906996F34CC`,
  builtin vs builtin-basic `CE3DC8150005FF2C` - zero faults, reproducible at DOP 16 (recorded in
  #191). The rerun also caught + fixed a latent G3 gap (unvalidated stand-still early-outs).
- Spun off and closed along the way: **#258** (rule-definition identity broke every
  `Definition ==` check on resumed saves; root-fixed as name equality).

Follow-ups live elsewhere: #211 (solo impassible leak), #216 (Tactician solo-fallback drift),
#210 (dop>1 bench nondeterminism).
