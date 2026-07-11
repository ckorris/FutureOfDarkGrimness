# 213 — Moving through impassible terrain: show it as invalid (red, unclickable) + block the AI too

**Status:** open (filed 2026-07-11 from Chris's play report)
**Related:** #211 (solo AI mover submits a path through impassible terrain ~1/1800), #159 (DefinePathStage
cohesion/validate-or-decline ladder), #050 (base-radius-aware terrain geometry), #155 (difficult-terrain clamp)

## Report

When a model's move would take it through impassible terrain - in EITHER group or single-move mode - the GUI
lets you place the move but then won't let you press Done. That's confusing. Instead:

- The ghost/model should turn RED and be un-clickable (you can't commit the placement).
- Its movement line should be drawn in RED.

So the "can't place here" signal is immediate and visual, matching how the enemy-overlap red already works,
instead of a silent failure at the Done button.

While fixing this, also make sure the **AI resolvers cannot move through impassible terrain** (the engine
authoritative check should reject it and the resolvers should back off) - see #211, which is the AI half of
the same family (solo mover submits an impassible-crossing path ~1/1800).

## Where to look

- `GuiDefineMovementResolver`: it already flags enemy-overlap red (`OverlapFill`) and blocks placement; extend
  the same treatment to impassible-terrain crossings for the ghost AND the movement line, in both group and
  single-move modes. `PlacementUtilities.OverlapsImpassibleTerrain` / the swept-path terrain check are the
  levers (per-segment, base-radius-aware per #050).
- AI side: `AiDefineMovementResolver` / `MovementPlanner.ValidateWithBackoff` already validate against
  `MovementUtilities.ValidatePaths` (which includes the impassible check) - #211 is the residual leak; verify
  the ladder truly never submits an impassible-crossing path.

## Notes

- 2026-07-11 — filed. The AI half overlaps #211; keep them coordinated (one validate-or-decline ladder).
