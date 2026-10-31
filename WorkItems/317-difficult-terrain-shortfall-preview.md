# 317 — Show WHY a move snaps back at difficult terrain

**Status**: done (hand-verified in the running app 2026-08-02)
**Related**: #155 (difficult/dangerous terrain indication — this builds on its clamp + panel lines)

## Goal
Moving around difficult terrain was awkward in the 2026-08-02 playtest: the ghost silently snaps back to a
shorter position and nothing on the table says why. Done looks like — whenever the difficult-terrain clamp
shortens the preview, the table also shows the pose the ghost WOULD have taken, in pale gray, joined to the
real ghost by a dotted gray line, with a two-line gray label naming the rule and what it cost. Both movement
modes (single and group).

## Notes

- 2026-08-02: Impassible terrain gained the matching label after owner playtested that path. It blocks rather
  than shortens (no phantom to draw), so it is text only: "Impassible Terrain" / "Cannot move through it" in
  light red beside the red contact footprint, drawn once per frame. `ImpassibleBlockLabel` + 3 tests; label
  layout shared with the difficult one via `DrawTerrainReasonLabel`. App suite 899/899, engine 2578/2578,
  headless smoke exit 0.
- 2026-08-02: Implemented. New `FdgRaylib/Rendering/DifficultShortfallPlan.cs` (show/hide rule + label
  wording, display-independent, mirroring the `ReachRingPlan` idiom) + drawing in
  `GuiDefineMovementResolver` (`DrawDifficultShortfall` / `DrawDifficultShortfallLabel`). Single mode keeps
  the pre-clamp travel and re-runs the enemy + table clamps on it; group mode re-solves the whole rigid step
  with `Feasible(..., includeDifficult: false)`. 9 new tests (`DifficultShortfallPlanTests`); app suite
  896/896, engine 2578/2578, headless smoke exit 0.

## Decisions

- **The counterfactual runs through the OTHER clamps.** The gray phantom is not "mouse position" and not
  "pre-clamp travel" — it is the move re-solved with *only* the difficult-terrain clamp switched off. A step
  that the band cap, an enemy base or the table edge shortened comes back identical to the real one, so no
  phantom is drawn and difficult terrain is never blamed for a limit it didn't impose.
- **Two sentences, not one.** The clamp already distinguishes `CappedCrossing` (moving through, whole move
  held to 6") from `StoppedShortOfEdge` (cap already spent, held at the edge). Reusing "Can only move 6"" for
  the second case would tell a model that has already moved 6" that it may move 6". Owner picked
  `Cannot enter - 6" used` (2026-08-02).
- **One label per unit in group mode**, at the centroid of the gray phantoms — a copy per model buried the
  formation in text. The cap sentence wins when a unit has models in both cases at once, since it is the one
  that explains the shape of the whole step.
- **0.15" minimum shortfall.** Below that the phantom sits on top of the real ghost and the dotted link is a
  smudge; the snap-back also isn't what confused anyone at that size.
- Group mode was a genuine fork (more code: the difficult-free re-solve). Owner chose both modes — the whole
  formation snapping back is arguably the more confusing case.
- **Impassible keeps its own colour.** The label is red (the wash it explains), not the shortfall gray: gray
  means "shortened, this is where you'd have been", red means "refused". Same two-line shape and same position
  above the model, so they read as one family without implying the same consequence.
- **One impassible label per frame**, not per crossing: a blocked group step reports a crossing per phantom.

## Outcome
Shipped and closed 2026-08-02, all five checks below hand-verified in the running app by the owner. A move
the difficult-terrain clamp shortens now draws the pose it would have taken in pale gray, dotted-linked to the
real ghost, under a two-line label naming the rule and what it cost — `Can only move 6"` when moving through,
`Cannot enter - 6" used` when the cap was already spent — in single mode (per selected model) and group mode
(one phantom per held-back model, one label at their centroid). Impassible terrain, which refuses the
placement rather than shortening it, gained the matching text in red beside the existing contact footprint,
drawn once per frame. Show/hide rules and wording live in `DifficultShortfallPlan` + `ImpassibleBlockLabel`
(12 tests) so they are checkable without ImGui; the resolver keeps the drawing, with both rules sharing
`DrawTerrainReasonLabel` so they can't drift apart in voice or placement. The gray phantom is a true
counterfactual — the move re-solved with only the difficult clamp switched off — so a step shortened by the
band cap, an enemy base or the table edge shows nothing and terrain is never blamed for a limit it didn't
impose. Nothing deferred. Filed as #315, renumbered on merge (Reconciliations 39).

Hand-verify checks (all passed 2026-08-02):
1. Single mode, walk a model into a difficult piece: gray phantom ahead of the green ghost, dotted gray link,
   "Difficult Terrain" / "Can only move 6"".
2. Single mode, spend the 6" first, then aim into the piece: same visuals but "Cannot enter - 6" used".
3. Group mode into difficult terrain: one gray phantom per held-back model, formation shape preserved, one
   label at their centroid.
4. A move shortened by the table edge or an enemy base only (no difficult terrain in the path): no gray
   phantom, no label.
5. Aim a path through an impassible piece: red piece + red contact footprint as before, now with
   "Impassible Terrain" / "Cannot move through it" above it. In group mode, exactly one copy of that text.
