# 029 — Movement-modifier rules (Fast/Slow/…, Strider, Aircraft, Flying) + target-perspective charge debuffs

**Status**: DONE (2026-06-30). Fast/Slow/VeryFast (#042), Immobile (#100), Strider (#102), the per-target
charge-debuff mechanic + Melee Shrouding, Darkborn (Off/Def), Flying (full), and **Aircraft (all facets,
including the forced-movement mode)** are implemented, catalogued, and tested. See Outcome.
**Related**: #102 (range-modifier threading — the shooting twin of this; Ranged Shrouding ↔ Melee Shrouding),
#093 (per-model "all models have this rule" gating), #027 (weapon-scoped rules).

## Goal
Implement the movement-modifier special rules. Most of the simple ones (Fast/Slow/VeryFast/Agile/Quick/Rapid*)
already live in the #042 catalog via `Effect.MovementBonus` → `MovementModifierSink` → `MovementActionContext`.
The harder pieces are the **target-perspective** debuffs — "enemies get −N movement when CHARGING this unit"
(Melee Shrouding, defensive Darkborn) — and the bundle rules Aircraft / Flying.

## Notes
- 2026-07-01: **Aircraft UX pass (hand-verification findings + user-requested rework).** Rule text verified
  against the OPR wiki: *"Aircraft may only use Advance actions, moving in a straight line by 30"-36" without
  turning. If it moves off-table, its activation ends, and it must be deployed on any table edge at the
  beginning of the next round"* — direction changes only when re-placed. Changes (engine `86a6b34`/`8111b05`/
  `00a88cb`, + app):
  - **Shoot-after-move fixed** (`86a6b34`): the forced 30–36" Advance is exempt from the advance-and-shoot
    distance gate — Aircraft may shoot after it. Counter-gate: a flown-off unit can't shoot or charge
    (`OffTableFromForcedMove` gates in `ChooseActionStage`), so flying off truly ends the activation.
  - **Heading = deploy facing** (`8111b05`): `GetHeading` (was `EnsureHeading`) reads the shared model facing
    placed at deploy/redeploy; the auto-aim toward table centre + `AircraftHeadingSet` token are GONE (this was
    the visible "turn on activation" the user caught). Divergent per-model facings assert.
  - **Continuous, visual move** (`8111b05` + app): new `AircraftAdvanceRequest` (heading, 30–36" band) →
    `AircraftAdvanceResult` (distance, fliesOffTable) replaces the 30/33/36 string menu. GUI
    `GuiAircraftAdvanceResolver`: mouse projected onto the segment 30–36" ahead, ghost bases + heading
    triangles, click commits, fly-off Yes/No modal when the spot crosses the bounds. CLI prompts a distance
    (EOF → 30, EOF-confirm → fly off); AI picks the shortest on-table distance else confirms the mandatory
    fly-off. Geometry is authoritative over the resolver's flag.
  - **Edge redeploy** (`00a88cb`): `PlaceObjectsRequest.MustTouchTableEdge` + `PlacementUtilities.TouchesZoneEdge`
    (circumscribed radius + 0.5" tolerance; unit-level — ≥1 model touches). The redeploy passes it (task
    "Aircraft Redeploy"); GUI gates group-drop + Done with a live hint; CLI auto-scans the edges and enforces
    on the last model; AI pins a block axis to the nearest-to-lane edge and faces the models inward (facing =
    heading, so it doesn't fly straight back off). Retires the "any edge relaxed to anywhere" simplification.
  - Suite 969/0, both builds clean, headless smokes exit 0 (the aircraft army exercised the whole flow:
    deploy-first → prompt → fly-off confirm → leave-play). GUI hand-verification pending.
- 2026-06-30: **Aircraft forced-movement mode DONE — #029 COMPLETE** (branch `029-aircraft-forced-movement`).
  Corrected my earlier overstatement: a heading is a SEPARATE additive field, not a refactor of everything that
  reads Position (user caught this). The real work was the movement machinery, not the storage.
  - **Heading:** `UnitData.AircraftHeading` (`Float2?`, JsonProperty, additive). Set lazily on first move toward
    the table centre (`ForcedAircraftMove.EnsureHeading`) and never recomputed while on the table ("doesn't
    turn"); cleared when it flies off so it re-aims when re-placed.
  - **Advance-only:** Aircraft rule gains `RestrictActions([Advance])` at `Activation_OnActionChoice` (the
    Immobile mechanism). Shooting after the Advance is unaffected (Shoot is a sub-step, not an EActionType).
  - **Forced move:** `DefinePathStage.Enter` branches on `IsAircraft` BEFORE the free-form path request — it asks
    the distance via a `StringSelectionRequest` (30/33/36"; reuses the existing string resolvers, AI/EOF → 30"),
    builds a rigid straight-line translation along the heading (`ForcedAircraftMove.BuildPaths`), and either
    submits it (on-table) or — if it would cross a table edge (`WouldLeaveTable`) — sets the models to origin,
    adds `TokenType.OffTableFromForcedMove`, and clears the heading. No path-UI; no ValidatePaths (Aircraft
    ignore terrain + units).
  - **Off-table → reserve → redeploy:** holding the models at origin makes the unit off-battlefield (reserve-like:
    no shooting targets, no objective contest), so the activation naturally peters out. `StartOfRoundExtraActionStage`
    (round 2+) redeploys any `OffTableFromForcedMove`-tokened unit via the existing reserve placement flow, clears
    the token, and marks it `ArrivedFromReserve` (can't seize the round it returns).
  - Tests: `ForcedAircraftMoveTests` (heading toward centre + idempotent/no-turn; rigid paths; off-table bounds;
    + DefinePathStage integration for the off-table leave-play and the on-table straight move). Engine 938/0,
    full build, headless exit 0. No app changes (the distance prompt + redeploy reuse existing request types).
  - Simplifications (recorded): the heading auto-aims toward centre (no player heading-pick UI), the redeploy zone
    is the whole table ("any edge" relaxed to "anywhere"), and the off-table-ends-activation is achieved by the
    unit becoming a reserve rather than an explicit activation-abort. Each is a faithful approximation; a fuller
    version (player-chosen heading, edge-only redeploy strip) is a refinement, not a gap.
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
Closed 2026-06-30. All of #029's movement-modifier rules are implemented, catalogued, and tested:
- **Self move-distance** (Actor `MovementBonus`): Fast/Slow/VeryFast/Agile/Quick/Rapid* (#042), Immobile (#100,
  RestrictActions), and offensive Darkborn's +3 charge (#102).
- **Terrain ignore**: Strider (difficult cap, #102) and **Flying** (all terrain via `ETerrainIgnoreScope` +
  the threaded impassible flag + Dangerous-roll skip, and move-through-units).
- **Target-perspective debuffs**: the per-target charge-distance mechanism (`Movement_OnChargeDeclared` +
  `EffectiveChargeDistanceAgainst`, worst-case in DefinePathStage) powering **Melee Shrouding** and **Darkborn
  (Defensive)**; range debuffs ride #102's Subject `RangeModifier`.
- **Aircraft** (all facets): −12 range / −1 hit to attackers + terrain/unit ignore; can't seize objectives;
  can't be charged/contacted; deploys first; and the **forced straight-line movement mode** (Advance-only +
  heading field + off-table → reserve → edge redeploy).

Net new machinery this item introduced: `ETerrainIgnoreScope` + `IgnoresAllTerrain` + the impassible flag;
`Movement_OnChargeDeclared` lit up + `MovementBonus.MinResultInches` floor + `EffectiveChargeDistanceAgainst`;
`EnemyModelFootprint.Uncontactable`; `AircraftRules.IsAircraft`; `UnitData.AircraftHeading` + `ForcedAircraftMove`
+ `TokenType.OffTableFromForcedMove`. `CoreRuleCatalog.All` grew to 111 rules.

### Faithful simplifications (recorded, not silent gaps)
- Aircraft heading auto-aims toward the table centre (no player heading-pick UI); redeploy uses the whole-table
  zone ("any edge" relaxed); off-table-ends-activation is realised by the unit becoming a reserve (it has nothing
  to do off-table) rather than an explicit activation-abort. Per-model gating ("where ALL MODELS have this rule")
  is approximated unit-level for the Shrouding/Darkborn family (#093). Each is a faithful approximation; a fuller
  version (player-chosen heading, edge-only redeploy strip, per-model gating) is a refinement, not a missing facet.
