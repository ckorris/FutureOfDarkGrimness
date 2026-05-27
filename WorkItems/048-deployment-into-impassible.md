# 048 — Block deployment of models into impassible terrain

**Status**: open
**Related**: #002 (terrain placement)

## Goal
When a unit is being deployed (auto-placement by AI, or manual placement via the GUI/CLI resolvers), models must not be placed inside impassible terrain. Currently `DeployAllUnitsStage` / the AI auto-placement has no intersection check against `Impassible`-flagged terrain pieces, so a model can be placed inside or overlapping a building. Observed in the wild: a terrain piece placed flush against the deployment zone boundary caused the AI to place a model directly on top of it.

Done when:
- Auto-placement (AI and CLI EOF fallback) rejects candidate positions that intersect any `Impassible` terrain piece.
- The GUI placement resolver similarly blocks the player from confirming a position that overlaps impassible terrain (or at minimum warns visually).
- The engine test suite has a case covering auto-placement with an impassible piece in the deployment zone.

## Notes

## Decisions

## Outcome
