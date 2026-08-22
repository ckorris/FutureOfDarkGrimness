# 363 — Tactician is line-of-sight-blind: phantom volleys through walls

**Status**: closed 2026-08-06
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

- 2026-08-06 (BlockedThreatShare tuning, 0.2 vs 0.4): **0.2 measures mildly BETTER, but the pool
  cannot settle it.** Same 640-game pool/seeds/options as the gate, both runs on one fresh Release
  binary, sequential; `--weights BlockedThreatShare=<v>`. Reports:
  `FdgLab/reports/363-gate/{share04,share02}/`.

  | run | share | score | vs 0.4 | vs pre-#363 control |
  |---|---|---|---|---|
  | share04 | 0.4 (shipped default) | 84.84% | - | -0.86pp |
  | share02 | 0.2 | 85.62% | **+0.78pp** (z +2.24) | -0.08pp |

  - share04 reproduced the recorded facet-3 run EXACTLY (same score, same W/T/L, zero flipped
    games) despite being a Release build against facet3's Debug one - so config is outcome-neutral
    here and the 0.2 pairing is against a clean same-binary control. (Timing is not comparable
    across those two: Release runs 45.6ms/decision and 18.4s/game vs Debug's 65.2/26.0.)
  - Only 17 of 640 games flip between 0.2 and 0.4 - a far smaller perturbation than adding the
    sight gate at all (83 flips). Breakdown: 10 tie->win, 1 loss->win, 4 win->tie, 2 loss->tie;
    net +5.0 game-equivalents. Sign test 13/17 one-sided p=0.025, two-sided p=0.049 - REAL but
    borderline, and not a pre-registered hypothesis. 0 faults in both runs.
  - At 0.2 the whole facet-3 cost disappears: -0.08pp against the pre-#363 control (z -0.08), i.e.
    indistinguishable from the baseline, where 0.4 sat -0.86pp below it.
  - **Direction matters: LOWER share = blocked incoming fire counts for less = MORE cover-seeking.**
    So this leans against the prior recorded in the Decisions section below ("a hard zero would
    invent perfect hard cover and teach the whole army to hug walls"). It does not refute it -
    0.2 is not 0.0 - but the shipped 0.4 is not the measured optimum on this pool.
  - **Not retuned on this evidence, deliberately.** The pool's maps are nearly LoS-free (see the
    terrain audit below), so 17 flips is what "a handful of wall situations per game" looks like;
    borderline significance on a near-null instrument is not a mandate. The tuning question is
    reopened properly by a terrain-dense pool, not by more games on this one. 0.6 (and 0.0, which
    would test the prior head-on) not run - deferred, not dropped.

- 2026-08-06 (terrain audit - why this pool under-measures the fix): the bench uses
  `GameSettings.GetDefault()`, i.e. `AutoFromLayout` over `DefaultTerrainPool`. Of its 6
  Blocking|Impassible pieces, `PlaceTerrainStage.RepresentativeCenterZ` (AVERAGE of a composite's
  parts' centre-Z, not the AABB centre) puts FIVE inside a deployment zone, where
  `DeploymentZonePlacementChance` = 0.4 drops them 60% of the time - and deployment zones are
  behind the armies, not between them. Only the Central building (6x4 = 24 sq in, jittered up to
  10") is always on the table. Expected map: ~3.0 blocking pieces, 77.6 sq in = **2.2% of the
  72x48 table**. The other pieces (2 forests, 2 sandbag lines, mine field, rubble) are
  Cover/Difficult/Dangerous, and `HasLineOfSight` reads the Blocking flag only, so #363 never sees
  them. Consequence: this pool is structurally a no-regression instrument for any LoS work, and
  cannot be made into a measurement of one by adding games. FdgLab has no terrain lever today
  (`GameRunner` hardcodes `GetDefault()`) though the engine already supports
  `ETerrainPlacementMode.LoadFromFile` + `TerrainLayoutPath` (placed verbatim - no jitter, no
  dropout). Filed as **#365**.

- 2026-08-06 (pool gate, all three facets): **NO REGRESSION.** 8-army pool, Tactician (A) vs
  SoloRules (B), 64 ordered matchups x 10 games = 640 games per run, DOP 16, seeds from 1000,
  Realistic dice, each build in its own worktree, runs SEQUENTIAL so load conditions match.
  Reports: `FdgLab/reports/363-gate/{control,control-b,facet12,facet3}/` (gitignored; numbers of
  record are here).

  | run | engine | score | delta | paired flips | faults |
  |---|---|---|---|---|---|
  | control | `1183f55` (pre-#363) | 85.7% | - | - | 0 |
  | control-b | `1183f55` again | 85.7% | +0.00pp | **0** | 0 |
  | facets 1+2 | `8c875a7` | 85.2% | -0.55pp (z -0.70) | 57 (8.9%) | 0 |
  | facet 3 | `7498717` | 84.8% | -0.86pp (z -0.88) | 83 (13.0%) | 0 |

  - The control REPEAT is the reason the small deltas are readable: same code, same options, run
    again after the others - **outcome hash bit-identical (`6638851179176049`), zero flipped
    games**. So the harness contributed no noise here (a datum for #210, which is about exactly
    this), every flip below is caused by the change, and the only remaining uncertainty is
    sampling. Sigma is therefore computed PAIRED, over the games that actually moved (win<->loss
    counts 1, win<->tie 0.5): 0.78pp and 0.97pp, against the 1.98pp unpaired bound. Both deltas
    are inside 1 sigma; of 83 flips at facet 3, a neutral change would split them ~evenly and the
    observed net is ~5.5 game-equivalents.
  - Per-army rows (facet 3): Battle Brothers +1.9 (the archetype the source save came from),
    Dark Elf +0.6, Hives 0.0, HDF -0.6, Orks -1.9, Robot Legions -1.9, Dwarf Guilds -2.5, HEF
    -2.5. Cell noise at 80 games/row is ~4pp, so this is flat with no collapsed row.
  - Cost: decision mean 64.27 -> 63.40 -> 64.25ms, per-game wall 26.3 -> 25.9 -> 26.0s. The sight
    tests are free at this scale.
  - Honest limit, same as #296's gate: this 1v1 2k pool is a NO-REGRESSION instrument, not a
    measure of the fix. The Tactician already wins ~85% here, and the pathology needs terrain
    between two shooters - the save replay, the scenario and the pins are the evidence the
    behavior changed; the pool is the evidence nothing else broke.
  - Behavior spot-check on one identical game (same seed/armies, facet12 vs facet3 builds): 196
    narration lines differ - candidate scores rise and the ranking reshuffles - while the tally of
    CHOSEN intents is unchanged (8 AdvanceOnObjective, 4 EngageAtRange, 1 each Rush/Hold/Escort/
    Charge/Block). Pricing moved; the cowering failure mode did not appear.

- 2026-08-05 (facet 3 - the mirror): **incoming fire now respects the same walls** (engine
  7498717). `AttackContext.SightBlocked` generalized to `SightFactor` (float, default 1 = clear,
  so every caller that supplies no geometry keeps the old distance-only estimate). Offense still
  passes 0 for a cut lane - that shot would be taken FROM the endpoint being priced, so blocked
  means no shot. Every incoming estimate passes `TacticianWeights.BlockedThreatShare` (0.4)
  instead: `Score`'s retaliation, the projected-threat forecast (sighted from the PROJECTED
  position - that is the position the term prices from), `BestAlternativeTargetValue` (must move
  with the numerator or the share compares a wall-discounted "us" to a see-through-walls "them"),
  and `WantsDisembark`'s transport bail-out check. **Discount, not zero**: retaliation is a
  NEXT-activation threat and the shooter moves before it shoots, so a cut lane costs it a
  repositioning move it may not have - a hard zero would invent perfect hard cover and teach the
  whole army to hug walls. 0.4 is a stated prior (the generator's own arc search shows a clear
  lane is usually findable within one move), tunable like any weight via `--weights`. **Measured
  2026-08-06: 0.2 scores +0.78pp above 0.4 on the gate pool (borderline, and on near-LoS-free
  maps) - see the tuning note at the top. Left at 0.4 pending a terrain-dense instrument.**
  Melee threat deliberately untouched: a charge needs a path, not a sight line.
  - Pins: `TacticianLineOfSightTests` now 7 - partial factor scales linearly, Indirect ignores
    both 0 and partial factors, and `Score_WallShadowEndpoint_PricesIncomingFireBelowOpenGround`
    (two endpoints equidistant from a gunline we cannot answer; the covered one must price safer,
    and must still price BELOW zero - cover is worth something, not everything).
  - Verified: suite 2900 green; full `dotnet build`; headless smoke exit 0; scenario
    `363-wall-shadow-engage` byte-identical behavior (side-step 0.3708, volley wipes the unit,
    Hold-in-shadow 0.0000 - the Targets carry no guns, so nothing to discount); save replay of
    the rewound BattleBrothers state picks the same RushObjective winner (0.2500), with the
    wall-shadow candidates all pricing up: the old phantom advance -0.1789 -> -0.1477, the engage
    endpoints behind the wall -0.0758/-0.0789. The cowering bias in that table is gone.
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
- ~~**Deferred facet: retaliation/projected-threat symmetry.**~~ DONE 2026-08-05 (facet 3,
  above). The "escort valuation" named in the original deferral turned out to be melee-only
  (`ScreenLane` prices the ward's exposure with `EstimateMelee`), so it needs no sight gate.
- **Still open (recorded, not silently cut): melee threat reaches through IMPASSIBLE terrain.**
  `MeleeThreatReach` / the charge-threat clamps are straight-line, so an enemy that would have
  to walk the long way around a solid block still prices as if it could charge through it. Same
  route-vs-straight-line family as #264's approach fix, not a sight problem - left for its own
  slice. `DeploymentMatchup`'s estimate stays distance-only on purpose: at deployment there are
  no positions yet to draw a line between.

## Outcome

**SUPERSEDED IN PART, 2026-08-06: facet 3 was replaced by #365 the day after it shipped.** Facets
1+2 (offense-side exact LoS, the generator's clear-lane arc search) stand and are still the fix for
the phantom volley. Facet 3 - the threat-side mirror - was the wrong shape: a boolean LoS test
against where a shooter stands NOW, used to price what it does AFTER it moves, which put a cliff in
the score. `BlockedThreatShare` is deleted; threat is priced through walls again and cover earns a
bounded habit bonus instead. The tuning datum below is what exposed it (both 0.2 and 0.4 were
defensible, which is the signature of tuning the height of a cliff). See
`WorkItems/365-cover-as-a-habit.md`.

**Closed 2026-08-06.** All three facets shipped and gated. The Tactician no longer prices a shot
it cannot take, no longer fails to find the firing position two steps to the side, and no longer
treats a wall as if it were made of glass when the guns point the other way.

What the source save does now: the 6" advance into the corner wall's shadow that started this
(+0.2220, followed by "No actions available - passing") prices -0.1477, and the squad rushes 12"
toward the objective at (15,20) instead - honest play, since the volley it "gave up" was worth
~0 against 2+-save-in-cover Revenants.

Shipped: `AttackContext.SightFactor` (offense 0 on a cut lane, incoming at
`BlockedThreatShare` 0.4, Indirect exempt per weapon); `MacroActionGenerator.ClearLaneGoal`'s
15-degree arc search for a band endpoint that can actually see the target; sight gates on
retaliation, projected threat, the retaliation share's denominator, and the transport bail-out.
7 pins in `TacticianLineOfSightTests`, suite 2900 green, scenario `363-wall-shadow-engage.json`
(+ `363-Gunline`/`363-Targets` signal armies), 4 x 640-game pool gate above.

Left open on purpose, filed rather than dropped: **#364** - melee threat is still straight-line,
so a charge prices as if it could cross Impassible terrain the real move has to walk around. Same
"estimates ignore terrain" family, different mechanism (path, not sight).

Not done and not needed: no GUI hand-verify - the planner runs identical code in both modes and
the evidence here is headless-reproducible. Worth a look next time you play near terrain, though:
the thing to watch is a unit CHOOSING cover it used to ignore.
