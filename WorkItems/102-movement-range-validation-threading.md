# 102 — Movement & range modifier validation threading (Strider, RangeModifier family)

**Status**: DONE (2026-06-29) for its whole original scope — Strider; Increased Shooting Range; Ranged Shrouding (with the −6 "min 6\"" floor); offensive Darkborn (+3 range / +3 charge); and the movement-overlay shooting-range preview. The one residual is **defensive Darkborn** (the corpus's other same-named rule: enemies get −range/−charge vs this unit) — its charge debuff needs a per-target charge-distance mechanic the engine doesn't have, so it's split out as a follow-up rather than counted against this item. See Outcome.
**Related**: #100 (deferred from here — the invasive Part-1 #4 items), #029 (movement-modifier rules umbrella — Strider/Aircraft/Flying live there), #027 (weapon-scoped rules)

## Goal
Wire the two special-rule effects that change how far a unit may move/shoot through **validation**, both of which the engine declares but no stage consumes today, and both of which need a per-unit flag threaded through the movement/range validation path across **both repos**:

- **Strider** (`Effect.IgnoreTerrainEffects`) — the unit ignores the **Difficult-terrain movement cap**. The engine already *enforces* that cap (`MovementUtilities.ValidateMovingThroughDifficultTerrain` adds `ExceededDifficultTerrainMoveLimit` when a move crossing Difficult terrain exceeds `GameWideConstants.DIFFICULT_TERRAIN_MOVE_CAP_INCHES`); Strider should waive it. Also unblocks **Aircraft/Flying** (#029), which ignore all terrain.
- **RangeModifier family** (`Effect.RangeModifier`) — **Increased Shooting Range** (+6"), **Ranged Shrouding** (enemies get −X" range vs this unit), **Darkborn** (+range). The op (`RuleOperation.ApplyRangeModifier`) has no consumer; the shooting target-eligibility / range checks read the weapon's base range only.

Deferred out of #100 because these are **not "finish-a-seam" slices** — they thread a flag through core movement/range validation across ~14 files in both repos, with an open architectural question (below). Doing them well needs a design pass, not an inline continuation.

## The shape (and why it's invasive)
There's a proven precedent for the pattern: **`MovementRuleQueries.CanMoveThroughEnemies`** (Strafing's fly-over). It does one non-logging `EvaluateAllNamed(MoveThroughEnemyContext, …)` read for a rule-derived flag, and that flag:
- rides **`DefineMovementPathRequest.CanMoveThroughEnemies`** — computed once in `DefinePathStage` and read by the resolvers (so engine + GUI/CLI/AI agree),
- is a param on the **`MovementUtilities.ValidatePaths`** overloads, threaded into the per-validator (`ValidateMovingThroughEnemyUnits`).

Strider would parallel this exactly: add `MovementRuleQueries.IgnoresDifficultTerrain(unit, evaluator)`, an `IgnoresDifficultTerrain` flag on the move (and consolidation) request, an `ignoresDifficultTerrain` param on `ValidatePaths` + `ValidateMovingThroughDifficultTerrain` (skip the cap when set), and have every caller compute + pass it. RangeModifier is the same idea on the **shooting** side (a per-unit range delta read once and threaded into `ChooseRangedAttackStage`'s target-eligibility / `IsTargetWithinRange` and the GUI targeting overlay).

**Call sites that thread `canMoveThroughEnemies` today** (the surface a parallel flag touches):
- Engine: `DefinePathStage`, `PathTemplate`, `ConsolidateStage`, `MovementExecutor`, `GameOperationServices`, `AiDefineMovementResolver`, `AiConsolidationMoveResolver`.
- App (FdgRaylib): `GuiDefineMovementResolver`, `GuiConsolidationMoveResolver`, `DefineMovementPathResolver` (CLI), `ConsolidationMoveResolver` (CLI).
- Plus the movement-validation test files.

## Open question to settle before building
**Where is Difficult terrain authoritatively validated?** `PathTemplate.Validate` calls the **no-terrain** `ValidatePaths` overload (`terrain: null`), so it does NOT enforce the Difficult cap — that enforcement appears to live in the **resolvers'** own `ValidatePaths(…, terrain, …)` calls (the gray-out/preview), with the engine trusting the submitted path. If so, Strider only needs to be honoured in the resolver previews + wherever the engine re-validates; if the engine has an authoritative terrain check elsewhere, that's the must-wire point. Confirm this before wiring, or Strider risks being cosmetic (preview-only) or inconsistent.

## Notes
- 2026-06-29: **Move-overlay shooting-range preview DONE** (engine + app) — the last of the three deferred
  facets. `DefineMovementPathRequest` now carries `WeaponRangeOverrides` (a serializable list of
  `(weaponName, enemy UnitID, effectiveRange)`), computed in `DefinePathStage.BuildRangeOverrides` via
  `RangeRuleQueries.EffectiveRange` for each of the mover's distinct ranged weapons × each on-table enemy
  unit — emitting only pairs that differ from the weapon's base range (empty when no range rule is in play).
  `GuiDefineMovementResolver`'s "Show targeting" overlay builds a `(weaponName, UnitID) → range` lookup and
  both of its range gates now use `EffectiveWeaponRange(w, enemyUnit)` instead of raw `w.RangeInches`, so the
  post-move shooting preview reflects Increased Shooting Range (own +6 widens reach) and Ranged Shrouding (a
  shrouded enemy is harder to reach) — in step with the authoritative ChooseRangedAttackStage. Engine-side
  evaluation only (the resolver holds no RuleEvaluator); precompute-on-request mirrors the WeaponSightProfile
  pattern. `GameOperationServices`' triggered-move request leaves the list empty (its preview keeps base
  range — a rare path). Round-trip test guards the new field over JSON/network. Engine 913/0, full build,
  headless exit 0.
- 2026-06-29: **Range floor + offensive Darkborn DONE** (engine-only). Closes two of the three deferred facets.
  - **Floor:** `Effect.RangeModifier` / `RuleOperation.ApplyRangeModifier` gained `MinResultInches` (0 = none).
    `RangeRuleQueries.EffectiveRangeDelta` became `EffectiveRange` — returns the final range
    `max(maxFloorAmongOps, weapon.RangeInches + Σdelta)`. Ranged Shrouding now carries the "−6 to a min. of 6\""
    floor (adopts the floored corpus reading; differs from the unfloored printing only when post-reduction range
    would dip below 6", i.e. base < 12"). ChooseRangedAttackStage now calls `EffectiveRange` directly.
  - **Darkborn (offensive):** catalogued as +3" range (`RangeModifier(+3)`, Actor, at `Shooting_OnRangeCheck`) +
    +3" charge move (`MovementBonus(EActionType.Charge, +3)`, Actor, gated `Condition.ActionTypeIs(Charge)`).
    **Key reuse:** the charge half needed NO new primitive — the move-distance seam Fast/Agile/RapidCharge use
    (`MovementBonus` → `MovementModifierSink` → `MovementActionContext.Max*Distance`) already folds Actor-seat
    Charge bonuses live. All → 106. Tests: floor (9" weapon → 6, 24" → 18), Darkborn range (+3) and charge (+3
    via a real `MovementActionContext`, Advance/Rush untouched). Engine 912/0, full build, headless exit 0.
  - **Defensive Darkborn still deferred** (the corpus's other same-named rule: enemies get −4 range floor 6 AND
    −2 charge floor 6, both Subject). The range half is now expressible (RangeModifier −4 floor 6, Subject), but
    the **charge** half can't be wired: it reduces the CHARGER's distance based on the charge TARGET's rules,
    and the live charge computation (`MovementActionContext`) only folds the mover's own (Actor) rules — there's
    no per-target charge-distance path (the `Movement_OnChargeDeclared` Subject seam exists but isn't fired
    live). That's a distinct mechanic (target-specific charge budget); recommend its own item rather than
    forcing it here.
- 2026-06-29: **RangeModifier slice DONE** (engine-only; branch `102-movement-range-validation`). Consumes the
  long-declared `Effect.RangeModifier` → `RuleOperation.ApplyRangeModifier`. New `Shooting_OnRangeCheck` hook
  (EHookID 81) + `RangeModifierContext` + `RangeRuleQueries.EffectiveRangeDelta(attacker, weapon, defender,
  evaluator)` — a non-logging two-participant read folding the attacker's own range buffs (Actor seat) and the
  defender's range debuffs (Subject seat), mirroring `SightRuleQueries`. Threaded into the AUTHORITATIVE gate:
  `ChooseRangedAttackStage.BuildAttacksForEnemyUnit` computes the per-weapon effective range (`max(0,
  weapon.RangeInches + delta)`, cached per weapon name since it's model-independent) and passes it through
  `CanWeaponShootAtUnit` → `IsTargetWithinRange`. That single point covers all three ChooseRangedAttack
  resolvers (GUI/CLI/AI) — they read `modelsThatCanShoot` rather than recomputing range — and the
  `HasAnyFireableTarget` gray-out. Catalogued **Increased Shooting Range** (+6, Actor) + aura and **Ranged
  Shrouding** (−6, Subject) + aura; All → 105. Updated the `RangeModifier`/`ApplyRangeModifier` doc comments
  (were Aircraft/Subject-only) to describe both seats. Tests: new `RangeModifierRuleIntegrationTests` (out-of-
  range target brought in by Increased Shooting Range; in-range target pushed out by a defender's Ranged
  Shrouding; the query fold +6/−6/net-0; catalogued+resolvable). Verified: engine 909/0, full build, headless
  exit 0.
  - **Deferred (recorded, not silently cut):**
    - **Darkborn** — bundles a range modifier (defensive −4 floor 6, or offensive +3 depending on army) WITH a
      charge-move-distance modifier ("−2\" movement to charge" / "+3\" charge move"). The range half fits this
      primitive, but the charge-move half needs a charge-distance-modifier primitive that doesn't exist yet
      (#029 movement-modifier territory). Left whole so it lands faithfully later, not half-wired.
    - **"to a min. of 6\"" floor variant** — some armies floor the reduced range at 6"; the canonical Ranged
      Shrouding is a flat −6 (the floor is an army-specific variant, like Fortified's). Not modelled; the only
      floor applied is `max(0, …)`.
    - **Movement-overlay shooting preview** — `GuiDefineMovementResolver`'s "Show targeting" recomputes raw
      `weapon.RangeInches` (lines ~892/1043) to show "what could I shoot after moving". It still uses base
      range, so it under-/over-reports for a unit with these rules. Actual shooting is unaffected
      (ChooseRangedAttackStage is authoritative). A full fix needs a per-enemy defender eval the attacker-only
      `WeaponSightProfile` can't carry — deferred.
- 2026-06-29: **Strider slice DONE** (branch `102-movement-range-validation`, both repos). Settled the open
  question first (see Decisions): the Difficult cap IS authoritatively enforced at the stage level
  (`DefinePathStage`/`ConsolidateStage`/`MovementExecutor` all pass real terrain to `ValidatePaths`), so
  Strider wired there is real, not preview-only. Built exactly parallel to the `canMoveThroughEnemies`
  precedent: new `MoveThroughTerrainContext` (at the pre-existing-but-unused `Movement_OnMoveThroughTerrain`
  hook) + `MovementRuleQueries.IgnoresDifficultTerrain` reading `RuleOperation.IgnoreTerrainEffects`; new
  `ignoresDifficultTerrain` param threaded through `ValidateMovingThroughDifficultTerrain` (early-return skip)
  and the three terrain-aware `ValidatePaths` overloads; `IgnoresDifficultTerrain` flag on
  `DefineMovementPathRequest` + `ConsolidationMoveRequest`; computed+passed at all 4 engine sites
  (DefinePathStage, ConsolidateStage, MovementExecutor.TryMove, GameOperationServices.MoveUnit); read+passed
  at all 6 resolver sites (AiDefineMovementResolver ×3 + its centroid difficult-cap pre-clamp now skipped,
  AiConsolidationMoveResolver, GuiDefineMovementResolver, GuiConsolidationMoveResolver, CLI
  DefineMovementPathResolver ×4, CLI ConsolidationMoveResolver). Catalogued `Strider`
  (`IgnoreTerrainEffects` passive) + registered in `All` (101 rules) — **this also fixes its absence from the
  Army Creator picker** (the 2026-06-28 note below). Tests: new `MovementStriderValidationTests` (over-cap
  difficult crossing rejected without / allowed with the flag, on both the charge and no-charge overloads;
  query true/false; catalogued+resolvable); updated `MovementFlyOverValidationTests` for the new param.
  Verified: engine 905/0, full `dotnet build`, headless exit 0.
  - **Scope (recorded, not silently cut):** Strider waives ONLY the difficult-terrain move cap. Dangerous-terrain
    tests (`ApplyNonMovementTerrainEffectsStage`) and the enemy move-through block are untouched — Flying's
    "ignore all terrain + move through units" facet stays #029, which can reuse the same `IgnoreTerrainEffects`
    effect once those consumers also honour it (would need the op to distinguish Strider-scope from Flying-scope,
    e.g. a parameter or a second op). **RangeModifier family (Increased Shooting Range / Ranged Shrouding /
    Darkborn) is the remaining half of #102 — not started.**
- 2026-06-28: **Strider is also absent from the Army Creator picker** (user-reported; verified). Root cause: there is **no `Strider` `SpecialRuleDefinition` in `CoreRuleCatalog`** at all — not in `CoreRuleCatalog.All`. The army-builder rule picker is derived entirely from `CoreRuleCatalog.All` ∪ the open army's embedded rules (`SaveLoad/SpecialRuleRegistry.GetPickerEntries` → `ArmyBuilderScreen.RefreshRuleNames`), so an un-catalogued rule simply never appears. The engine *does* declare `Effect.IgnoreTerrainEffects` (`Rules/Definitions/Effect.cs`, doc'd as "covers Strider (difficult terrain only) and Flying") and `RuleOperation.IgnoreTerrainEffects` (`Rules/Definitions/RuleOperation.cs`), but nothing **consumes** them — `MovementExecutor.ApplyDangerousTerrainEffects` and `MovementUtilities.ValidateMovingThroughDifficultTerrain` never query a terrain-ignore flag. So Strider is a no-op end to end *and* unpickable. **When this item is built, cataloguing `Strider` (a `SpecialRuleDefinition` carrying `IgnoreTerrainEffects`) must land together with the validation-threading** — adding it to `All` first would surface a pickable rule that does nothing (the exact "silent mis-scope footgun" #059 guards against). Same applies to the RangeModifier-family rules (Increased Shooting Range / Ranged Shrouding / Darkborn): none are catalogued either, for the same reason. Aircraft/Flying (#029) likewise.
- 2026-06-22: Opened to hold the invasive movement/range-modifier work deferred out of #100 (where #1, #3, #4-Shred/Heal/RestrictActions, and all of #2 landed). Number **102** chosen at the user's request — free on `origin/master` (top item there is 098); confirm no collision at merge per the never-reuse rule (another in-flight branch could have claimed it). Engine effects (`IgnoreTerrainEffects`, `RangeModifier`) are already declared with `.Apply`; the work is the consumer + the cross-repo flag threading, not new effect types.

## Decisions
- 2026-06-29: **RangeModifier — threaded at the single authoritative gate, not every resolver.** Unlike Strider
  (whose enemy/terrain validation is duplicated across engine stages + every GUI/CLI/AI move resolver), shooting
  range is decided in exactly ONE place — `ChooseRangedAttackStage`'s target enumeration — and the three
  ChooseRangedAttack resolvers consume its `modelsThatCanShoot` set rather than recomputing. So threading the
  delta there covers actual play and all three resolvers at once. The only independent recompute (the *movement*
  overlay's shooting preview) is a planning nicety, deferred (see Notes).
- 2026-06-29: **Seat carries direction.** `Effect.RangeModifier(Delta)` is direction-agnostic; the HookEntry's
  seat decides whose range. Actor = the bearer's own weapons (+, Increased Shooting Range); Subject = enemies
  shooting the bearer (−, Ranged Shrouding / Aircraft). One two-participant `EvaluateAllNamed` (attacker Actor +
  weapon, defender Subject) sums both into one delta — symmetric with how the save pipeline folds an attacker's
  AP and a defender's Shielded bonus.
- 2026-06-29: **Open question resolved — the Difficult cap is authoritatively enforced at the STAGE level,
  not just in resolver previews.** Mapped every `ValidatePaths` call site across both repos: `DefinePathStage`,
  `ConsolidateStage`, and `MovementExecutor.TryMove` all pass real terrain (`context.RelevantTerrain` /
  `TableState.Terrain.Objects`) into the enforcing overloads and throw `RequestResponseInvalidException` on a
  violation. `PathTemplate.Validate` does call the `terrain: null` overload (so the template preview itself
  doesn't enforce the cap), but the resolvers run their OWN terrain-aware `ValidatePaths` for the gray-out, and
  the stage re-validates authoritatively. ⇒ Strider must be honoured at the stage validators (done) and at the
  resolver previews (done) — wiring it is real, not cosmetic.
- 2026-06-29: **Followed the `canMoveThroughEnemies` precedent exactly** rather than inventing a new threading
  shape — single non-logging `MovementRuleQueries` read, flag rides the request for resolvers, stages recompute
  the flag locally (stages don't read requests). Keeps the two rule-derived movement flags symmetrical.
- 2026-06-29: Added `ignoresDifficultTerrain` as a REQUIRED param on the terrain-aware `ValidatePaths` overloads
  (right after `canMoveThroughEnemies`), with the back-compat convenience overloads passing `false` — same
  pattern #090 used for the fly-over flag. The no-enemy `terrain`-overload is only ever reached with
  `terrain: null`, so it just hardcodes `false` (the difficult check is a no-op there regardless).

## Outcome
Closed 2026-06-29. The whole original scope is implemented, catalogued, tested, and merged to master:

- **Strider** — waives the difficult-terrain move cap; `MovementRuleQueries.IgnoresDifficultTerrain` threaded
  through `ValidatePaths` across both repos. Also fixed its Army-Creator-picker absence.
- **Increased Shooting Range** (+6, Actor) and **Ranged Shrouding** (−6 with a "min 6\"" floor, Subject) — a new
  `Shooting_OnRangeCheck` hook + `RangeRuleQueries.EffectiveRange` folded into `ChooseRangedAttackStage`'s
  authoritative target-eligibility check; both with auras.
- **Offensive Darkborn** (+3 range, +3 charge) — range via the RangeModifier primitive, charge via the existing
  live `MovementBonus(Charge)` move-distance seam (no new primitive needed).
- **Move-overlay shooting preview** — `DefineMovementPathRequest.WeaponRangeOverrides` precomputed in
  `DefinePathStage`, read by the "Show targeting" overlay so the post-move shooting preview matches the engine.

`CoreRuleCatalog.All` 100 → 106 over the item. Net new primitives: `Shooting_OnRangeCheck` hook + `RangeModifierContext`
+ `RangeRuleQueries`; `Effect/RuleOperation.RangeModifier.MinResultInches` floor; `MoveThroughTerrainContext` +
`IgnoresDifficultTerrain`; the `ignoresDifficultTerrain` validation flag; `WeaponRangeOverride` on the move request.

**Follow-up (NOT in this item):** **defensive Darkborn** — the corpus's other same-named rule, where *enemies*
get −range/−charge vs the bearer (both Subject seat). Its range half is now expressible (`RangeModifier(−4, floor
6)`, Subject), but its **charge** half can't be wired today: it reduces the *charger's* distance based on the
charge *target's* rules, and the live charge computation (`MovementActionContext.AccumulateMovementRules`) only
folds the mover's own (Actor) rules — there is no per-target charge-distance path (the `Movement_OnChargeDeclared`
Subject seam exists in tests but isn't fired in live play). That's a distinct mechanic (a target-specific charge
budget, or applying the debuff after charge-target selection) and belongs in its own item (movement-modifier /
#029 territory), not forced in here. Same shape would unblock Aircraft's "−12\" to enemies targeting it" range/charge facets.
