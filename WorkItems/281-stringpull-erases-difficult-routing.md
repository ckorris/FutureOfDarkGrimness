# 281 — StringPull erases the router's difficult-terrain preference

**Status**: done (fixed 2026-08-05; geometry + scoring gradient, see notes)
**Related**: #264 (route gradient), #191 (Tactician umbrella), #100/#197 (Strider as a rule)

## Goal

The Tactician's router should actually prefer clear ground over difficult ground when the two are
comparable in length — which is what `GridPathfinder.DifficultCostMultiplier` is written to do, and
what its own comment claims ("Difficult terrain costs extra so clear routes of similar length win").
Today that preference is computed and then discarded.

## The mechanism

`GridPathfinder.FindPath` runs A* over `TerrainGrid`, where difficult cells cost
`DifficultCostMultiplier = 2f`. So the CELL PATH does bend around difficult ground.

Its last act is `return StringPull(terrain, waypoints, baseRadiusInches)`, and `StringPull` greedily
replaces any run of waypoints with a direct hop whenever `SegmentClear` says the shortcut is legal.
`SegmentClear` tests **Impassible only**:

```csharp
return !terrain.Any(t => t.TerrainType.HasFlag(ETerrainType.Impassible)
    && t.Shape.DoesPathIntersectZone(start, end, baseRadiusInches));
```

So every bend the A* made to dodge difficult terrain is pulled straight back through it, whenever the
direct line is impassible-clear. Net effect: **the difficult multiplier only survives where impassible
terrain independently forces the bend.** On an open table with only difficult pieces, the returned
route collapses to `{start, goal}` and the multiplier does nothing at all.

Demonstrated while writing the #264 Strider pin: a difficult band straddling the lane
(`RectangularZone(20,28,18,26)`, start (24,8) → goal (24,36)) returns a route of exactly 28.0" — the
straight-line distance — even though crossing the band costs ~2x. The pin had to be rewritten to
assert at the GRID level (`TerrainGrid.IsDifficult`) because the routed geometry cannot show the
difference today. See `TacticianWalledUnitTests.StriderGrid_ChargesNoDifficultPenalty_UnlikeAPlainUnitsGrid`.

## Why it matters

- **The stated intent is unmet.** A unit routes through mud when a marginally longer clear lane
  exists, then eats the engine's 6" whole-move cap (`DIFFICULT_TERRAIN_MOVE_CAP_INCHES`) for it —
  which is exactly the outcome the multiplier was added to avoid.
- **It hides Strider.** #264's `TerrainGrid.Build(..., ignoreDifficultTerrain)` is correct but has
  little observable effect on geometry, because the plain and Strider routes get string-pulled to the
  same line anyway. Strider will only start paying off once this is fixed.
- It is a silent no-op with a tunable constant attached — the same shape as the dead
  `FunnelStallFraction` removed during #264 slice 3, and a future reader would take it as
  load-bearing.

## Fix sketch (not built, not signed off)

Make `StringPull`'s clearance test aware of what the shortcut costs, not just whether it is legal.
Options, cheapest first:

1. **Difficult-aware shortcutting**: give `StringPull` a predicate that refuses a shortcut which
   introduces difficult ground the original run avoided (pass "does this unit ignore difficult" in, so
   Strider keeps today's aggressive pull). Smallest change; keeps the impassible test intact.
2. **Cost-preserving pull**: accept a shortcut only when its weighted cost (same 2x rule as the A*)
   is not worse than the run it replaces. More principled, slightly more work per pull.
3. Leave geometry alone and instead price difficult crossings in the SCORE gradient
   (`RouteMetrics`), so the planner avoids them without changing routing. Weakest — the move itself
   still walks through the mud.

Whichever is chosen, this is a routing-behaviour change for every Tactician unit on a table with
difficult terrain, so it needs a benchmark run attached per the `TacticianWeights` file-header policy
(the constant is in `GridPathfinder`, but the policy's spirit applies) plus a solo D1 check —
`MovementPlanner`/`GridPathfinder` are not on the solo bot's path today, so the D1 hashes should stay
bit-identical; confirm rather than assume.

## Notes

- 2026-08-05 (FIXED - geometry slice + same-session scoring follow-up): **the erasure turned out to
  be THREE stacked bypasses, not one** - the filing's StringPull mechanism was real but two gates
  upstream of it meant the A* never even saw most mud lanes:
  1. `MovementPlanner.PlanMoveAlongRoute` only pathfound when the straight lane was IMPASSIBLE-
     blocked - on a difficult-only lane the route was the straight segment, no search, no grid.
  2. `GridPathfinder.FindPath`'s early exit returned any impassible-clear straight shot before
     running the A*.
  3. `StringPull` re-tested shortcuts with the impassible-only clearance (the filed mechanism).
  Fix (option 2 of the sketch, cost-preserving pull, plus the two gates): the planner now also
  routes when the straight lane crosses difficult ground the unit does not ignore; FindPath's early
  exit additionally requires the straight shot difficult-clear PER THE GRID; and StringPull (given
  the grid) accepts a shortcut only when its cost under the A*'s own metric - length with the 2x
  difficult multiplier, sampled against the radius-inflated grid at half-cell steps - is no worse
  than the run it replaces (slack 0.1"). A shortcut through mud that is GENUINELY cheaper is still
  taken, so unavoidable crossings stay straight. The grid also threads into `RouteLegsFor`'s
  per-model pulls - without that, every model's legs collapsed straight back through the mud the
  unit route just paid to avoid (nothing impassible forces the bend, by construction). Strider needs
  no special casing anywhere: its grid marks no difficult cells, so every new test degrades to
  today's behaviour.
  - **Scoring gradient (same session, Chris asked for the follow-up):** `RouteMetrics.Route` had the
    same impassible-only gate, so the objective deadline-slack and melee-approach gradients priced
    straight lines through mud the mover now detours around. Route now triggers the search on a
    difficult-crossed lane too, with an `ignoresDifficultTerrain` parameter from
    `TacticianPlanner.UnitRoute` (whose grid already carried the flag) so a Strider lane skips the
    build. `MacroActionGenerator.Plan`'s progress measure needed nothing - it grades along the
    route `PlanMoveAlongRoute` returns, which already bends. The melee scorer's existing
    "only-on-a-real-detour" switch picks mud detours up automatically.
  - **Tests** (all red-by-design verified): `GridPathfinderTests.FindPath_DifficultBandOnTheLane_
    RoutesAroundIt` (the filing scene: 28" straight -> ~30" mud-free detour),
    `FindPath_UnavoidableDifficultField_CrossesRatherThanWanders` (cost-preserving, not
    mud-forbidding; green pre-fix by design), `PlanMoveToward_AvoidableDifficultBand_
    ModelsRouteAroundAtFullBudget` (end-to-end: every MODEL leg difficult-clear and the move keeps
    its 12" budget instead of the 6" cap), `Route_DifficultBandOnTheLane_PricesTheDetour_
    UnlessTheUnitIgnoresIt` (gradient), and the #264 Strider pin upgraded to routed GEOMETRY (plain
    detours, Strider straight) now that the geometry can show it. The old
    `PlanMoveToward_DifficultRoute_CapsTheMoveAtSix` scene became avoidABLE under the fix (the unit
    correctly walked around at 11.99" - the bug's own demonstration); its band now spans the table
    so the cap pin keeps testing an unavoidable crossing.
  - **Verification**: engine suite 2869/2869; app suite 1108/1108; full build + headless smoke exit
    0. **Solo D1 bit-identical** on both matrices (builtin mirror `4B73F1B9DBBC8102`, builtin vs
    builtin-basic `E86503B238B27EA1`; controls re-derived same-session on this machine - older
    recorded hashes are machine/master-stale). **Pool re-gate (geometry slice), 8-army pool,
    Tactician vs SoloRules, 64 ordered matchups x 50 games = 3200 each side, DOP 12, seeds 1000+:**
    aggregate **84.23% -> 83.71%** (-0.5pp vs ~0.65pp aggregate sigma - flat, the expected shape:
    this pool's auto-layout terrain exercises mud lanes only incidentally; the pins evidence the
    fix), faults 3 -> 1 (all 120s watchdogs under triple-bench CPU load; the one fix-side fault
    seed also faulted in control), worst cell Dwarf-vs-Hives -14pp = 2.0 sigma on a 50-game cell
    (a 64-cell scan expects ~3 past 2 sigma; per-army aggregates level). Decision cost flat
    (31.29 -> 31.21ms mean; worst p95 552 -> 569ms, same load). Hashes: control
    `65C3DB4896788FAC`, fix `A5C4FF16B5E8F1EE`. **Gradient slice A/B (isolated: control = geometry
    + #216 + #170 build, hash `221D7FD8F0733551`, so #170's deploy shift is on both sides):
    aggregate 84.42% -> 84.84%** (+0.4pp, inside sigma; hash `CA79CA44195000DF`), faults 1 -> 0
    across 3200 games (the recurring DE-vs-Hives seed-1016 "fault" completed in 31s on an unloaded
    machine - every watchdog fault this session was CPU-contention from concurrent benches, none a
    regression), decision cost 27.6ms mean / 477ms worst p95 (lowest of the session). Only >=10pp
    cell: Dwarf-vs-Hives +12pp - the same noisy cell that read -14pp in the geometry A/B, now
    recovered. Cross-read: the #170 deploy fix alone moved the pool 83.71% -> 84.42%.

- 2026-07-25: filed. Found while pinning the #264 Strider half; no fix attempted, no scope cut —
  #264's grid flag was landed as correct-and-ready rather than blocked on this.

## Decisions

- **Cost-preserving pull over a difficult-forbidding predicate** (sketch option 2 over 1): the
  boolean form ("refuse a shortcut introducing mud the run avoided") over-refuses when crossing is
  genuinely cheaper; pricing both sides under the A*'s own metric makes the pull and the search
  agree by construction, and Strider falls out for free.
- **Sample the GRID, not the terrain shapes, in the pull's cost:** the cells are radius-inflated
  exactly like the search's, so a shortcut grazing the inflation pays what the search paid to avoid
  it - two metrics would oscillate.
- **RemainingFrom's offset legs stay impassible-only.** Only the route SHAPE carries the mud
  detour; pricing mud on the project-onto-route joins is second-order and not worth the extra
  geometry today.
- **The 6" cap is not priced into deadline slack for unavoidable crossings.** A route that MUST
  cross mud reads as gapNow ~= straight length while the unit will actually make ~6"/activation;
  candidate endpoints (real planned moves, capped) price it per-move, but `movesLeft - gap/speed`
  is optimistic there. Known limit, recorded not silently cut - revisit if muddy-table play shows
  units committing to hopeless marker runs.

## Outcome

Difficult-terrain routing preference now survives end to end: planner trigger, A* early exit,
route-level and per-model string pulls, and the scoring gradients all price mud with the same
2x-weighted metric, while unavoidable crossings still cross and Strider keeps full-speed straight
lanes (first observable Strider geometry). Pool-flat, solo-D1-identical, decision-cost-flat.
Follow-up owed: none within this item's goal; the deadline-slack cap limit above is the one
recorded soft edge.
