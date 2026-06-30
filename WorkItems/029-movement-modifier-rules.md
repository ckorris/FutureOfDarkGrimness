# 029 — Movement-modifier rules (Fast/Slow/…, Strider, Aircraft, Flying) + target-perspective charge debuffs

**Status**: in progress — Fast/Slow/VeryFast (in #042 catalog), Immobile (#100), Strider (#102), and now the
**per-target charge-distance debuff mechanic + Melee Shrouding** (2026-06-30) are done. Open: Aircraft, Flying,
and defensive Darkborn (which now has its mechanism).
**Related**: #102 (range-modifier threading — the shooting twin of this; Ranged Shrouding ↔ Melee Shrouding),
#093 (per-model "all models have this rule" gating), #027 (weapon-scoped rules).

## Goal
Implement the movement-modifier special rules. Most of the simple ones (Fast/Slow/VeryFast/Agile/Quick/Rapid*)
already live in the #042 catalog via `Effect.MovementBonus` → `MovementModifierSink` → `MovementActionContext`.
The harder pieces are the **target-perspective** debuffs — "enemies get −N movement when CHARGING this unit"
(Melee Shrouding, defensive Darkborn) — and the bundle rules Aircraft / Flying.

## Notes
- 2026-06-30: **Per-target charge-distance debuff mechanic + Melee Shrouding DONE** (engine-only; branch
  `029-charge-distance-debuff`). Lights up the long-dormant `Movement_OnChargeDeclared` hook + `ChargeDeclaredContext`.
  - **Mechanism:** `MovementRuleQueries.EffectiveChargeDistanceAgainst(charger, target, baseCharge, evaluator)`
    fires `ChargeDeclaredContext` and folds the target's Subject-seat `MovementBonus(Charge)` deltas + floor,
    returning the effective charge distance toward that target. `Effect/RuleOperation.MovementBonus` gained a
    `MinResultInches` floor (default 0; only the charge query reads it — the Actor-seat Fast/Slow sink path
    ignores it). `DefinePathStage` reduces the charge budget to the **worst case** among enemies within base
    charge reach (`WorstCaseChargeDistance`), then uses the reduced `hardCap` for the request + validation — so
    the preview rings, all GUI/CLI/AI resolvers, and the authoritative check all shrink with one scalar change.
  - **Rule:** Melee Shrouding (−3" charge, floor 6", Subject) + Melee Shrouding Aura. The charge twin of Ranged
    Shrouding. `All` → 108.
  - **Architecture decision (worst-case vs per-target):** charge is geometric here (a Move that ends in melee;
    target picked AFTER the move in `ChooseMeleeDefenderStage`), and the charge budget is a single scalar
    computed before any target is known. Chose the **worst-case** model (user sign-off): reduce the whole budget
    by the strongest in-reach Shrouding. Never permits an illegal over-charge; the cost is that when a Shrouding
    AND a normal enemy are both in charge range, charges toward the normal one are also shortened (the mis-bite
    is narrow — a normal enemy at 9–12"). The fully per-target alternative (per-enemy penalties threaded onto the
    request + a charge-aware `ValidatePaths` overload + footprint changes + every move resolver, with a
    UnitKey↔UnitID matching wrinkle) was deemed too much surface/risk for one rule.
  - Tests: `MeleeShroudingRuleIntegrationTests` (query −3 / no-rule / floor 6; catalogued + aura resolvable).
    Engine 917/0, full build, headless exit 0.

## Decisions
- 2026-06-30: Worst-case charge-cap reduction (above), per user sign-off. Conservative; never allows an illegal
  charge. Reuses the single-scalar `hardCap` machinery so no `ValidatePaths`/resolver/footprint changes.
- 2026-06-30: The "to a min. of 6\"" floor rides `MovementBonus.MinResultInches`, read only by the charge query
  (the Actor-seat `MovementModifierSink` path that Fast/Slow use ignores it) — mirrors #102's `RangeModifier`
  floor. The corpus's "where ALL MODELS have this rule" is approximated unit-level (#093), as for Ranged Shrouding.

## Outcome
_(written when closed — Aircraft / Flying / defensive Darkborn remain)_

### Follow-ups still open
- **Defensive Darkborn** — enemies get −range (already expressible: `RangeModifier(−4, floor 6)`, Subject, #102)
  AND −charge (now expressible: `MovementBonus(Charge, −2, floor 6)`, Subject, via this mechanism). Both halves
  are now buildable; the only blocker was the charge mechanic, now done. NOT catalogued because the name
  "Darkborn" is already taken by the offensive variant (#102) — needs a disambiguation decision before adding.
- **Aircraft / Flying** — core-rulebook bundle rules (not in the army corpus). Flying = ignore all terrain
  (extends Strider's `IgnoreTerrainEffects` to Dangerous + move-through-units) + the enemy move-through flag.
  Aircraft = forced-move + targeting/charge restrictions; its "−range to enemies targeting it" rides #102's
  Subject `RangeModifier`, and any "−charge to enemies" rides this item's mechanism.
