# 389 — Tactician: walled-in rear shooter activates on a phantom volley, then slides laterally along the back edge

**Status**: options 1 AND 2 implemented + tested (2026-08-31, Chris's picks); pool gate legs in
flight - see the dated notes for the mechanism revisions found during implementation
**Related**: #296 (crowded-game drift - the frontline-bias half), #359 (lane clearing - closed with
"option 3 friendly-aware routing to be filed fresh if Chris's big games still show it"; they do),
#264 (walled-unit umbrella - the terrain sibling of this friendly-wall case), #191 (Tactician umbrella)

## Goal

In a crowded 2v2, a rear gunline unit walled in by friendlies either waits for the wall to clear
(activation order) or makes real forward progress when it must move - never a full-budget lateral
slide along its own back edge. Done = the WarriorSisters save shape picks a sane activation order
and a sane move, without regressing the pool benchmark.

## Evidence (2026-08-30, Chris's GUI game - `WarriorSistersMovedLaterally.fdgsave`, repo root)

2v2 (Sisters + Elementals vs Orcs + Vehicles), round 1, 48"-deep table, save parked right after the
move. Warrior Sisters (11 models + joined Celestial hero, `ActivatedThisRound + MovedThisRound`)
ended at (47.4,45.8) - flush against their own back edge - after moving sideways along it. The
Fanatic Sisters at (50.8,40.4), directly on their forward lane, had NOT yet activated; nor had the
teammate's Great Elemental (54.5,40.3). Chris: "they just moved sideways along the back, which
doesn't buy them anything... they have other units that could've moved out of the way."

## Findings (measured via `fdglab analyze` + temporary per-term Score/Urgency instrumentation)

Two mechanisms compound, one per decision layer:

### 1. Activation order: the urgency kill term prices a volley the unit cannot take

`TacticianActivationResolver.Urgency`'s kill term prices "advance straight at the enemy and shoot
at best range": `reach = MinDistanceBetweenUnits - advance`, congestion- and legality-blind, from
the CLOSEST MODEL's base edge. For the walled sisters that read `dist=25.7 - 6.0 = 19.7"` vs the
Light Walker -> 3.00 expected wounds -> kill=0.8124 -> urgency **1.0027**. The Fanatic Sisters
(melee, kill term only prices shooting) scored **0.1130**. So the DEEPEST unit on the team
activated second, while its lane-blockers waited; `ActivationFrontlineBias` (0.1, built in #296
exactly against this ordering) cannot bridge a 0.89 urgency gap - #359's measurement worry ("may be
swamped exactly when needed") confirmed in the wild.

The volley is phantom: the movement planner then found **offense = 0.0000 at every reachable
endpoint** - the friendly wall caps actual closure at ~3" and every endpoint stays out of
range/sight. The urgency and the planner disagree about the same physics.

### 2. Movement argmax: clipped forward stubs keep full risk, the lateral slide dodges it

Full decomposition of the post-move argmax (same shape as the observed move; scores = winner then
best forward alternative):

- **Escort ally Great Elemental -> (52.5,44.4), 0.0670 - WINNER, a ~5" eastward slide along the
  back edge.** Terms: proj=-0.038, cover=+0.050, objAppr=+0.042, appr=+0.066.
- RushObjective obj(42,24) -> (40.5,42.6), 0.0170. Terms: proj=-0.108, cover=+0.026,
  objAppr=+0.061, appr=+0.092.

The forward candidates are all BudgetClipped to ~3-5" stubs by the friendly wall (routing grid
holds terrain only - #359's deliberately-deferred "option 3"). The clip shrinks their approach
credit toward zero, but they still pay the FULL projected-threat premium (+0.070 relative) and
cover-habit loss (+0.024) for standing nearer the guns. Meanwhile the lateral Escort slide (goal:
the interpose point in front of the teammate's Great Elemental; clipped, so the executed move is
just the sideways stub) dodges the premium AND still collects objective-approach credit, because
sliding east genuinely closes straight-line x-distance to the unowned objective at (51,22). Risk
asymmetry on clipped stubs > clipped reward: the #264 issue-1 refinement ("removing the bonus alone
converts retreat into freeze") reappearing in friendly-wall form.

The same save shows it about to repeat: the OTHER Warrior Sisters at (27.0,45.0) - every
substantive candidate Blocked AT ITS OWN CENTROID - carries urgency 0.3911 (kill=0.2053, another
phantom volley) vs the Fanatic Sisters' 0.1130, so the next pick is again a walled rear unit.

## Fix candidates (all policy-gated; none built)

1. **Ground the urgency kill term in reachability** - cheapest, attacks the ordering half. E.g.
   cap the assumed closure by a cheap congestion probe (is the straight lane to the enemy clear of
   unactivated friendlies for the first advance-length?), or price kill at the CURRENT position
   plus a discounted advance. Kills the phantom 0.81 -> the Fanatic Sisters activate first, clear
   the lane, and the sisters' later move has room. Risk: underpricing genuinely open shooters;
   needs the pool gate.
2. **Risk symmetry for clipped candidates** - scale the projected-threat/cover premium by actual
   distance moved toward it, or charge the lateral slide the same premium at its own endpoint (it
   is barely farther from the guns). Attacks the argmax half directly.
3. **Option 3 from #359: friendly-aware routing/staging** - stamp unactivated friendly bases into
   the routing grid (or retarget to the nearest reachable staging point when the lane is jammed).
   The structural fix for every "flanks as congested as the lanes" shape; the most work and the
   most benchmark risk.

Recommendation: 1 first (small, measurable on this save via `fdglab analyze`, pool-gated), then
re-observe; 2/3 only if the shape survives.

## Notes

- 2026-08-31 (cont. 3): **Clean gate landed - numbers of record (post-#392 lineage: per-game
  armies, Server GC, dop 24, Release; 1920 games/leg, seeds 1000+, ~190s/leg).**
  - baseline-v2, engine `a64ef2f`+`16977e7`: matrix **88.3** / mirrors 88.5 / worst 61.7
    (BB-vs-Hives) / 0 faults; hash `2B54D2A0D7C20367`.
  - opt1-v2, `bac1e8d`+fix: matrix **85.7** / mirrors 85.2 / worst 58.3 (DG-vs-HEF);
    hash `B57ACC5373969000`.
  - opt12-v2, master `16977e7`: matrix **86.6** / mirrors 86.7 / worst 60.0 (BB-vs-Hives) /
    0 faults; hash `F72A403CBD50EAF7`.
  Read: the shipped pair costs ~1.7 matrix points vs this baseline, option 2 recovering ~0.9 of
  option 1's ~2.6 dip. A-gate automated criteria pass on every leg (aggregate >= 70, NO cell
  below 50 anywhere). WATCH ITEM: the dip concentrates against HORDES - biggest moves
  baseline->shipped are DG-vs-Orks -20 (82->62), BB-vs-Orks -15, HDF-vs-Orks -15,
  DE-vs-Hives -11.7 - consistent with the #384-aware kill term being over-cautious about
  sight through 30-model mobs (the per-model shoot stage often finds the shots the
  closest-model ray does not). If the next hand-played games or gate show the Orks rows
  degrading further, the first lever is softening the urgency sight gate against horde
  targets (partial factor instead of binary), not reverting the grounding. Accepted on
  behavior: the save's pathology (both walled units, both armies) is measurably gone.
- 2026-08-31 (cont. 2): **Gate legs invalidated by #392 and rerun on the fixed stack.** The
  first baseline (hash `0ABE7B5AAB4E3440`, matrix 85.7 / mirrors 88.1 / worst 53.3) and the
  option-1 leg (matrix 85.7 / mirrors 85.6 / worst 58.3) ran on the harness whose concurrent
  games coupled through shared army files (#392, found mid-gate during Chris's bench-speed
  question) - an engine change shifts setup interleaving exactly like the perturbations that
  flipped 7/16 repro games, so those legs cannot separate engine effect from contamination.
  Recorded here as old-lineage datapoints only. All three legs (pre-#389 `a64ef2f`, option 1
  `bac1e8d`, option 1+2 `27e2ffe`, each with the #392 engine fix `16977e7` applied) rerun on
  the fixed harness (per-game armies, Server GC, dop 24) - numbers of record below when they
  land.
- 2026-08-31 (cont.): **Option 2 built - the band-edge cliff is now a ramp; the historical slide
  argmax turns out to be mostly HONEST pricing.** The arriving-pressure forecast
  (`TacticianPlanner.Score`, #191 idea 2) had a hard band edge: an endpoint one inch beyond an
  enemy's projected reach paid ZERO, one inch inside paid FULL. `ArrivalRamp` (internal static,
  directly pinned) now decays the SHOOTING forecast linearly over one more enemy advance beyond
  the edge - a forecast two moves out arrives half an advance later, not never; inside the band
  nothing changes. Two scope decisions, both deliberate: (1) the MELEE forecast keeps its hard
  edge - ramping it broke the #365 corridor pin
  (`Score_CoveredSideWithinChargeReach_LosesToOpenSideOutOfIt`): the charge arc is a categorical
  boundary (outside it the enemy cannot charge next activation), and stepping just outside it
  must stay worth something; (2) no smoothing INSIDE the band. On the save, an A/B table diff
  shows the ramp re-pricing exposed endpoints across many units (0.005-0.03 shifts), but the
  slid sisters' Escort-vs-RushObjective gap is UNCHANGED - per-term decomposition (temporary
  SCORE_DEBUG dump, stripped): slide proj=0.2342/cover=1.0000 vs stub proj=0.6637/cover=0.5135,
  both proj values identical pre/post ramp, i.e. both endpoints sit INSIDE arriving bands. The
  residual gap is honest: the closer stub genuinely faces more of the arriving guns
  (inside-band weapon steps), and the slide's cover=1.0 is the #384 friendly-base shadow - per
  official rules, standing behind your own wall IS safer. The pathological half of the save
  (activating the walled unit early on a phantom volley) is option 1's kill. If big games still
  show back-edge slides, the next lever is per-weapon range falloff inside the forecast
  (CombatMath-level - bigger blast radius, needs its own sign-off). Tests:
  `TacticianArrivalRampTests` (4: 3 ramp pins + a Score-level ordering pin, red-checked against
  the restored cliff). Suite 3089/3089 green, full build clean, smoke exit 0.
- 2026-08-31: **Option 1 built - and the phantom's true mechanism revised mid-implementation.**
  The closure cap alone turned out NOT to kill this save's phantom: re-measurement showed the
  walled sisters' lane free run is 2.27" (matches the observed ~3" formation clip), but their
  24" rifles at 25.7" need only 1.7" of closure - the capped reach (23.4") stays in range, so
  pure distance never zeroes the volley. What actually zeroed the planner's offense was SIGHT:
  the #363/#384 gate (Blocking terrain + other friendly units' bases under official rules) that
  `TacticianPlanner.Score` has carried since #363 ("the phantom volley through a wall... can no
  longer be credited") but the urgency kill term never got. The shipped fix grounds the kill
  term in BOTH: (1) `TacticalAnalysis.FreeStraightAdvance` caps the assumed closure at the
  farthest point on the straight lane where the closest model could legally END a move (#205:
  friendlies never block passage, only standing - so a thin screen with room behind costs
  nothing, a deep mass caps at its near edge; swept-circle spans, merged, farthest-free-below-
  budget); (2) the volley is sight-tested with the planner's exact blocker set from the point
  that closure reaches (`AdvancedBy` - a screen the mover can step past must not block the
  priced shot), via a new `seeThroughFriendlyUnits` ctor flag wired from `TacticianOptions` in
  the registry factory. Threat term deliberately left congestion/sight-blind (over-estimates
  incoming = conservative); flip term untouched; simplifications recorded: circle approximation
  (bounding radii), closest-model lane (single-model standing room over-estimates an 11-model
  formation's real closure - measured 2.27" vs ~3" observed, same side of the range line here).
- 2026-08-31: **Measured on the save (`fdglab analyze --urgency`, now a permanent flag):** the
  slid Warrior Sisters 1.0027 -> 0.1903 (walker/APC/buggy volleys all sight-blocked; remainder
  is the honest under-threat term); the Elementals' walled Retributors 1.0676 -> 0.2552, its
  army's pick flipping to the frontline Elemental Strikers (bias-decisive) - the second
  rear-first ordering on the board fixed by the same change. The OTHER Warrior Sisters keep
  0.3911: their Assault Buggy lane (24.9", free 6.0") is open AND visible - a real volley,
  correctly still priced. Tests: `TacticianPhantomVolleyTests` (9: 4 FreeStraightAdvance
  geometry pins, 4 urgency pins incl. the in-range-but-sightless save shape and the step-past
  screen guard, 1 activation-pick pin of the headline ordering); red-check confirmed the 4
  discriminating pins fail on the pre-fix resolver. Engine suite 3085/3085 green (+9), full
  build clean, headless smoke exit 0.
- 2026-08-30: filed after the save-file investigation (fdglab analyze + temporary per-term
  instrumentation in TacticianPlanner.Score / TacticianActivationResolver.Urgency, reverted).
  Analysis only; no engine changes.

## Decisions

- 2026-08-31 (Chris): "Okay, let's do 1 first." - option 1 (ground the urgency kill term in
  reachability), options 2/3 held pending re-observation.
- 2026-08-31 (Chris): "Sure, let's do that" - option 2 as the band-edge ramp (recommended
  variant), sequenced as its own commit + its own bench leg after option 1's, so the two remain
  independently attributable and revertible. Option 3 still held.
- 2026-08-31 (build-time): melee forecast edge stays HARD (the #365 corridor pin is
  load-bearing); ramp applies to the shooting forecast only.

## Outcome

(open)
