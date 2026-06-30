# 029 — Movement-modifier rules (Fast/Slow/…, Strider, Aircraft, Flying) + target-perspective charge debuffs

**Status**: in progress — Fast/Slow/VeryFast (#042 catalog), Immobile (#100), Strider (#102), the per-target
charge-debuff mechanic + **Melee Shrouding**, **Darkborn (Defensive)**, **Flying** (full), and **Aircraft** all
done. Aircraft's can't-seize-objectives, can't-be-charged, and deploy-first facets landed 2026-06-30. **Remaining:
only Aircraft's FORCED-MOVEMENT mode** (Advance-only straight-line 30–36" no-turning + off-table redeployment) —
a large new sub-system (no facing model; constrained-move resolvers; off-table + redeploy); pending a design call.
**Related**: #102 (range-modifier threading — the shooting twin of this; Ranged Shrouding ↔ Melee Shrouding),
#093 (per-model "all models have this rule" gating), #027 (weapon-scoped rules).

## Goal
Implement the movement-modifier special rules. Most of the simple ones (Fast/Slow/VeryFast/Agile/Quick/Rapid*)
already live in the #042 catalog via `Effect.MovementBonus` → `MovementModifierSink` → `MovementActionContext`.
The harder pieces are the **target-perspective** debuffs — "enemies get −N movement when CHARGING this unit"
(Melee Shrouding, defensive Darkborn) — and the bundle rules Aircraft / Flying.

## Notes
- 2026-06-30: **Aircraft facets — can't-seize-objectives, can't-be-charged, deploy-first DONE** (branch
  `029-aircraft-deferred-facets`). New `AircraftRules.IsAircraft(unit)` helper (a plain RuleDefinitions name
  check, the `TransportUtilities.IsTransport` pattern, since these gates run in stage code with no RuleEvaluator).
  - **Can't seize objectives:** `ReconcileObjectivesStage.PlayersNearObjective` skips Aircraft units' models, so
    they count toward neither seizing nor contesting.
  - **Can't be charged / moved into contact:** `EnemyModelFootprint` gained an `Uncontactable` tag (optional, so
    the ~15 test sites are untouched), set in `GetEnemyModelFootprints` AND all six move-resolver footprint
    builders (so preview = authoritative). In `ValidateMovingThroughEnemyUnits`, a move may not close to within
    the standoff distance of an uncontactable enemy (covers the base-contact zone AND the standoff band) — but
    units may still pass UNDER it (the through-check is skipped). Also excluded from `ValidateChargeReach`,
    `GetCanCharge`, and `ChooseMeleeDefenderStage` (never a valid melee defender). The over-restriction is tiny:
    you simply can't END within 1" of an Aircraft.
  - **Deploy first:** `ChooseUnitToDeployStage` offers only Aircraft while the player has any undeployed Aircraft;
    the rest are grayed ("Aircraft deploy first") — enforced per player (deployment alternates players, so it's
    "each player's Aircraft before their own others"). The stage already had a placeholder comment for this.
  - Tests added to `ObjectiveOwnershipTests`, `MoveThroughEnemyValidationTests`, `AmbushDeploymentChoiceTests`.
    Engine 932/0, full build, headless exit 0.
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
_(written when closed — only Aircraft's forced-movement mode remains; see below)_

### Follow-up still open — Aircraft's forced-movement mode (LARGE; pending a design call)
Everything else on #029 is done. The sole remainder is Aircraft's movement behaviour: **may only Advance, moving
in a straight line 30–36" with no turning; direction can't change while on the table; if it leaves the table its
activation ends and it redeploys on any table edge next round.** This is a large new sub-system, NOT a clean
hook:
- **No facing/direction model exists** — `ModelData`/`IModel` carry only a `Position` (no heading). True
  "can't turn while on the table" needs either a facing field (a ~50-file refactor across everything that reads
  Position) or a per-unit "current heading" stored on a token + validated at activation.
- **The move resolvers are free-form** — GUI/CLI/AI all pick a destination within a distance budget. A forced
  straight-line fixed-distance move needs new request fields (forced direction + distance) and constrained
  resolver logic (or a fully auto-resolved move with no player path choice).
- **Off-table + redeploy** — bounds are known (`GameWideConstants.DEFAULT_TABLE_WIDTH/HEIGHT_INCHES`) but there's
  no automated off-table detection; redeploy could reuse the Ambush/`PlaceDeferredUnitsStage` pattern (place from
  a table edge at round start) + a token marking the unit as off-table.
- **Advance-only** is the one easy part (`Effect.RestrictActions([Advance])`, as Immobile uses) — but shipping it
  ALONE is a regression (an Aircraft would crawl ~6" instead of flying 30"+), so it can't land without the rest.
Reasonable as its own work item, or a faithful-simplified version (auto straight-line move + heading token +
edge-redeploy) on sign-off. Until then, the catalogued Aircraft moves like a normal unit (loudly documented).
