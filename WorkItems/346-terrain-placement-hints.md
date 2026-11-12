# 346 — Terrain placement: how to back out, and what the piece does

**Status**: in-progress (implemented; awaiting GUI hand-verify)
**Related**: #248 (Esc reserved for the in-game menu), #343 (right-click = undo everywhere), #301 (points mode)

## Goal
While placing terrain (both Alternating modes, which share `GuiPlaceOneTerrainResolver`), the panel must
say how to get back to the piece list, and should describe the piece being placed rather than only
naming its type.

## Notes

- 2026-08-05: Implemented in `GuiPlaceOneTerrainResolver`. New `DrawPieceDetails` (footprint, height,
  one line per rules effect) and `DrawBackHint`; new `FdgRaylib/Rendering/TerrainEffectText.cs` holds the
  per-flag effect copy. Right-click now also un-freezes a pending placement.

## Decisions

- **The old hint named the wrong key.** It read "Right-click or Esc to switch template" — Esc has opened
  the in-game menu since #248 and has never cancelled this panel. So the single line telling a stuck
  player how to get out was pointing at a key that does something else entirely, which is the likeliest
  root of "it's not clear how to cancel". Now reads "Right-click or Backspace", sourced from
  `ResolverKeybinds.Back.Hint` so it cannot go stale again.

- **Back hints are drawn in a visible colour, not `TextDisabled`.** Dimming is what hid it. They are the
  line a stuck player is hunting for.

- **The confirm step advertises both of its exits**, because there are two and they differ: Backspace /
  right-click re-positions (keeping the piece), the Cancel button returns to the piece list.

- **Right-click now un-freezes at the confirm step too.** It previously did nothing there, so "undo one
  step" (#343's universal gesture) worked at one placement step and not the other. Advertising a
  gesture obliges it to work; this is the smaller half of that fix.

- **`ETerrainType.Elevated` is described as having no rules effect**, because it has none — the flag is
  declared and read by nothing (see `DefaultTerrainPool`). Only a hand-authored layout can set it, and a
  piece that silently does nothing is worse than one that says so.

- **Effect copy verified against the engine**, not written from the flag names: Cover = +1 to the
  defender's save roll (`CoverCheckStage`), Dangerous = a wound on a 1 per model that moves through
  (`MovementExecutor.RollDangerousTerrain`), Difficult = the move is cut short, Blocking = line of
  sight, Impassible = movement.

## Outcome
_(pending)_
