# 312 — "Charge offered but won't allow" + partial one-at-a-time moves (2026-07-31 networked game)

## Goal

Two reports from the Chris-vs-Odo networked game (save: `ChargeOfferedButWontAllow.fdgsave`, Chris's
Desktop): (1) Odo (client, all-rect-base titan army) repeatedly saw the UI say he could charge while
"it wouldn't let him"; (2) Chris moved 10 models one at a time, confirmed, and believes only 6 moved.
Fix the confirmed defects behind both.

## Established facts (2026-08-02 investigation)

- Save state: Odo's Vassal Micro-Titan (unit idx 10) had activated and ended ~2.2-2.9" edge-to-edge
  from Chris's Spotter Squad — just outside the 2" melee band, activation spent. All of Odo's units
  are on rect bases (2.76x4.13 up to 4.80x6.30); all of Chris's are circles. Neither army carries a
  charge-only movement bonus (no Rapid Charge / Darkborn).
- `BaseShapeGeometry.SurfaceGap2D` itself verified correct: scratch harness vs brute-force ground
  truth, 4000 randomized rotated rect/circle pairs + analytic cases, zero mismatches.
- **Bug 1 (engine, dormant in this game):** `MovementUtilities.ValidateChargeReach` measures
  CENTER-to-center (`Position.GetDistance2D(end, e) <= 2"`), discarding both base shapes — the only
  distance gate in the melee/charge family not base-to-base. Effective requirement is a base gap of
  `2" - r_mine - r_enemy`; impossible for big rect bases even in literal base contact. Only fires
  when a model exceeds its Rush cap (needs a charge-only bonus since Rush == Charge == 12 default).
  `ChargeReachValidationTests` bakes the wrong semantics in ("1.5 inches from enemy" = base contact
  at r=0.75).
- **Bug 2 (GUI):** three ways a destination click is silently eaten in `GuiDefineMovementResolver`
  (`:678-707`): (a) click on an enemy base is consumed as a tactical-overlay pin; (b) click on any
  unit-mate's START footprint re-selects it (`ModelPicker.HitTest` tests `model.Position`, never the
  planned endpoint — while `WouldOverlapAnyModel` uses planned endpoints, so the ghost draws green
  exactly where the click gets swallowed); (c) budget-exhausted click is a silent no-op. Enter/Space
  is Done and an empty/partial path is always valid; the "N can charge" charge line draws from the
  GHOST, not the committed path. Together these reproduce both reports without Bug 1 firing:
  a one-at-a-time forward shuffle clicks rear models onto vacated start slots (10 -> 6), and a
  charge approach clicks the enemy base itself (pin eats it) then confirms a short/empty move.
- Client-only aggravators noted, not in scope here: resolver holds raw `ModelData` refs across
  replication churn (#309 class; `[RESOLVER ERROR]` latch in `RaylibRenderer`), GUI Done gate
  stricter than host (strict vs lenient coherency, no epsilon, off-battlefield phantom enemies at
  origin — the #207 filter is missing in `GetEnemyFootprintsForRequest`).

## Agreed fixes (Chris, 2026-08-02)

1. `ValidateChargeReach` -> base-to-base with shapes + facings (all pairs — nearest-by-center is not
   nearest-by-base for rects/corners); audit the rest of the movement validators for center-based or
   facing-less measures.
2. Remove the enemy-pin click behavior from the movement resolver ("I don't think that even works
   anymore").
3. Click hit-test (movement + consolidation) follows planned endpoints, with the hover highlight
   drawn at the same spot.

## Notes

- 2026-08-02 — **was #310 pre-reconciliation-36**: origin/master had already claimed 310 (per-user
  config, via reconciliation 34) and 311 (pass confirmation, via reconciliation 35) while this item
  was being built; per merged-wins precedent it yields to 312. The two commit messages naming "#310"
  (engine `03bf1a4`, superproject `fec819a`) predate the renumber.
- 2026-08-02 — all three fixes implemented + tested:
  - **Engine `03bf1a4`:** `ValidateChargeReach` measures shape+facing base-to-base over all model pairs
    at the mover's END position and END facing (was centre-to-centre). Same-audit fixes: the
    move-through-enemy END gap and per-segment sweep, `ValidateEndsOnFriendly`'s end check, and both
    coherency checks now measure the base at the facing the executor actually leaves it at (new
    `EndFacing` helper; `ValidateEndsOnTable` already did this). `ChargeReachValidationTests` semantics
    corrected (the old "1.5 inches from enemy" was base contact at r=0.75) + 2 new regression tests:
    the Micro-Titan base-contact case and a reach-only-at-end-facing case. Engine suite 2555/0.
  - **App (this commit):** (2) enemy-pin click removed — `HandleEnemyPinClick` /
    `_frameEnemyPinConsumed` deleted from `GuiDefineMovementResolver`, `TryHandleEnemyClick` deleted
    from `TacticalOverlayController`. Nothing sets a pin any more; the remaining `_pins` plumbing
    (band snap branch, pin panels, Esc unpin, `ActiveTargetUnit` fallback, `TacticalOverlayConfig.
    ClearPinsKey`) is inert dead code — **removal deferred, tracked here, not silently cut**. Hover
    (#247) remains the way to inspect an enemy mid-move. (3) `ModelPicker.HitTest` takes per-model
    POSES (planned ghost position + facing) — movement and consolidation resolvers share a
    `PlannedPose` helper between the final-ghost draw and the hit test, so a planned model is
    clicked where its ghost stands and its vacated start slot is placeable ground; the hit test is
    also facing-aware for rotated rect bases (was axis-aligned). `ModelPickerTests` migrated + 2 new
    (rotated-rect hit, planned-endpoint vs vacated-slot). App suite 862/0, headless smoke exit 0.
  - Not addressed here (recorded, not cut): the silent budget-exhausted click no-op; ghost-based
    "N can charge" line vs committed path divergence; the client-only aggravators listed above
    (stale `IModel` refs / `[RESOLVER ERROR]` latch, GUI-stricter-than-host Done gate).
- 2026-08-02 — filed after the investigation session.

## Outcome

_(implemented + tested; awaiting GUI hand-verify and a networked re-test with Odo)_
