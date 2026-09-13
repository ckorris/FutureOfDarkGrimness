# 295 — Click a model to switch to it; Space joins Enter as Confirm

**Status**: in-progress (implemented + tested; awaiting GUI hand-verify)
**Related**: #248 (resolver hotkeys / letter + number keys), #240 (edge-only commit keys), #149/#150 (base shapes + facing), #215/#277 (group mode). Filed locally as 294; renumbered to 295 per reconciliation 29 (`Reconciliations.md`) when origin/master turned out to have landed 294 = movement footstep cue.

## Goal

Two changes that have to land together, because the first is what makes the second possible:

1. **Single-model mode selects by clicking the model.** Space used to cycle to the next model in the
   unit's list. Clicking a model already selected it, so the cycle key was the discoverable-but-clumsy
   half of an interaction that already had a better half. Clicking becomes the only way, the panel text
   says so, and unselected models get a **hover highlight** so they read as clickable.
2. **Space, now free, joins Enter as the universal Confirm key** in every resolver, with the advertised
   text following ("(Enter/Space)").

Done means: no resolver binds Space to anything but Confirm; every Confirm affordance in the GUI reads
"(Enter/Space)"; hovering an unselected model in single-model movement/consolidation highlights it; and
the highlight can never point at a different model than the click would select.

## Notes

- 2026-07-27: Implemented, all suites green (engine 2227/2227, app 649/649 incl. 10 new), headless smoke
  exits 0. App-side only -- no engine change was needed.
  - New `ResolverKeybinds` (+ `ResolverKeybind`): the binding table. `Confirm` = Enter / KeypadEnter /
    Space, `Back` = Backspace, each carrying its own `Hint` ("Enter/Space") and `Parenthetical`
    ("(Enter/Space)"). The binding owns the muting (typing / Esc menu open) and the #240 edge-only rule.
  - `ResolverHotkeys.IsEnterPressed` -> `IsConfirmPressed`, delegating to the table; `IsBackPressed` too.
    `ResolverButtons.Primary` derives its label suffix from `Confirm.Parenthetical` instead of a literal
    "(Enter)", which is what makes every Done/Confirm/Auto-assign button update for free.
  - Hand-written "(Enter)" strings retired at the three sites that had them: `Fire!` (ranged), `Cast`
    (spells), and the two Done tooltips (movement, consolidation). `Cancel (Backspace)` now derives from
    `Back.Parenthetical` too. Esc-menu Options "Controls" list updated, plus a new line for click-to-switch.
  - `ModelPicker.HitTest` extracted from the two copies of the hit-test loop that movement and
    consolidation each carried inline; both resolvers now compute the hovered model ONCE per frame, before
    anything is drawn, and feed the same answer to both the highlight and the click handler.
  - Space bindings removed from `GuiDefineMovementResolver` and `GuiConsolidationMoveResolver`.
  - Movement's single-mode hint lines were stale independently of this work: both copies read
    "R-click: waypoint" when right-click actually undoes. Rewritten (and de-duplicated into
    `DrawSingleModeHints`) as "L-click a model: switch to it / L-click elsewhere: place waypoint" +
    "R-click: undo / Backspace: undo / back".

## Decisions

- **Hover is a dimmer, filled version of the selection outline, not a new hue.** Movement already spends
  green/yellow/orange on move bands and red on illegal placement; consolidation spends cyan on the move
  preview. A fourth colour would have had to mean something. White-at-0.75 with a 0.18 wash reads as
  "this is about to BE the selection", which is exactly what the click does.
- **One hit test, computed before the draw pass.** The tempting version computes the highlight in the
  draw loop and re-runs the hit test in the click handler at the bottom of `Draw` (which is what the code
  did before -- the loop existed, just inline). Two copies of a float comparison drift; a highlight that
  points at a model the click won't select is worse than no highlight at all. So `hoveredModel` is
  resolved once, up front, and both consumers read it.
- **Consolidation got the same treatment even though only movement was asked for.** It had its own
  `Space: next model` binding; leaving it would have meant Space both confirming and cycling depending on
  which panel was open. Same fix, same shared `ModelPicker`.
- **Bind the intent, not the key.** The layer is deliberately tiny (two intents) and covers only bindings
  shared ACROSS resolvers -- those are the ones whose advertised text goes stale. Panel-local keys (option
  numbers, R, G, Y/N) stay where they are; pulling them in would centralise things that have no reason to
  move together.
- **The ghost still follows the mouse while hovering a model.** Clicking there selects rather than placing,
  so the ghost is momentarily advisory-only. Suppressing it would flicker `_ghostSnapshot`, which feeds the
  tactical overlay and the #280 remote preview stream. Pre-existing behaviour, left alone; the highlight
  carries the signal instead.

## Outcome

_(open)_
