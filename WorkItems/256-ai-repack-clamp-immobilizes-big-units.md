# 256 — AI movement: repack clamp + stacking backoff immobilize big/clustered units

**Status**: todo
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

- 2026-07-22: **S2 landed** (engine, app-side pointer bump pending): `ValidateWithBackoff` now
  side-steps the pack anchor before halving when a candidate's SOLE fault is ending stacked on a
  friendly (`EErrorReasonType.EndedOnFriendlyUnit`). Mechanics: `BuildCandidate` gained a
  `lateralOffsetInches` param that shifts the anchor perpendicular to the move (`(-ndz, ndx)`); the
  ladder, at each rung where friendly-stacking is the only error, probes offsets of +/-1 and +/-2
  base widths (nearest-first, alternating sides) at the SAME step and returns the first that
  validates. The measure-and-correct loop absorbs the extra travel (a side-step trades forward
  advance for clearance, never exceeding the per-model budget), so the G3 always-valid fallbacks are
  untouched. Wired at both straight-candidate call sites (solo `AiDefineMovementResolver` +
  Tactician charge in `MacroActionGenerator`); `PlanMoveToward`'s path candidate passes no reaim
  (corridor width is S4). A "sole obstacle" gate keeps every non-stacking case byte-identical, so the
  solo-bot behavior pins are unaffected (benchmark rerun still deferred per S1). Verified: 1805/1805
  engine tests green (new pin `ValidateWithBackoff_FriendlyBlocksCenteredAdvance_SideStepsAndKeepsAdvance`:
  a 6-model unit whose centered 4" advance lands on a friendly re-aims to keep >2.5" net move, still
  within budget, with a real lateral component), full `dotnet build` clean, headless smoke exit 0.
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

(none yet)

## Outcome

(open)
