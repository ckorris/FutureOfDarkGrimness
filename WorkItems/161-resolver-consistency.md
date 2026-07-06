# 161 — Resolver UI consistency pass

**Status**: todo (audit recorded 2026-07-05; fixes not yet applied)
**Related**: spun off from this session's deploy/activate unit-picker stat labels (`GuiUnitSelectionResolver` + `UnitOptionLabel`) and the movement/consolidation click-consistency fix (`GuiDefineMovementResolver`, `GuiConsolidationMoveResolver`). Touches the whole `FdgRaylib/Rendering/Resolvers/` family.

## Goal
The GUI resolvers have drifted into inconsistent interaction/presentation patterns — the same class of drift the user flagged (stats shown on some selectors but not others; left-click to place in one mode, right-click in another). Sweep every resolver and bring them onto one consistent scheme: what shows stats, what left/right-click mean, what hover does, and how the dialog chrome looks. "Done" = the findings below are each fixed or explicitly waived, and a short canonical-scheme note lives in this file so new resolvers follow it.

## Audit findings (2026-07-05)

Trait matrix built across all `Gui*Resolver.cs`. Key inconsistencies:

### A. Stats-on-buttons parity (the flagged class)
- `GuiModelSelectionResolver` and (as of this session) `GuiUnitSelectionResolver` show a stat block on each dialog button; `GuiChooseRangedAttackResolver` shows weapon stats (`range", A{n} AP{n}`).
- **`GuiCancellableUnitSelectionResolver`** (cancellable unit picks — spell targets with Back, #100 pre-attack) shows **bare unit names, no stats** — it overrides neither `OptionLabel` nor `OnValidOptionHovered`. It is the near-duplicate twin of `GuiUnitSelectionResolver` and should carry the identical `UnitOptionLabel` stat block.
  - **Fix:** override `OptionLabel` (reuse `UnitOptionLabel.Format`) + `OnValidOptionHovered` (button->ring highlight), mirroring `GuiUnitSelectionResolver`.

### B. Button->unit hover-highlight parity
- The canvas selectors highlight the hovered unit/model ring from a hovered dialog button (`OnValidOptionHovered`) AND from a canvas hover (`GetHoverLabel`). `GuiCancellableUnitSelectionResolver` has the canvas->ring half but **not** the button->ring half (same gap `GuiUnitSelectionResolver` had before this session). Fold into fix A.

### C. Right-click semantics parity
Right-click means "undo/cancel the last action" across the canvas resolvers — **except deploy**:
- movement / consolidation: right-click clears the last waypoint (fixed this session).
- objective placement / one-terrain placement: right-click (or Esc) cancels the pending/selected ghost.
- **`GuiPlaceObjectsResolver` (deploy models): right-click does nothing.** Deploy already left-clicks to place (both single + group), so the missing half is undo.
  - **Fix:** right-click removes the last placed model (undo), matching the family's "right-click undoes the last thing."

### D. Duplication that caused the drift
`GuiUnitSelectionResolver` and `GuiCancellableUnitSelectionResolver` are near-identical (canvas rings, `GetHoverLabel`, `HandleClick`, `InchesToPixel`, hover-highlight). The ring/hover/highlight logic is copy-pasted again across `GuiModelSelectionResolver` and `GuiAssignWoundsResolver`. This duplication is *why* the cancellable twin didn't get stats/highlight when the plain one did.
- **Fix / consider:** extract a shared base (e.g. `GuiCanvasSelectionResolver<T>`) or shared helpers for canvas rings + button/canvas hover-highlight, so a future enhancement lands on every selector at once. Do A/B by way of this extraction rather than a third copy-paste.

### E. Lower-confidence / stylistic (list, decide per item)
- **Dialog chrome varies.** The `GuiSelectionResolver`-derived dialogs share a backdrop + `ChildBg` + rounding; the bespoke dialogs (`GuiAssignWoundsResolver`, `GuiChooseRangedAttackResolver`, movement/consolidation info panels, `GuiCastAssistResolver`, `GuiAircraftAdvanceResolver`) each roll their own window chrome (differing bg alpha, position, sizing). Consider a shared dialog-frame helper.
- **Undo affordances vary.** Movement/consolidation offer Backspace AND right-click; others differ. Pin the canonical scheme (below) and document it.

## Canonical interaction scheme (proposed — confirm on pickup)
- **Left-click** = place / select (on a model = select; on empty valid ground with a model selected = place a waypoint; single decisive picks just select).
- **Right-click** = undo/cancel the last action (clear last waypoint / remove last placed model / cancel a pending ghost).
- **Backspace** = same as right-click undo where a path exists (kept as a secondary binding).
- **G** = toggle Group/Single (shared). **R / Shift+R / wheel** = rotate facing.
- Every unit/model selector: stats on the button + button<->canvas hover-highlight + click-to-select-on-canvas.

## Decisions
- Filed as a standalone item (not folded into the session's changes) at the user's request, so the already-shipped deploy/activate stat labels + movement/consolidation click fix stay a clean unit and the remaining parity work is tracked separately.

## Outcome
_(open)_
