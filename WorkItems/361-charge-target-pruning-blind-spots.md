# 361 — Tactician never considered the reachable charge (value pruning + zero-progress construction)

**Status**: facets 1-3 built + verified 2026-08-05; facet 4 (routed-charge contact refinement) in progress
**Related**: #191 (Tactician umbrella), #359 (congestion/routing - shares the funnel mechanics),
#264 (routing fallbacks), #312 (charge-reach gate), #149/#150/#341 (base shapes / swept-base validator)

## The report (Chris, live 2v2 game)

`WhyDidHiveLordMoveSlightlyToTheRight.fdgsave`: a Hive Lord (stock 12" charge, no Fast/Slow)
stood at (48.5,17) with the Blood APC **9.3" edge-to-edge** away and an Infantry Squad at
**10.4"**. It charged neither - it slid 6" west toward the contested marker. The analyze replay
picked that slide deterministically (-0.037 vs -0.211 Hold; everything toward the enemy graded
Blocked with zero progress).

## What it actually was (diagnosis correction, 2026-08-05)

Facet 1 held up. Facet 2 did NOT - the filed suspicion (missing nearest-reachable fallback /
ladder collapse in the funnel) was wrong. Instrumented replay found the real mechanism:

**The Hive Lord is on a 3.62" x 4.72" RECTANGULAR base** (the save's `baseShape.rect`; its
`BaseRadiusInches` correctly reports the #149 INSCRIBED radius, 1.81"). It was legally parked
0.03" from a tank-trap bar **rotated 225 degrees** (`zone.rotated` - the earlier axis-aligned
terrain extraction was wrong too; nearly every piece on this map is rotated). The #341
swept-base validator sweeps the true oriented rect, whose corners reach the **circumscribed**
2.98" - so it was RIGHT that every north/east move clips the bar. But all planning geometry
(route grids, straight-clear probes, route metrics) inflated by the inscribed 1.81": the
router planned corridors the swept base cannot take, every backoff arc failed validation, and
the ladder collapsed every candidate to a stand-still. Hence the signature: EVERY move toward
the enemy "Blocked at own centroid" while Hold validates. #149 chose inscribed deliberately
("until #150 brings true swept-shape geometry") - #341 then upgraded the validator but nobody
upgraded the planner's view, and the deferred over-block became a hard wedge.

And a third defect surfaced while fixing: even once the APC charge graded honestly
(BudgetClipped, real progress), it never reached the scorer - in-family pruning ranks by
feasibility with GENERATION order as tiebreak, and generation was value-first, so the hopeless
35"-away Assault Brothers held the family's budget slot.

## The build (engine)

1. **Facet 1 - nearest-enemies union**: `MacroActionGenerator` targets top-value-3 UNION
   nearest-2 (by closest LIVING MODEL, not centroid - reach is edge-to-edge). `NearestEnemies
   = 2`. `rankedEnemies[0]` (value pick) still anchors M6/M7/M8.
2. **Facet 2 - clearance radius**: new `MovementPlanner.TerrainClearanceRadius` = max
   circumscribed base radius; used by `PlanMoveAlongRoute`, the generator's shared grid +
   `BuildCharge` straight-clear probe + route-progress metric, and the planner's
   `RouteToObjective`/`RouteToEnemy` grids. Conservative by construction (swept disc contains
   the swept base at any facing), so planned routes always validate; circles unchanged
   (circumscribed == radius), so all-round-base games are bit-identical. Contact arithmetic
   (leadRadius + enemy radius) stays inscribed - the shape-aware gap refinement corrects it.
3. **Facet 3 - nearest-first enumeration**: targeted families (M4 bands, M5 charges) enumerate
   nearest-enemy-first. Value picks WHO is targeted, never the order; the stable in-family
   pruning rank then keeps the reachable charge under a tight budget.

Pins (Tests/MacroActionGeneratorTests, all three proved red against the pre-fix generator):
- `ChargeToContact_CheapNearbyEnemy_SurvivesValuePruning` (facet 1, the Hive Lord shape)
- `ChargeToContact_BigRectBaseParkedByRotatedTerrain_StillMakesProgress` (facet 2, exact save
  geometry: the rect base, the 225-degree bar, the 10" enemy; asserts not-Blocked and >= 3"
  closed)
- `ChargeFamily_RanksNearestEnemyFirst_WhenFeasibilityTies` (facet 3)

## Verification

- Engine suite 2891/2891 green (2888 + the three pins).
- Save replay (`analyze ... --unit "Hive Lord"`): the wedge is gone - candidates route and
  validate; the Blood APC charge is now a scored row (BudgetClipped, 2.8" progress); the
  squad charge-approach grades Reachable at 1" standoff and scores 0.42; the winner is an
  east reposition (0.46) toward the team's objective cluster instead of the old dead west
  slide (-0.09). Defect behavior (reachable charges invisible / everything Blocked-at-zero)
  eliminated.
- Solo D1 identity: `4B73F1B9DBBC8102` / `E86503B238B27EA1` - both bit-identical (solo
  resolvers untouched; circle-only armies see identical geometry).
- Pool bench tactician-vs-solorules 3200 games (hash `3D5D010A3A155595`): aggregate 85.52%
  vs the #359 baseline 84.39% (+1.13pp, sigma ~0.65pp), 0 faults, 0 timeouts across all 64
  cells; decision cost flat (mean 28.50ms, worst p95 515ms). Hives mirror 76%, RL mirror 84%.

## Notes

- 2026-08-05: built all three facets; diagnosis correction above. Follow-up observations
  recorded, NOT built:
  - **Routed charges never refine to contact**: the routed `BuildCharge` branch stops at the
    grid-quantized goal - the squad charge achieved a 0.26" gap and missed the Reachable grade
    by 0.01" (`ContactFeasibleGapInches = 0.25`), so a melee monster standing a hair from an
    enemy plays a Rush-to-standoff instead of a fighting charge. The straight branch's
    `RefineStepTowardGap` (or a final-leg refinement) would close it.
  - Difficult-terrain detection also uses the circumscribed radius now - rect-based units get
    the 6" cap slightly more often than the true footprint requires (conservative, legal).
  - The `BaseRadiusInches = 0.5` aside filed earlier was a misreading of the save (that value
    was another model's); the Hive Lord's base data is correct - the bug was the planner's use
    of the inscribed approximation, not the import.
- 2026-08-05: filed from the save analysis (Chris's challenge caught the first diagnosis using
  centroid distances - base-to-base, two enemies were chargeable). Repro:
  `dotnet FdgLab.dll analyze WhyDidHiveLordMoveSlightlyToTheRight.fdgsave --unit "Hive Lord"`.
