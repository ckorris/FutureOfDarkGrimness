# 291 — Models could move partially off the table

**Status**: in-progress
**Related**: #149/#150 (base shapes + shape-aware geometry), #029 (Aircraft fly-off), #155 (GUI clamps)

## Goal
The movement validator had **no table-bounds rule at all**. The only thing keeping models on the board
was the GUI refusing clicks outside it — which constrains a model's CENTRE, so a big base overhangs the
edge long before its centre leaves the table. Reported on vehicles for exactly that reason.

Done when: no model may end a move (or a consolidation) with part of its base off the table, enforced
engine-side for every client and AI; and the move preview stops at the edge rather than letting the
player build a path that would be thrown out at Done.

## Notes
- 2026-07-26: reported from play — "vehicles are able to move partially off the table".
- Aircraft are unaffected: flying off the edge is legal for them and runs through a separate path
  (`ResolveForcedAircraftMove`) that never calls `ValidatePaths`.

## Decisions
- The check uses the model's **true oriented footprint** (hull corners + Minkowski rounding), not a
  bounding circle. `ForcedAircraftMove.WouldLeaveTable` uses the CIRCUMSCRIBING radius because it predates
  facings being threaded there; copying that here would hold a 4"x2" vehicle 2.24" off the edge even when
  its 2" side faces the edge. Facings are available on the path (#282 puts one per waypoint), so the exact
  test is both possible and what a player expects.
- It is a **"not worsened"** rule, not an absolute one — matching `ValidateEndsOnFriendly` and
  `ValidateCoherencyNotWorsened`. A model that somehow already overhangs may move as long as it doesn't
  overhang further, so it can never be frozen in place by a validator that rejects everything it can do
  (the #159 deadlock class).
- The check went into **all four** validator bodies (three `ValidatePaths` overloads +
  `ValidateConsolidationPaths`). They each duplicate their check list, and the first patch only caught
  two of them — the dedicated tests are what surfaced the rest.
- `MovementUtilities.ClampTravelToTable` (bisection over `OverhangInches`, shape-agnostic) is shared by
  the GUI move and consolidation resolvers, so preview and validator cannot disagree. The consolidation
  resolver's old circumscribing-radius `ClampToTable` was replaced by it.
- The AI needed no change: `MovementPlanner.ValidateWithBackoff` runs the same validator and halves its
  step on any fault, and a 0" hold is always bounds-legal for a unit that starts on the table.

## Outcome
Shipped 2026-07-26 (engine `3ad0f2f`, app in the same superproject commit). Engine:
`ValidateEndsOnTable` + `OverhangInches` + `ClampTravelToTable` in `MovementUtilities`, a new
`EErrorReasonType.EndedOffTable` (and its message - the `ErrorReasonToString` switch throws on an
unmapped value, which the first test run caught). App: `GuiDefineMovementResolver` clamps the single-model
ghost's travel and reports an off-table group step as infeasible (so `LargestFeasibleScale` shrinks the
whole step and the formation keeps its shape); `GuiConsolidationMoveResolver` swapped its
circumscribing-radius clamp for the shared exact one.

15 new `MoveStaysOnTableTests` - the reported vehicle case, the flush-along-an-edge case a radius test
would wrongly reject, a facing-sensitivity pair, all four edges for a circle, the not-worsened escape
hatch, consolidation, and the clamp helper.

Two existing `ConsolidateStageTests` fixtures placed models at `(0,0)` - the table corner, where a base is
already half off - and moved them further out; shifted to mid-table (they test delta/facing application,
not bounds).

Engine 2219/2219, app 639/639, build clean, three AI-vs-AI headless games exit 0 (the AI backs off on the
new fault rather than faulting). Awaiting GUI hand-verify with a vehicle at a board edge.
