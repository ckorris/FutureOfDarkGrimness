# 389 — Tactician: walled-in rear shooter activates on a phantom volley, then slides laterally along the back edge

**Status**: filed (2026-08-30; investigation complete, fix awaiting Chris's sign-off - every
candidate fix is a scoring/urgency policy change under the TacticianWeights benchmark gate)
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

- 2026-08-30: filed after the save-file investigation (fdglab analyze + temporary per-term
  instrumentation in TacticianPlanner.Score / TacticianActivationResolver.Urgency, reverted).
  Analysis only; no engine changes.

## Decisions

(awaiting Chris's pick of fix candidate(s))

## Outcome

(open)
