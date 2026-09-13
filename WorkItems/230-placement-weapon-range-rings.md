# 230 — Show weapon ranges when deploying, embarking, or ambushing

**Status**: implemented + tested, awaiting GUI hand-verify
**Related**: #162 (the opportunity field this reuses), #247 (range/threat overlay UI rethink), #252 (anchored field vs #201 cover proximity), #223 (picker stat tooltip), `GuiPlaceObjectsResolver`

## Goal
During placement decisions (deployment, ambush arrival, disembark/embark placement) the player should be able to see the unit's weapon ranges on the canvas - e.g. range rings from the ghost/placement position - so they can judge what the spot actually threatens before committing. The hover ring machinery exists for on-table units (`DrawRangeRings`); placement ghosts aren't on-table yet, so the rings need to anchor to the candidate position instead. Decide whether rings follow the cursor ghost live or draw on demand (modifier key?) - live rings on a whole-unit group ghost could be noisy.

## Notes

- 2026-07-25/26: Implemented by extending #162's ghost-anchored opportunity field to placement, rather
  than drawing new geometry.
  - **False start, recorded because the reasoning matters.** The first attempt built a new
    `ThreatEnvelope` primitive: the union of the carriers' reach circles as a polar sweep, drawn as plain
    outlines. It worked and was tested, but it duplicated - badly - something that already existed.
    `TacticalOverlayController.RebuildGhostField` is *already* this feature: "what can I hit from here",
    anchored on ghost positions, rebuilt every frame, one band per distinct effective weapon range, LoS
    from each ghost via `PolarSightMap`, cover crosshatch, GPU-rasterized, band labels naming the weapons.
    It simply never fired during placement, because `DrawField` early-returns unless
    `_moveResolver.ActiveRequest` is a live `DefineMovementPathRequest`. A geometric outline is also
    strictly worse than what the field shows: no LoS, no cover, so it draws reach straight through a
    building. Commit reverted (unpushed), `ThreatEnvelope` + its 9 tests deleted.
  - **The seam**: new `IGhostFieldSource` (`FdgRaylib/Rendering/Resolvers/`) - "this resolver has pending
    ghosts worth anchoring a field on" - surfaced as `GuiResolverOverlay.ActiveGhostField`, the same
    active-resolver-opt-in pattern as `ActivePreviewSource` / `ActiveEnemyExclusion`. So the field appears
    and disappears with the placement, and a future opt-in resolver (consolidation, aircraft advance) needs
    no controller change. `GuiPlaceObjectsResolver<T>` implements it from a by-model position map rebuilt
    each `Draw` from this frame's ghosts and committed placements - the live cursor ghost / group phantoms
    win over a dropped position, so a picked-up model contributes at the cursor.
  - **The controller**: `DrawField` falls through to `TryDrawPlacementField` when no move job is running;
    `RebuildGhostField` generalized from `(req)` to `(unit, ghosts, req?)`. The req fed only pin-related
    things (`WeaponRangeOverrides` via `EffectiveRange`, and `BuildSecondaryContours`), and pins are scoped
    to a move job, so null takes the no-pin path rather than needing a placement equivalent.
  - **Placement is always ghost-anchored**, ignoring `TacticalOverlayConfig.GhostAnchoredField` (default
    off). That flag picks between "where can I stand to shoot the pin" and "what can I hit from here"; the
    first has no meaning with no pin, and during a placement the ghosts ARE the question.
  - **`DrawBandLabels` gate fixed**: it keyed on `_moveResolver.ActiveRequest != null`, so the captions
    naming each band's weapon ("24in 5x Rifle") - most of the field's readability - would have gone missing
    during placement. Now gates on `_fieldActive`, set when `DrawField` paints and cleared with the picture.
  - `ViewSettings.ShowPlacementRanges` (default on) + hotkey `V` + a checkbox in the placement panel. V was
    free: the in-game key census is A/F/G/L/R/T, arrows, Enter, Backspace, Escape, Space, digits, F10;
    Ctrl belongs to #277's formation cycle and Alt to camera/ruler.
  - Covers deployment, ambush arrival, disembark, transport-wreck spillout, teleport/reposition and the
    Aircraft edge redeploy in one place - all `PlaceObjectsRequest<ModelData>`. Embark raises no placement
    request today (the move-into-contact half is #097), so despite the title it is out of reach until that
    lands; recorded rather than silently dropped.
  - Verified: `GhostFieldSourceTests` 5/5 new, FdgRaylib.Tests 597/597, engine 2144/2144, full
    `dotnet build` clean, headless smoke exit 0.

- 2026-07-15: Filed from user playtest feedback.

## Decisions

- **Reuse #162's field, don't draw new geometry** (owner call 2026-07-26, after catching the duplication).
  The overlay already computes the rules-true answer - LoS per ghost, cover shading, effective ranges - on
  the GPU. A second, dumber range vocabulary would also have made #247's "reads as cryptic" complaint
  worse, not better, by adding a third grammar to the same surface.
- **Route by resolver opt-in, not by resolver type.** The controller could have taken a
  `GuiPlaceObjectsResolver` reference the way it takes the movement one. Going through
  `ActiveGhostField` instead means the "field is live" condition is exactly "the resolver showing ghosts is
  the pending one", which is the condition that was wanted anyway, and it doesn't grow a new attach call
  per resolver.
- **Ghosts one frame stale, accepted.** The canvas pass draws the field before the ImGui pass runs the
  resolver's `Draw`. Re-ordering to close that would mean moving the field draw out of its spec'd layer
  (under terrain). Movement has always had this and it is invisible at frame rate.

## Open / follow-ups

- **2026-07-26: extended by #247 slice 1.** The V toggle built here became the global reach toggle, the
  placement anchor became one entry in `FieldAnchorPlan`'s contest, and hovering any unit now outranks the
  placement ghosts. `ViewSettings.ShowPlacementRanges` was renamed `ShowReachOverlay` to match. The
  placement-specific behaviour and its hand-verify list below still stand.

- **#252 applies here too**: the anchored field's cover tint ignores #201's proximity rules, so a placement
  field over-paints cover exactly as the movement one does. Not made worse by this change; noted so the
  placement case is covered when #252 is done.
- **Threat frontiers (F) still key off a move job or the activating unit** (`ResolveReference`), so enemy
  threat does not auto-show during deployment. Deliberately out of scope - #230 is about my own reach - but
  it is the obvious sibling if deployment-time threat is wanted.

## Outcome

Implemented 2026-07-26. Awaiting GUI hand-verify:

1. Deploy a unit with two weapon ranges: the field follows the whole-unit ghost in Group mode, with one
   band per range, band captions naming the weapons, and the cover crosshatch / LoS shadow behaving as it
   does mid-move.
2. Put a building between the ghost and open ground: the shadowed (no-LoS) region must appear behind it -
   this is the whole reason for reusing the field rather than drawing circles.
3. Single mode (G): the field tracks the one ghost, then grows as each model is dropped. Pick a placed
   model back up - the field follows the cursor, not the vacated spot.
4. Press `V` and the panel checkbox: field off/on, state surviving into the next placement and next game.
5. Ambush arrival and Teleport/reposition: the field draws there too, alongside #214's green reach rings,
   without the two reading as the same thing.
6. Commit the placement: the field clears immediately, and a following move job's field behaves normally
   (both anchor modes - check the Esc > Options "Anchor field on my position" checkbox both ways).
7. Objective / terrain placement: no field, no checkbox (there is no unit to show reach for).
