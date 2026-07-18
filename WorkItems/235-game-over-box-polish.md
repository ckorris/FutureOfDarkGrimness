# 235 — Game Over box: center the text, make it draggable

**Status**: todo
**Related**: #040 (post-game navigation, done - built this card), `RaylibRenderer.DrawGameOverOverlay`

## Goal
Two cosmetic fixes to the Game Over card: (1) the result text is left-aligned in the centered card and looks off - center it; (2) the window is pinned (`ImGuiWindowFlags.NoMove` + forced position each frame) so it can't be moved off the battlefield - make it draggable so the player can inspect the final board state before clicking Return to Main Menu.

## Notes
- 2026-07-18: Implemented in `RaylibRenderer.DrawGameOverOverlay`: position now forced only on appear
  (`ImGuiCond.Appearing`) and the `NoMove` flag dropped, so the card is draggable from any empty spot
  in its body (the app never sets `ConfigWindowsMoveFromTitleBarOnly`); "Game Over" title and the
  result line are centered via a new `CenteredText` helper (long results fall back to left-aligned
  `TextWrapped`). Build green. Awaiting GUI hand-verify (end a game: text centered, card draggable,
  re-centers on the next game over).
- 2026-07-15: Filed from user playtest feedback.

## Decisions

## Outcome
