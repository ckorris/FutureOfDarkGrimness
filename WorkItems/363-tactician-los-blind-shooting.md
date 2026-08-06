# 363 — Tactician is line-of-sight-blind: phantom volleys through walls

**Status**: in-progress
**Related**: #191 (Tactician umbrella), #360 (move-penalty pricing seams), #361 (charge
targeting blind spots - same estimate-vs-engine family)

## Goal

The Tactician stops crediting shooting it cannot legally take. Concretely: (1) the planner's
offense term prices a candidate's volley at zero when Blocking terrain cuts the sight line
from the candidate endpoint to the enemy; (2) the EngageAtRange generator, when its
straight-line band endpoint is LoS-blocked, looks for a nearby endpoint at the same band
distance with a clear lane, so "step around the wall and actually shoot" exists as a
candidate. Pinned by a scenario reproducing the source save's corner-wall setup.

## Notes

- 2026-08-05 (later): facets 1+2 implemented + verified (engine 8c875a7). AttackContext gains
  `SightBlocked` (default false - every other call site unchanged); EstimateShooting skips
  LoS-bound weapons when set, exempting Indirect via `SightRuleQueries.IgnoresTerrain` per
  weapon; the offense term feeds it a centroid-to-centroid `LineOfSightUtilities.HasLineOfSight`
  test against a per-activation terrain snapshot. `MacroActionGenerator.ClearLaneGoal` rotates a
  blocked band goal around the target in 15-degree steps (up to 90 each way, deterministic
  order) to the first on-table clear-lane sample. Pins: `TacticianLineOfSightTests` (5) -
  estimate silenced/Indirect-exempt, generator side-steps, planned move ends on a clear lane,
  clear-lane engage outscores shadow-Hold. Suite 2898 green. Scenario
  `Scenarios/363-wall-shadow-engage.json` (+ 363-Gunline/363-Targets signal armies - the
  Marksmen/Dummies pair carries ONE rifle per unit, too weak to demo shooting value): Gunline
  side-steps 6" right of the wall and wipes the target unit; Hold-in-shadow prices 0.0000.
  Save-replay acceptance (rewound unit 7): the phantom-propped advance drops +0.2220 -> -0.1789
  and the squad rushes 12" toward objective (15,20) instead - honest, since the "lost" shot vs
  2+-save-in-cover Revenants was worth ~0. Visible in that table: engage endpoints near the wall
  price NEGATIVE because retaliation still sees through walls - facet 3's cowering bias, live.
- 2026-08-05: filed from the `BattleBrothersJustMovedAShortDistanceAndDidntShootWhy.fdgsave`
  analysis. Reproduction: rewind the save (clear the squad's ActivatedThisRound/
  MovedThisRound tokens, re-add to UnactivatedUnits), replay `--headless --scenario ...
  --all-ai --log-decisions --ai-profile tactician`. The squad at (52.3,8.6) picks
  `AdvanceOnObjective end=(49.7,14.0)` at 0.2220 over Hold/EngageAtRange at 0.2006, then
  the engine logs "No actions available - passing": no Shoot stage ever opens.
  - Ground truth (mirrored the engine's 2D LoS math, rotations included): the only enemy
    within 24" (Revenants, ~19") is LoS-cut by the rotated "Corner wall" piece from EVERY
    model of the squad at BOTH the start and the endpoint; all other enemies are out of
    range. The engine's Shoot gate (`HasAnyFireableTarget`) is correct.
  - Root cause: `CombatMath.EstimateShooting` drops a weapon only when
    `reach < context.DistanceInches` - AttackContext carries no geometry beyond distance,
    so terrain never enters any planner estimate. `MacroActionGenerator`'s EngageAtRange
    endpoints are band-distance points on the straight line to the enemy - also LoS-blind.
  - The kicker: clear firing lanes existed ~4.7-5.2" away (e.g. (55.0,12.5), clear LoS to
    a Revenant at 20.3") - a legal Advance-and-shoot the planner structurally cannot find.

## Decisions

- LoS checks are centroid-to-centroid segment tests against Blocking terrain, matching the
  scorer's centroid altitude everywhere else. Cheap (~candidates x enemies segments), and
  the approximation errs the safe way: it may undervalue a marginal per-model shot, but it
  can no longer walk into a wall's shadow expecting one.
- **Deferred facet (recorded, not silently cut): retaliation/projected-threat symmetry.**
  Incoming-fire estimates (`Score`'s retaliation term, projected threat, escort valuation)
  still see through walls, so after this item threat is overestimated exactly where cover
  is best - a mild cowering bias. Fixing it flips sign on a much broader behavior surface
  and deserves its own benchmark pass.

## Outcome

(open)
