# 235 — Game Over box: center the text, make it draggable

**Status**: todo
**Related**: #040 (post-game navigation, done - built this card), `RaylibRenderer.DrawGameOverOverlay`

## Goal
Two cosmetic fixes to the Game Over card: (1) the result text is left-aligned in the centered card and looks off - center it; (2) the window is pinned (`ImGuiWindowFlags.NoMove` + forced position each frame) so it can't be moved off the battlefield - make it draggable so the player can inspect the final board state before clicking Return to Main Menu.

## Notes
- 2026-07-15: Filed from user playtest feedback.

## Decisions

## Outcome
