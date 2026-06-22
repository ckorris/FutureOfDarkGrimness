# 097 — Transport disembark/embark full movement (real path + Rush/Charge)

**Status**: todo
**Related**: #035 (Transport — slices C/D), #011 (move-through-enemy validation), #012 (Advance/Rush/Charge bands)

## Goal
Replace the Advance-equivalent simplifications #035 slices C and D shipped with the faithful movement the rule implies ("units may enter/exit by using any move action"):

- **Disembark (slice C today):** places the unit within 6" of the transport and counts it as an Advance (may then Shoot, can't move further). Should let the unit take the *full* move from the 6" drop point — Rush, or Charge into melee out of the transport.
- **Embark (slice D today):** the unit is "set aside" if a friendly transport is within Advance distance — no real path is drawn, no Rush/Charge in. Should move the unit along a real path into base contact with the transport (and allow Rush/Charge to reach it).

"Done" = a unit can disembark and then charge, and can move (including Rush/Charge) into a transport to embark, with real paths validated against terrain / enemies like any other move.

## Notes
- 2026-06-21: Opened from the #035 slice C/D deferrals. Both slices deliberately reused the simplest "place within 6" / set-aside" primitive (the same shape as #035's other Advance-equivalent calls) and recorded the real-movement work here. Threads an embark/disembark target through the movement flow (`DefinePathStage` / `ExecuteMoveStage`).

## Decisions

## Outcome
