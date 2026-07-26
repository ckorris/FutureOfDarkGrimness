# 281 — StringPull erases the router's difficult-terrain preference

**Status**: open (found 2026-07-25 while folding Strider/Flying into the #264 route gradient;
analysis only, no fix attempted)
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

- 2026-07-25: filed. Found while pinning the #264 Strider half; no fix attempted, no scope cut —
  #264's grid flag was landed as correct-and-ready rather than blocked on this.

## Outcome

(open)
