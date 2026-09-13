# 089 — AI movement aims at base contact / standoff (not blind backoff)

## Goal

Fix the limitation #011 surfaced: the AI/auto movement resolvers stopped "1" short of the enemy
**centre**, which is actually base overlap, then relied on a blind halving backoff that only ever
retreats — so the AI never completed a charge, just stalled ~1"+ out of melee.

## Decisions

- **Aim at an explicit end gap, don't guess-and-retreat.** Pick the unit's intent — a melee/hybrid
  unit that can reach charges to **base contact**; everyone else (and the CLI auto-advance) advances to
  the **1" standoff**. Compute the centroid step that lands the *nearest model* at the target gap.
- **Solve the packing offset by measuring, not modelling.** `PackGrid` translates the whole formation,
  so the nearest model's gap responds ~1:1 to the centroid step. The AI builds a candidate, measures the
  actual nearest-enemy gap, and corrects the step (`step += achievedGap - targetGap`); 3 passes converge.
  This decouples the aim from PackGrid's internal layout. (The CLI auto-advance uses the simpler
  single-shot standoff target + backoff — it's a degenerate EOF fallback, not worth the control loop.)
- **Keep validate-and-backoff as the safety net** for when terrain or a screening unit blocks the ideal.
- **Add a true no-op fallback rung.** The previous final fallback (reform-in-place via `PackGrid`) was
  returned **without re-validation**; a unit intermingled with enemies (mid-melee) can't re-pack without a
  model crossing an enemy base, so that move was rejected and `DefinePathStage` threw
  (`Moves through an enemy unit`). Added `HoldExactPositions` — each living model keeps its exact position
  (zero-length paths can't move through anything), tried after the in-place re-pack fails. Applied to both
  the AI resolver and the CLI auto-advance.

## Notes

### 2026-06-14
- Branch `089-ai-charge-to-contact`, stacked on `011-move-through-enemy-units` (both repos).
- The intermingled-unit crash was caught by the headless smoke (not the unit tests) — melee only starts
  happening *because* the AI now charges into contact, which then exposed the unvalidated re-pack
  fallback. Fixed before committing the bump; 5/5 smoke runs exit 0 with 3–5 melee resolutions each.
- Engine `dae8da1`, suite 490/0 (+2: melee reaches contact, shooter holds at standoff).

## Outcome

AI melee/hybrid units charge into base contact; shooters/auto-advance hold at the 1" standoff (legal,
not overlapping); robust no-op fallback prevents the intermingled-unit `DefinePathStage` throw. Engine
`dae8da1`; superproject bump + CLI mirror in the follow-up commit. Suite 490/0, headless-verified (5×).
