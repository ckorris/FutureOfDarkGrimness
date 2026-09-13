# 343 — Deployment action-stack undo + canonical undo gestures

## Goal

Deployment's Undo popped `_placed[last]` — the roster-ordered list, not a history — so it diverged
from "reverse the last thing I did": undoing a group drop deleted one model (and stranded the rest,
since the group ghost is gated on `_placed.Count == 0`), a drag-edit was not undoable at all (Undo
after a drag deleted a *different* model), and Auto-place took K clicks to reverse. Replace with a
command history at action granularity, and pin the app-wide gesture scheme:

- **Right-click = undo the last action** (deployment now; movement/consolidation already had it).
- **Backspace = back out** (edge-only, where `AllowCancel`) — its undo role in movement and
  consolidation is removed (owner call 2026-08-05: "right-click is enough"; supersedes #248's
  undo-first-back-second and the #161 deploy fix sketch, which was worded as "remove last placed
  model" — action granularity replaces that).

## Scope (signed off 2026-08-05)

- `PlacementHistory<T>` (new, arithmetic-only like `ModelRoster`): actions `Place`,
  `GroupDrop(rotation)`, `DragMove(index, beforeEntry)`, `Restart(snapshot)`. Undo of a group drop
  clears the list — the group ghost returns automatically via the `_placed.Count == 0` gate — and
  restores the drop's rotation. Restart is undoable. No redo (movement has none either).
- Right-click during a drag = cancel the drag (model stays put; nothing recorded).
- Undo button stays (discoverability), driven by the history; hints updated.
- **Auto-place button removed.** The GUI resolver's `AutoPlaceRemaining`/`TryFindAutoPosition` was
  reachable only from the button — AI players use engine-side `AiPlaceObjectsResolver` /
  `TacticianPlaceObjectsResolver`, headless EOF uses the CLI resolver's own `FindAutoPosition` — so
  it is deleted as dead code. AI + CLI auto-place untouched.
- CLI placement: no change (sequential typed coordinates; no undo concept; EOF-driven play never
  types).

## Notes

- 2026-08-05 — **Implemented + tested; awaiting GUI hand-verify.** New `PlacementHistory<T>`
  (`FdgRaylib/Rendering/Resolvers/`, arithmetic-only) + 8 `PlacementHistoryTests`; recording points in
  `GuiPlaceObjectsResolver` (place / drag / group drop / Restart), right-click handler (undo, or
  cancel a live pick-up), Undo button relabelled "Undo (R-click)" and driven by `CanUndo` (so an
  undone Restart is reachable with an empty placed list), Auto-place button + `AutoPlaceRemaining` +
  `TryFindAutoPosition` deleted. Movement: Backspace block reduced to back-out via
  `ResolverHotkeys.IsBackPressed()`; group hint now advertises R-click/Backspace. Consolidation: both
  Backspace-undo paths removed (no back-destination there, so Backspace now does nothing). Comments/
  docs updated: `ResolverKeybinds` (repeat example, Back binding), `PlacementPanelLayout` (footer
  wording; arithmetic unchanged — Undo kept the secondary row), `docs/ResolverGuide.md` "Undo vs back"
  paragraph, #161 C + canonical scheme amended, #248 superseded note. Suites: 1085 app + 2835 engine
  green; headless smoke exit 0.
  - Hand-verify checklist: (1) group-drop -> R-click lifts the whole unit back to the ghost, rotation
    kept; (2) drag a model, R-click mid-drag puts it back; (3) drag-drop then Undo restores the
    pre-drag spot/facing, not a different model; (4) Restart then Undo restores everything;
    (5) movement: Backspace with waypoints down abandons the move (no undo), R-click still clears
    the last waypoint; (6) consolidation: Backspace does nothing, R-click undoes.
- 2026-08-05 — Filed after design discussion; options A (action stack) / B (mode-aware patch) /
  C (no undo) presented, A picked. Backspace ruling clarified to app-wide.
