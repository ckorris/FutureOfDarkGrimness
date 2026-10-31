# 019 — Consolidation moves after melee resolution

**Status**: done
**Related**: commit fdg-raylib 77bf284, submodule e47df8c (branch `019-ConsolidationMoves` in both repos)

## Goal
After melee resolves, the attacker performs a consolidation move:
- **Wipeout** (defender wiped, attacker survives): up to 3" in any direction.
- **Disengage** (both survive): up to 1" back. (GDF wording is "away", interpreted here as player choice with a 1" cap.)
- **Attacker wiped**: skip entirely.
Movement must respect impassible terrain, the cap, and unit cohesion. Enemy-unit obstruction is deferred to work item 011.

## Notes
- 2026-05-24: Closed out. AI table-bounds clamp added per playtest; GUI refactored to per-model path-builder (same UX as DefineMovement).
- 2026-05-24: Playtest revealed AI was missing a `ConsolidationMoveRequest` resolver on the network client — added `AiConsolidationMoveResolver` and registered it in `AiResolverRegistryFactory`.
- 2026-05-24: Playtest revealed the GUI ghost could be committed off-board — added a table-bounds clamp to the ghost (and an equivalent scalar clamp on the AI delta that preserves cohesion).
- Initial implementation moved the whole unit as a single delta; refactored to per-model paths via `PathTemplate` so it matches the DefineMovement flow (L-click select / R-click waypoint / Backspace undo / Space cycle).

## Decisions
- **Reused `PathTemplate`** instead of carrying a one-shot delta. `PathTemplate.MaxAdvanceDistance` is half of charge, which is irrelevant here — we pass the consolidation cap as the "charge" cap and ignore the advance split. This lets the GUI piggy-back on the existing per-model path data structure rather than inventing a parallel one.
- **No new shared canvas-transform utility.** The three resolvers (DefineMovement, PlaceObjects, Consolidation) duplicate ~6 lines of pixel/inch math each. Extracting now would be premature; revisit if a fourth resolver appears.
- **Reused `ModelMoveEntry` as the request return type** so the engine's existing "apply each entry's positions" loop covers consolidation with no special-case code.
- **AI Disengage strategy: step directly away from the nearest enemy model**, not the unit-as-a-whole. Simpler and the 1" cap means edge cases barely matter.
- **AI clamp preserves cohesion** by computing a single scalar `t ∈ [0, 1]` such that every model's `position + t*delta` stays on-board, then applying `t * delta` to all models. This is the largest in-bounds move that keeps the unit together.
- **Validation runs the standard `MovementUtilities.ValidatePaths`** even though cohesion is preserved trivially by single-delta moves — defensive, since a future resolver might submit truly per-model paths.

## Outcome
Shipped: `ConsolidateStage` between `ApplyFatigueStage` and the melee-finished event; `ConsolidationMoveRequest` with `EConsolidationReason.{Wipeout, Disengage}`; CLI, GUI, and AI resolvers; 6 NUnit tests in `ConsolidateStageTests`. GUI ghost and AI delta both clamped to the table. Enemy-unit pass-through validation remains deferred to item 011.
