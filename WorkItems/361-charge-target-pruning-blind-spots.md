# 361 — Tactician never considered the reachable charge (value pruning + zero-progress construction)

**Status**: filed 2026-08-05 (not started)
**Related**: #191 (Tactician umbrella), #359 (congestion/routing - shares the funnel mechanics),
#264 (routing fallbacks), #312 (charge-reach gate)

## The report (Chris, live 2v2 game)

`WhyDidHiveLordMoveSlightlyToTheRight.fdgsave`: a Hive Lord (stock 12" charge, no Fast/Slow)
stood at (48.5,17) with the Blood APC **9.3" edge-to-edge** away on an essentially clean
straight lane, and an Infantry Squad at **10.4"** behind the Tank traps. It charged neither -
it slid 6" west toward the contested marker and parked against the Wreckage block. The analyze
replay picks that slide deterministically (-0.037 vs -0.211 Hold; everything toward the enemy
is Blocked with zero progress). Both mechanisms verified against the save:

### Facet 1 - value-only target pruning has no eyes for reachability

`MacroActionGenerator.TopEnemies = 3` ranks targeted families (charges, range bands) by
`UnitValue` alone. Here the top-3 were the Blood Assault Brothers (35" away!) and two 10-model
Infantry Squads - the cheap APC, the ONLY enemy with a clean in-reach charge lane, was pruned
from every targeted family. A melee monster standing next to a transport never evaluated
charging it. A human sees that charge instantly.

Fix sketch: rank by a reachability-aware key (value discounted by distance-beyond-reach), or
take the union of top-value-K and nearest-K enemies (K=3 + K=2 keeps enumeration O(small));
the diversity pruning already guarantees a family slot.

### Facet 2 - a terrain-detoured charge grades Blocked-at-zero instead of a partial approach

The 10.4" Infantry Squad WAS a charge candidate; its straight lane clips the Tank traps, so
`BuildCharge` fell to the routed `PlanMoveToward` - which returned literal zero progress
(feasibility Blocked, endpoint = own centroid), so the argmax saw "charging = standing still".
A partial route move (BudgetClipped) should be the worst case there; zero suggests the routed
contact goal lands in inflated/occupied cells with no nearest-reachable fallback on this path
(the #264-issue-3 fallback exists in `Plan`, unclear in `BuildCharge`'s PlanMoveToward branch),
or the ladder collapsed on the funnel between the Wreckage and the traps (the ally Hex-Furies
sit at its mouth - the #359/option-3 friendly-blind class). Needs a repro pin from this save's
geometry before choosing the fix.

## Notes

- 2026-08-05: filed from the save analysis (Chris's challenge caught the first diagnosis using
  centroid distances - base-to-base, two enemies were chargeable). Repro:
  `dotnet FdgLab.dll analyze WhyDidHiveLordMoveSlightlyToTheRight.fdgsave --unit "Hive Lord"`.
  Aside, worth a look while here: the Hive Lord's save data carries `BaseRadiusInches = 0.5` -
  a monster on an infantry base smells like a base-size import default (#225 fixed vehicles).
