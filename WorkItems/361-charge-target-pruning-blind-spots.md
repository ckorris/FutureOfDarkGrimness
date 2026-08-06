# 361 — Tactician never considered the reachable charge (value pruning + zero-progress construction)

**Status**: CLOSED 2026-08-05 - all four facets built and verified (see Outcome)
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

4. **Facet 4 - routed charges refine to contact** (second commit, after Chris asked "the pick
   isn't to charge the APC?"): two flaws in `BuildCharge`'s routed branch. The contact goal
   was placed with the INSCRIBED contactDistance, so a rect base's front edge landed inside
   the target, the validator rejected every long arc, and the ladder stalled the charge
   mid-route (the APC charge died at 3" of a 9" dogleg); now placed with circumscribed radii
   on both sides (always legal, slightly short). And route arrival is grid-quantized (the
   squad charge missed the 0.25" contact grade by 0.01"); new
   `MovementPlanner.NudgeToContact` translates the arrived formation toward the TARGET's
   nearest model (target-only gap measurement - contact with a bystander must not read as
   arrival) to the contact gap, kept only if the extended move fully re-validates. The
   out-of-reach approach standoff got the same circumscribed treatment.

Pins (Tests/MacroActionGeneratorTests, all four proved red against the pre-fix generator):
- `ChargeToContact_CheapNearbyEnemy_SurvivesValuePruning` (facet 1, the Hive Lord shape)
- `ChargeToContact_BigRectBaseParkedByRotatedTerrain_StillMakesProgress` (facet 2, exact save
  geometry: the rect base, the 225-degree bar, the 10" enemy; asserts not-Blocked and >= 3"
  closed)
- `ChargeFamily_RanksNearestEnemyFirst_WhenFeasibilityTies` (facet 3)
- `ChargeToContact_RoutedAroundTerrain_RefinesToBaseContact` (facet 4, same scene as facet 2's
  pin but asserts ActionType Charge + Reachable + gap <= 0.25")

## Verification

Facets 1-3 (commit `401f2fe`):
- Engine suite 2891/2891 green (2888 + the three pins).
- Save replay: the wedge gone, the APC charge scored (BudgetClipped), but the PICK was still
  an east reposition (0.46) - both fighting charges were demoted to approaches by the routed
  construction, which became facet 4.
- Solo D1 identity: `4B73F1B9DBBC8102` / `E86503B238B27EA1` - both bit-identical (solo
  resolvers untouched; circle-only armies see identical geometry).
- Pool bench tactician-vs-solorules 3200 games (hash `3D5D010A3A155595`): aggregate 85.52%
  vs the #359 baseline 84.39% (+1.13pp, sigma ~0.65pp), 0 faults, 0 timeouts across all 64
  cells; decision cost flat (mean 28.50ms, worst p95 515ms). Hives mirror 76%, RL mirror 84%.

Facet 4:
- Engine suite 2892/2892 green.
- Save replay: **ChooseAction -> Charge, target Blood APC** - Reachable base contact at
  (45.5,21.4), score 1.51; the squad charge is a real Charge too (0.84, second); the old
  west slide is nowhere. The exhibit now plays the move a human sees instantly.
- Solo D1 identity: both hashes bit-identical again.
- Pool bench 3200 games (hash `6E781E5D634061ED`): aggregate 85.42% - flat vs facets 1-3's
  85.52% (-0.10pp, sigma ~0.65pp), +1.03pp over the pre-#361 baseline; 0 faults, 0 timeouts;
  decision cost flat (mean 28.48ms). Hives mirror 76 -> 78, RL mirror 84. More real charges
  at no win-rate cost.

## Outcome

All four facets built and verified 2026-08-05: nearest-enemies union (targeted families always
evaluate the enemy standing next to us), circumscribed planning clearance (router and swept-base
validator finally see the same world - the rect-base wedge class is gone), nearest-first family
enumeration (reachable charges keep their pruning slot), and routed-charge contact refinement
(circumscribed goal placement + validated NudgeToContact - fighting charges reach the scorer AS
charges). The reported exhibit went from "slid 6 inches west for nothing" to "charges the APC it
was standing next to". Four pins, each proved red; suite 2892; solo D1 bit-identical throughout;
pool aggregate 85.42-85.52% vs the 84.39% baseline with 0 faults. Same-day follow-up (Chris:
"pretty wrong - how hard to fix?"): the all-enemies gap grading mislabel fixed too - see the
follow-up note. Recorded, not built: the difficult-terrain circumscribed conservatism.

## Notes

- 2026-08-05 (last): the all-enemies grading mislabel fixed as a same-day follow-up (engine
  `1183f55`). BuildCharge's feasibility gap now measures against the TARGET's models only
  (new `MovementPlanner.UnitFootprints`, shared with NudgeToContact); the construction
  machinery (refine, ladder) keeps the all-enemies lists, which are what make the move legal.
  The mislabel was worse than cosmetic: a charge that dead-ended in contact with a bystander
  was declared for real and the stage's #312 reach check rejected it at resolve time (#216
  degradation class). Pin `ChargeGrade_DeadEndOnABystander_IsNotAReachableCharge` proved red
  (bystander one base-width in front of the target - the pre-fix grade was a playable
  Charge). Suite 2893/2893; D1 bit-identical; benchmarked on Chris's melee proxy (Hives as
  Tactician vs the whole pool, 400 games, full-pool baseline rows from the facet-4 run):
  90.38% vs 90.00%, 0 faults, Hives mirror 78 -> 85 - the melee-densest cell, where a
  false charge declaration costs a whole activation.
- 2026-08-05 (later): facet 4 built - the "routed charges never refine to contact" follow-up
  graduated from a recorded observation to the fourth facet after Chris confirmed the charge
  was the expected pick. Remaining recorded observation, NOT built:
  - Difficult-terrain detection also uses the circumscribed radius now - rect-based units get
    the 6" cap slightly more often than the true footprint requires (conservative, legal).
  - The `BaseRadiusInches = 0.5` aside filed earlier was a misreading of the save (that value
    was another model's); the Hive Lord's base data is correct - the bug was the planner's use
    of the inscribed approximation, not the import.
- 2026-08-05: filed from the save analysis (Chris's challenge caught the first diagnosis using
  centroid distances - base-to-base, two enemies were chargeable). Repro:
  `dotnet FdgLab.dll analyze WhyDidHiveLordMoveSlightlyToTheRight.fdgsave --unit "Hive Lord"`.
