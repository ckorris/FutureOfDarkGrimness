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

**2026-08-05 — LEDGER TRUTH-UP + the repair pass is now PINNED for the friendly-stacking arm.
The "resolver-level repair still open" residual below was STALE: the repair landed 2026-07-23 as
#264 slice 4 (`76c3c48`)** - `TacticianMovementResolver.TryRepairWithinRequestBudgets` re-plans
toward the rejected plan's own destination under the request's budgets (friendly-aware via
`PlanMoveToward`) before conceding to solo, and #264 slice 5 (`39c2c49`) added the degradation
log line. What #264 pinned, though, was only the BUDGET-mismatch arm (the Slow-hero scene). This
session added `TacticianMovementResolverTests`:
- `PlanEndingStackedOnAFriendly_IsRepairedTowardTheSameGoal_NotSoloFallback` - the #205 scenario
  this item was filed about, reached via a fake `IMovePlanSource` handing the resolver a plan that
  parks every model exactly on a friendly base. Scene-checked (the stacked plan provably fails the
  re-check), then asserts: solo NOT called, repaired move passes the same re-check, every endpoint
  clear of every friendly base, and the unit still closes >=5" of the 10" gap to the planned
  destination - intent preserved, not discarded.
- `NoCachedPlan_DegradesToSolo` - documents the one legitimate fallback arm.
Engine suite 2868/2868. **Remaining open (unchanged): the DE/RL mirror-drift attribution** -
transcript diff same seed A5-9 vs current per the #210 workflow, or fold into the Opus clean
re-gate; the close condition below still stands.

**2026-07-11 (later) — charge fix does NOT recover the mirror drift; drift-driver hypothesis
REJECTED.** DE/RL mirrors rerun post-fix (FdgLab/reports/216-recheck/): RL 78.0 with an
IDENTICAL outcome hash to the pre-fix run (the fix changed nothing in those 50 games); DE 82.0
with a changed hash (blocked charge lanes do occur in transport play) but the same 38/6/6
split. The fix stands on its own merits (a planned charge no longer silently degrades to
solo), but the DE 99->82 / RL 89->78 drift has another cause. Leading alternative: #205/#212
changed MOVEMENT LEGALITY FOR BOTH SIDES - the solo baseline can now move through friendlies
(ending-only stacking check), which plausibly strengthens SOLO play most in the two densest
armies; i.e. the "drop" may be baseline improvement, not Tactician degradation. Next step
(unchanged): transcript diff, same seed, A5-9 engine vs current (#210 workflow), or fold into
the Opus clean re-gate.

**2026-07-11 — straight-line charge candidate made friendly-aware.** Passes
`LiveFriendlyFootprints` into its ValidateWithBackoff, so the backoff ladder shortens the
charge rather than emitting a plan the resolver must reject. Pin test:
MacroActionGeneratorTests.ChargeThroughAFriendly_PlansAValidMove (fails without the fix).
Residual (open): the resolver-level fallback is still solo when a plan IS rejected for any
other reason - a repair pass (re-plan toward the same endpoint friendly-aware, then solo as
last resort) would preserve intent; and the mirror-drift attribution (transcript diff per the
#210 workflow, or the Opus clean re-gate) has not been run. Close this item only after the
DE/RL cells are re-explained or recovered.
