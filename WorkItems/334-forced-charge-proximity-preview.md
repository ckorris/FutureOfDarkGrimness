# 334 — Show the 1" forced-charge band while moving

**Status**: in-progress
**Related**: #206 (forced-charge moved from a move-time rejection to a Choose Action gate), #155 (the
terrain-consequence warning pattern this copies), #150 (oriented base footprints), #326 (model roster)

## Goal
Playtest note (2026-08-04, Chris): *"When moving, it needs to be very clear when you're within 1 inch and
it's going to force you to make a charge."*

Today the obligation is invisible until it is too late. `MovementUtilities.ValidateMovingThroughEnemyUnits`
deliberately does NOT reject a non-charge move that ends inside the 1" standoff band (#206 moved that
decision downstream), so Done accepts the move without comment. The consequence only appears one stage
later, in `ChooseActionStage.GetCanPass`, as a greyed-out Pass reading *"Within 1" of an enemy - must charge
(or reposition) rather than stand idle."* By then the move is committed.

Done means: while the player is still aiming the move, the 1" band is visible on the table, the ghost says
when it is inside it, and the panel names the consequence in the same slot the terrain warnings use.

## Decisions (owner sign-off 2026-08-04)
- **Band scope: enemies in reach only.** The faint 1" band is drawn around enemy models the unit could
  actually close with this activation (remaining budget + the band itself), not around every enemy on the
  table. Rejected: always-on (clutter around enemies that cannot be reached) and reactive-only (no help
  planning the approach, which is the half of the problem the playtest note is about).
- **Done stays live.** A loud panel warning, no confirmation dialog and no gating. The move is legal - #206
  is explicit that the standoff band is a consequence, not a rejection - so blocking Done would contradict
  the engine and make a legal, sometimes deliberate, move feel like an error.

## Approach
**Engine first.** The predicate behind the gate is private to `ChooseActionStage`
(`AnyEnemyWithinStandoff`), so a front end can only re-derive it and drift. Extract
`Utilities/ForcedChargeUtilities`:
- `IsInsideStandoff(gap)` - the single `< ENEMY_STANDOFF_DISTANCE_INCHES` comparison.
- `AnyEnemyWithinStandoff(gameContext, player, unit)` - lifted verbatim; `GetCanPass` now delegates, so the
  gate and the preview cannot disagree.
- `FindContacts(movers, enemies)` over `StandoffPose` (centre + base shape + facing) - the hypothetical
  form the resolvers need, measuring with the same shape-aware 3D base-to-base call
  (`DistanceUtilities.GetBaseToBaseDistanceInches_3D`) the live-position path uses.

**App.** `GuiDefineMovementResolver` computes the contacts per frame from the same ghost-aware final
positions the cohesion check already builds, in both single and group mode. `ModelBaseRenderer` gains an
exact rounded-hull band draw: `IBaseShape.Footprint` already returns a rounded convex hull, so the 1"
Minkowski inflation is just `Rounding + 1"` - exact for circles and oriented rectangles alike, where the
existing `inflateInches` outline would square off a rectangle's corners and overstate the band.

CLI parity: `DefineMovementPathResolver` prints the same warning when the accepted move ends inside the
band (both front ends carry every rule the other does).

## Notes

### 2026-08-04 - implemented (engine `e912127`; awaiting GUI hand-verify)
Engine 2804/0, app 1038/0, headless smoke exits 0 with the warning firing in play.

Built as planned. Three things the build itself taught, all folded in:

- **"It must Charge" is not always true.** The headless smoke showed a rifle-only unit take the warning and
  then hit the engine's zero-options fallback: Pass is gated by proximity ALONE, but Charge needs a melee
  weapon (`GetCanCharge`), so a unit with no melee weapon inside the band can do neither. Both front ends now
  say *"cannot Pass, and has no melee weapon to Charge with"* in that case - which is the more useful warning
  of the two, since it describes a trap rather than a choice.
- **The band outline had to be exact, not approximate.** `ModelBaseRenderer.DrawOutlineImGui`'s existing
  `inflateInches` pushes a rectangle's half-extents outward and leaves SQUARE corners, putting them 1.41" from
  the base - it would have drawn the boundary claiming ground the rule leaves legal. New `BandOutline` builds
  the true Minkowski outline off `IBaseShape.Footprint`'s rounded convex hull (corners + `Rounding + band`),
  which is exact for circles and oriented rectangles and free for any future shape. Split pure (no ImGui) the
  way `ModelRoster` is, so `ForcedChargeBandTests` can feed every emitted point back through the shape's own
  `DistanceToLocalPoint` and assert it lands at 1.00".
- **Band colour is magenta on purpose.** Orange was the tempting choice and is wrong: on this canvas orange is
  the CHARGE DISTANCE band - a budget, not an obligation - and conflating "how far you may move" with "what
  you will be forced to do" is the exact confusion this item exists to remove.

Ordering inside the frame: bands go down before the paths and ghosts (they mark ground), the violated bands
and link lines go on top after the ghosts are known. The live ghost tints itself from its own cheap check
rather than the contact pass, which has not run yet at the point it is drawn.

### 2026-08-04 - filed
Number taken from `origin/master` (index + archive max = 332; 333 went to the reconciliation-47 renumber of
the deploy-normally item).

Observed while reading the gate, NOT fixed here (deliberate, would be its own item): `AnyEnemyWithinStandoff`
does not exclude Aircraft, but `GetCanCharge` does. A unit that ends within 1" of an Aircraft therefore
cannot Pass and cannot Charge it. The preview mirrors the gate rather than second-guessing it, so it will
warn there too - which is the honest reading of what the engine will do.
