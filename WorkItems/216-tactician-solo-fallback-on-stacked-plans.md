# 216 — Tactician: planned moves rejected by #205 friendly-stacking fall back to SOLO

**Goal:** a Tactician plan that would end stacked on a friendly must be repaired or planned
friendly-aware — never silently replaced by the solo resolver's move.

**Context (2026-07-11, Chris: "that's really bad"):** #205 made the engine forbid ending a move
stacked on a friendly, and `TacticianMovementResolver` re-validates the planned macro-move with
friendly footprints before submitting - a rejected plan falls back to the SOLO movement
resolver, discarding the Tactician's intent for that activation. Suspected driver of the
A5-9 -> post-#205 mirror drift in the two densest armies (DE Raiders mirror 99 -> 82/80, RL
89 -> 78/76; measured on both fixed and fix-neutralized engines, so pre-existing drift, not
from the garrison-release/dilution work).

**Call-site audit (2026-07-11):**
- `MovementPlanner.PlanMoveToward` -> ValidateWithBackoff WITH friendlies - already safe.
- `MacroActionGenerator` straight-line charge candidate (~line 326) -> ValidateWithBackoff
  WITHOUT friendlies - builds friendly-blind charge moves. FIXED same day (see notes).
- `AiDefineMovementResolver` (solo) - frozen baseline, has its own handling; not touched.

## Notes (newest first)

**2026-07-11 — straight-line charge candidate made friendly-aware.** Passes
`LiveFriendlyFootprints` into its ValidateWithBackoff, so the backoff ladder shortens the
charge rather than emitting a plan the resolver must reject. Pin test:
MacroActionGeneratorTests.ChargeThroughAFriendly_PlansAValidMove (fails without the fix).
Residual (open): the resolver-level fallback is still solo when a plan IS rejected for any
other reason - a repair pass (re-plan toward the same endpoint friendly-aware, then solo as
last resort) would preserve intent; and the mirror-drift attribution (transcript diff per the
#210 workflow, or the Opus clean re-gate) has not been run. Close this item only after the
DE/RL cells are re-explained or recovered.
