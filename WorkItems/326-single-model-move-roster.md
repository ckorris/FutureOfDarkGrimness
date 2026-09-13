# 326 — Model roster for single-model moves

**Status**: in-progress — movement roster hand-verified by owner; consolidation (slice 2) still open
**Related**: #295 (click-to-select freed Space), #286 (two-way canvas hover), #288 (footer costed first),
#298 (line-height multiples), #155 (difficult-terrain cap), #093 (per-model budgets), #277 (group mode)
**Was #325** pre-reconciliation-44 (see `Reconciliations.md`).

## Goal

Make "which model am I moving, and which ones still need moving?" answerable without hunting the table.
#295 replaced the Space cycle with a click on the model itself; a playtester never found the gesture and
the owner found it annoying. Done looks like: the movement panel shows the unit's models as a list with
per-model distance travelled, selection works by row click / keyboard / table click, and Space still means
Confirm everywhere in the client.

## Notes

- 2026-08-02: **Renumbered 325 -> 326 (reconciliation 44).** Filed as 325 off a freshly fetched
  `origin/master` at `0f21304`, where 325 was free; the shooting-forecast session's reconciliation 43
  landed on origin during this session and renumbered its item INTO 325. Merged wins, so this one
  yielded. Third instance of the same race after 39/40 and 43 - a fetch at filing time bounds nothing
  about what lands while the work is in progress.
- 2026-08-02: **Movement roster hand-verified by owner in the running app** ("works great"). Remaining
  open scope is slice 2 (consolidation), so the item stays out of the archive.
- 2026-08-02: Implemented for `GuiDefineMovementResolver`. New `ModelRoster` (pure arithmetic, no ImGui:
  row height, footer cost, roster height, wrap-around cycle, row text, `FormatInches`) +
  `DrawModelRoster` in the resolver. `ResolverHotkeys.CycleDelta()` binds Up/Down and Tab/Shift+Tab
  (repeat on) with `CycleHint` for the advertised text. `ModelRosterTests`: 18 cases, green.
- 2026-08-02: Two-way hover wired. Table->row was free (the table hit test already runs at the top of
  `Draw`, the panel at the bottom); row->table needed the #286 single-frame handshake via
  `_panelHoveredModel`. Split `highlightModel` (may come from the roster) from `hoveredModel` (table hit
  test only) so the roster can never feed the click handler — see Decisions.
- 2026-08-02: `DrawInfoPanel` now takes the whole `cappedByDifficult` set instead of a single
  `selectedCapped` bool, so every row shows its real 6" cap rather than only the selected one.
- 2026-08-02: Fixed a wrong line in Esc -> Options: it read "G: cycle formation (during moves)". G toggles
  Group/Single; Ctrl+wheel cycles the formation (#277). Both are now listed correctly, alongside the new
  pick-model keys.
- 2026-08-02: `FormatInches` de-duplicated — the resolver's private copy now forwards to
  `ModelRoster.FormatInches`, so the roster's "/ 12"" and the panel's own readouts cannot diverge.

## Decisions

- **Roster before keybinding.** The reported symptom was a key, but the defect was that the set of models
  had no representation outside the table, and the only affordance (a hover highlight) shows up when the
  cursor is already on a base — which during a move is busy aiming the waypoint ghost. Canvas + object
  list (Figma layers / Blender outliner) is the standard answer, and this codebase already codified the
  selection half of it in #286. Side benefit: the table's left click has ONE meaning again (place a
  waypoint) instead of "place unless you happened to be over a base".
- **Space stays Confirm.** Rebinding Space to cycle in this one panel was considered and rejected:
  `ResolverKeybinds.Confirm.Hint` is a single string feeding every button label, tooltip and Options line,
  so a per-panel exception makes all of that text lie — the exact property #295 built the table for. The
  cycle is ADDITIVE: Up/Down (what a visible list already means everywhere else in this client, per
  `KeyboardListNav`) plus Tab/Shift+Tab (universal "next", unbound in this client, works without looking
  away from the table). A/D and W/S were also considered — both collide with the pinned Choose Action
  letters, and W/S invites the expectation that WASD pans the camera, which it does not.
- **The roster feeds the highlight, never the click.** `_panelHoveredModel` is a frame old by
  construction and is recorded while the pointer is over the PANEL. Letting it reach the click handler
  could select a model the player never aimed at, so `Draw` keeps two variables: `highlightModel`
  (table hit test, else last frame's roster row) for the wash, `hoveredModel` (table hit test only) for
  the click.
- **No auto-advance after a committed waypoint.** Tempting — it would make "place, place, place" walk the
  unit — but multi-waypoint paths around terrain are a core feature of this resolver, and auto-advancing
  would make a model's second waypoint unreachable. Tab is the explicit "next" instead.
- **Two decimals in the roster.** F1 scans better in a list, but the selected model's detail line has
  always shown F2; a roster reading 6.0" beside a detail line reading 5.96" reads as a bug.
- **Footer costed first (#288).** A big Tough unit is exactly the case where a naive roster pushes Done
  off the bottom and the move becomes uncommittable — silently. `ModelRoster.FooterHeight` prices the
  hint block, mode button, both checkboxes and the button stack; the roster takes the remainder, capped
  at `MaxVisibleRows` = 5.

## Deferred (explicitly, not dropped)

- **`GuiConsolidationMoveResolver` gets the same roster** — it carries the identical click-to-select
  gesture (`GuiConsolidationMoveResolver.cs:330`). Owner-agreed slice 2; until it lands, consolidation
  keeps the click-only affordance and its hint line still names the click.
- Models are numbered "Model N" (the wound-assignment list's vocabulary) because `IModel` carries no
  name. A joined hero is only distinguishable by its different max in the distance column. Naming models
  is a separate question, not filed.

## Outcome

_Open: movement half verified, consolidation slice 2 still to come._

The movement roster shipped and was confirmed by hand in the running app on 2026-08-02. The checks it was
verified against, kept here for the consolidation slice to be held to the same bar (single-model move, a
unit of 4+ models):
1. The panel lists every living model with its distance; unmoved models are greyed, moved ones green,
   a model past its Advance orange.
2. Up/Down and Tab/Shift+Tab walk the list and wrap at both ends; clicking a row selects it; clicking the
   model on the table still selects it.
3. Hovering a row washes that model on the table; hovering a model on the table highlights its row and
   scrolls it back into view on a unit big enough to overflow the list.
4. Space still commits the move (Done), and the Done button still reads "(Enter/Space)".
5. On a 10+ model unit the roster scrolls and Done / Back / Skip / Clear all stay on screen.
6. Move a model through difficult terrain: its row's maximum drops to 6", not just the detail line's.
