# 102 — Movement & range modifier validation threading (Strider, RangeModifier family)

**Status**: in progress — Strider (difficult-terrain cap waiver) landed 2026-06-29; RangeModifier family still open
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
_(written when closed)_
