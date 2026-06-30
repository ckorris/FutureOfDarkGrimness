# 029 — Movement-modifier rules (Fast/Slow/…, Strider, Aircraft, Flying) + target-perspective charge debuffs

**Status**: in progress — Fast/Slow/VeryFast (#042 catalog), Immobile (#100), Strider (#102), the per-target
charge-debuff mechanic + **Melee Shrouding**, **Darkborn (Defensive)**, **Flying** (full — ignore all terrain +
move through units), and **Aircraft (partial)** all done (2026-06-30). Remaining: Aircraft's DEFERRED facets
(forced straight-line movement mode, can't-be-charged, can't-seize-objectives, deploy-first) — each needs new
machinery.
**Related**: #102 (range-modifier threading — the shooting twin of this; Ranged Shrouding ↔ Melee Shrouding),
#093 (per-model "all models have this rule" gating), #027 (weapon-scoped rules).

## Goal
Implement the movement-modifier special rules. Most of the simple ones (Fast/Slow/VeryFast/Agile/Quick/Rapid*)
already live in the #042 catalog via `Effect.MovementBonus` → `MovementModifierSink` → `MovementActionContext`.
The harder pieces are the **target-perspective** debuffs — "enemies get −N movement when CHARGING this unit"
(Melee Shrouding, defensive Darkborn) — and the bundle rules Aircraft / Flying.

## Notes
- 2026-06-30: **Darkborn (Off/Def) + Flying + Aircraft (partial) DONE** (branch `029-flying-aircraft-darkborn`).
  - **Darkborn split:** the corpus uses "Darkborn" for two different rules. Renamed the existing own-buff rule
    to **Darkborn (Offensive)** and added **Darkborn (Defensive)** (enemies −4 range floor 6 Subject, −2 charge
    floor 6 Subject) — both halves expressible now via the #102 range + #029 charge mechanisms.
  - **Flying (FULL):** parameterized `IgnoreTerrainEffects` with `ETerrainIgnoreScope` (DifficultOnly = Strider,
    AllTerrain = Flying). New `MovementRuleQueries.IgnoresAllTerrain`. Threaded a new `ignoresImpassibleTerrain`
    flag through `ValidatePaths` + `ValidateMovingThroughImpassibleTerrain` + both move requests + all GUI/CLI/AI
    move resolvers (mirroring Strider's difficult flag). Wired the Dangerous-terrain skip into
    `MovementExecutor.ApplyDangerousTerrainEffects` + `ApplyNonMovementTerrainEffectsStage`. Flying emits
    `IgnoreTerrainEffects(AllTerrain)` + `IgnoreEnemyMovementBlock` (reuses Strafing's move-through op). So a
    Flying unit ignores the difficult cap, Dangerous wounds, and Impassible blocking, and moves through units.
  - **Aircraft (PARTIAL, per the user-provided definition):** ENFORCED — enemies targeting it get −12" range
    (Subject `RangeModifier`, so it's effectively immune to ≤12" weapons) and −1 to hit (Subject
    `RollModifier(Hit)` at `Shooting_OnHitRollModifier`, where `DetermineHitRollStage` already evaluates the
    defender as Subject — exactly Stealth's mechanism minus the distance gate); plus Flying's ignore-all-terrain
    + move-through-units. DEFERRED (need new machinery, loudly documented on the rule + here): the forced
    straight-line 30–36" Advance-only move with no turning + off-table redeployment each round; can't be charged
    ("can't be moved in contact with"); can't seize objectives; must deploy before all other units. A unit with
    Aircraft therefore still moves/deploys/charges/seizes normally until those systems exist.
  - `All` 106 → 111 (Flying, Aircraft, Darkborn split +1 net, Darkborn(Def)). Tests: `MovementFlyingValidationTests`,
    `AircraftRuleIntegrationTests`, defensive-Darkborn case in `RangeModifierRuleIntegrationTests`; updated the
    Strider/FlyOver tests for the new param and the phantom-rules picker test (Flying/Aircraft now offered).
    Engine 926/0, full build, headless exit 0.
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
- 2026-06-30: **Full Flying over the impassible-only subset** (user sign-off): threaded `ignoresImpassibleTerrain`
  through the whole ValidatePaths/resolver surface (parallel to Strider's difficult flag) so Flying flies over
  walls too, not just difficult/dangerous terrain.
- 2026-06-30: **Darkborn disambiguated as "Darkborn (Offensive)" / "Darkborn (Defensive)"** (user sign-off) — the
  bare name is ambiguous across armies, so each variant is catalogued under a parenthesized name; no bare
  "Darkborn" alias (it can't be resolved without army context).
- 2026-06-30: **Aircraft shipped partial, loudly documented** (user supplied the definition and chose to build it).
  The dispatching facets (defensive shooting + terrain/unit ignore) are real; the movement-mode / charge-immunity /
  objective / deploy-order facets are deferred with a prominent note on the rule and below — building a
  half-rule labelled "Aircraft" is a known limitation, accepted to make the buildable facets available now.

## Outcome
_(written when closed — Aircraft's deferred facets remain; see below)_

### Follow-ups still open — Aircraft's deferred facets (each its own mechanic)
- **Forced movement mode** — Aircraft may only Advance, moving in a straight line 30–36" with no turning; if it
  leaves the table its activation ends and it redeploys on any table edge next round. A whole custom movement
  mode (current movement is free-form path within a budget).
- **Can't be charged** ("can't be moved in contact with") — a Subject-seat "no enemy may end a charge in contact
  with me" gate in the charge/melee-eligibility path.
- **Can't seize objectives** — exclude the unit in `ReconcileObjectivesStage`.
- **Must deploy before all other units** — a deployment-priority/ordering hook.
These would also complete other core movement rules (e.g. anything sharing the forced-move or charge-immunity
shape). The defensive-shooting + terrain facets are done and shared with Flying / Darkborn (Defensive).
