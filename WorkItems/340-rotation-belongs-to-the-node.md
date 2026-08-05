# 340 — A rotation dialled in mid-path rotated the model where it was STANDING

**Status**: in-progress (implemented + tested; awaiting GUI hand-verify)
**Related**: #150 (per-waypoint travel facing), #282 (offsets captured per waypoint), #283 (consolidation
rotate-in-place), #312 (end-state at end facing), #213/#317 (impassible preview), #155 (difficult clamp)

## Goal

A rotation belongs to the node it is placed at, and to nothing before it. Concretely:

1. **Placement.** Wheel / R shapes only the live ghost. Clicking commits that attitude *to that node*. No
   already-placed node, and above all not the ground the model is currently standing on, is re-oriented.
2. **Legality.** The attitude *during* a leg is not validated — the base turns from the node it left to the
   node it is arriving at somewhere along the way, and which instant is the animation's business. So:
   - a leg is **blocked only when its swept footprint collides at BOTH endpoint attitudes** (one clear
     attitude is enough), and
   - **every node's pose (position + the facing it was placed with) is checked strictly**, which is what
     still stops a move ending rotated into a wall.
3. **Animation.** The move beat carries the attitude at each waypoint and the glide interpolates rotation
   between them, so what the player watches is the pose sequence they planned.

Owner-reported (2026-08-04, in-game): a rectangular model parked next to a wall could not be told to "move
out a way and then turn" — the turn was applied at the start square too, where the rotated base clipped the
wall, and the move was refused as MovingThroughImpassibleTerrain.

Applies to movement (single + group) and to consolidation (owner's call, 2026-08-04).

## Notes

- 2026-08-04: **Root cause.** `MovementUtilities.SegmentFacing` returns `Facings[i]` — the ARRIVING
  attitude — and the whole leg into waypoint `i` is swept as one rigid base at it. A swept base necessarily
  covers its start point, so the departure point is tested already rotated; for leg 0 the departure point is
  the model's real, currently-legal square. Four call sites shared the fault: the impassible gate
  (`ValidateMovingThroughImpassibleTerrain`), its preview twin (`FindFirstTerrainCrossing` ->
  `DoesPathCrossImpassibleTerrain` / `FindFirstImpassibleCrossing`, which is what reddens the ghost and
  refuses the click), the enemy pass-through gate (`ValidateMovingThroughEnemyUnits`), and the GUI's
  `EnemyClampTravel`.

  The manual rotation is not the only trigger. #150 auto-faces each waypoint along its direction of travel,
  so a rectangle parked parallel to a wall that sets off diagonally is swept at the diagonal attitude *from
  its start square* with no keypress at all. Same defect, same fix.

- 2026-08-04: **No animation existed to fix.** `UnitMovedBeat.ModelMove` carried `Waypoints` only, and
  `MovementExecutor.CommitPositions` snaps `Facing` to the final value before the glide even plays — the
  model's rotation has always popped at the start of its move. Facings are now carried on the beat
  (parallel to the waypoints, index 0 = the pre-move resting attitude) and `GlideState` interpolates them.

## Decisions

- **"Either endpoint attitude" over "departing attitude only"** (owner's pick, 2026-08-04, from three
  options). Departing-only is the literal reading of "only that node gets the new rotation" and is a
  one-line change, but it is *stricter* than today for the #150 auto-facing case — a tank facing north
  driving east down a narrow corridor would be swept north-attitude and refused where today it passes. The
  either-attitude rule never rejects anything today's code accepts (the arriving attitude is one of the two
  it tries) and fixes the auto-facing start collision as well as the manual one.

  The OR is evaluated **globally over the obstacle set, per attitude** — "there is one attitude at which
  this leg is clear of everything" — not per obstacle, so "clear of the wall facing north, clear of the
  pillar facing east" is not a pass.

- **Legality widens; hazard detection does not.** Dangerous- and Difficult-terrain crossing stay on the
  arriving attitude, exactly as before. They answer "does this ground affect the model", which is not the
  same question as "is this move legal", and widening them either way changes how often units eat terrain
  wounds or the 6" cap — a gameplay change nobody asked for. `FindFirstTerrainCrossing` therefore takes an
  explicit polarity (`ELegAttitudeRule`) rather than silently applying one rule to both.

- **The gate and its preview are now one implementation.** `ValidateMovingThroughImpassibleTerrain`
  delegates to `FindFirstTerrainCrossing` instead of walking the legs itself. They were two copies of the
  same walk that `docs/ResolverGuide.md` requires never to diverge; with a two-attitude rule plus node
  poses there is far more to keep in step, so the duplicate is gone.

- **A pose identical to the one before it is not checked.** The node-pose check skips a node whose position
  AND facing both match the previous pose (the model's resting pose, for node 0). Otherwise a hold — the
  AI's `Positions = [currentPosition]` fallback — would self-flag for any model already overlapping
  terrain, which is the documented reason the leg walk skips zero-length legs and the thing that crashed
  `DefinePathStage` before. A pose the move does not create is not a pose the move has to justify.

- **Deliberately NOT changed** (recorded rather than quietly dropped):
  - `PathPassesThroughUnit` (the Strafing fly-over detector) still sweeps every leg at the model's resting
    facing and ignores `Facings` entirely. That predates #150 and is detection, not legality.
  - Intermediate nodes are not checked against enemy or friendly bases, nor against the table edge; only
    the final node is (`ValidateEndsOnFriendly` / `ValidateEndsOnTable` / the standoff + charge-reach
    checks, all already at `EndFacing`). Adding those would be new enforcement, not a fix.
  - The difficult-terrain preview clamp keeps the ghost attitude. Its degenerate case is not a freeze: an
    already-overlapping start yields `entry = 0`, which falls into `CappedCrossing` (move capped at 6")
    rather than `StoppedShortOfEdge`, unless the model has already spent the 6" — which is correct.
  - A model that STARTS with its base overlapping impassible terrain is still trapped (every leg collides
    at both attitudes). Pre-existing — but note the node-pose check now closes its one remaining escape:
    a pure pivot used to be unchecked (zero-length legs are skipped), so such a model could at least turn.
    No "not worsened" guard was added for it, deliberately: the leg sweep has never had one either, so
    guarding only the pose would be incoherent and would read as safety that is not there. Nothing in legal
    play starts overlapping.
  - `EnemyClampTravel`'s two-attitude change is **not covered by a test**. It reads `_tableState` and the
    live request, so it is not the pure arithmetic that `ModelRoster` / `PlacementPanelLayout` are pulled
    out for, and mocking a table state to reach it would test the mock. What protects it is that it cannot
    propose more than the gate accepts by construction: each attitude's clamp is individually a distance the
    engine's either-attitude rule allows, and the gate itself is tested
    (`MoveThroughEnemyValidationTests.SweptFootprintClipsEnemyOnlyAtTheArrivingAttitude_Accepted`).

- **The OR is per-enemy in the enemy validator, and globally over the piece set for terrain.** The terrain
  walk asks "is there one attitude clear of everything", which is the strict reading. `ValidateMovingThrough-
  EnemyUnits` evaluates it inside its per-enemy loop instead, because every other clause of that check
  (the start/end gap guard, the aircraft branch, the ending-stacked rule) is per enemy — "did I pass through
  THIS enemy" is the question it is built around. The gap: a leg crossing enemy A only at the arriving
  attitude and enemy B only at the departing one passes, though no single attitude is clear of both. Two
  enemies and a rotating rectangle threading between them; the end pose is still checked. Left as is rather
  than restructure a validator with this much documented history for it.

- **Two existing tests moved rather than broke, and the surprise was which.** The predicted casualty was
  `FindFirstCrossing_LateRotationOffset_FlagsTheEarlierSegment` ("a late rotation makes an EARLIER,
  already-green segment collide" — the bug, asserted). It still passes: the 45deg pose at the node it turns
  at lands inside the pillar, so the collision survives, reattributed from the leg to the node. Renamed and
  extended with the case that does change (turn past the pillar instead and the whole path is now legal).
  The one that actually failed was `FindFirstCrossing_ReportsPieceSegmentAndContact`, whose scenario became
  a node-pose collision reporting contact at the node rather than mid-leg; it now rests the model facing its
  direction of travel so the two attitudes agree and it stays a pure leg test, with a new sibling covering
  the node-pose reporting shape.

## Verification

- Engine 2831/2831, app 1077/1077, `dotnet build` clean, headless smoke exits 0 (run at every commit).
- Commits: engine `c0b0304` (rule) -> `617e51f` (consolidation tests) -> `1ffbc8e` (beat facings);
  superproject `fa3abbb` (clamps + bump) -> `8fc736f` (glide rotation + bump).
- **Not verified in the GUI.** The owner's scenario is a hand-verify: park a rectangular model beside a
  wall, rotate, and place a node clear of the wall's end - the ghost must stay green and Done must accept.
  Then repeat with the node still alongside the wall: the ghost must go red with the offending piece washed
  and the model's oriented footprint drawn AT that node. Watch the move play: the model should turn across
  the leg rather than arrive pre-turned.

## Outcome

_(pending)_
