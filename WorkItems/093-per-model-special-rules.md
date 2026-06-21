# 093 — Per-model special rules: the general reckoning

**Status**: todo
**Related**: #006 (Hero — forced the issue and bakes in the first per-model exceptions), #042 (rule framework — unit/weapon scope), #027 (weapon scope), #023 (Tough wound-priority), #031/#032 (rule implementations)

## Goal
Revisit, holistically, the engine's assumption that special rules are **unit-wide** (with weapon-scope the only exception). #006 is adding **per-model** rule/stat carriage as targeted exceptions for the Hero (per-model stats; per-model *combat* rules; weapon rules already per-model via the weapon). That patchwork is deliberate and bounded, but it leaves the general question unanswered and several corners deferred. This item is the reckoning: decide whether per-model scope should be a first-class, uniform capability across **all** hook categories, audit where unit-wide assumptions are baked in, and absorb the corners #006 punts.

"Done" = a coherent, documented scope model (unit / model / weapon) applied consistently by the dispatcher, the deferred corners below handled or explicitly and durably waived, and the #006 hero exceptions either subsumed by the general mechanism or confirmed as legitimately hero-specific.

## Why this exists (the worry to revisit)
The user flagged (2026-06-15) that doing per-model as hero-shaped exceptions risks biting us later — different code paths deciding "does this rule apply to this model?" in different stages, inconsistently. In OPR the tabletop truth is that rules/stats are **per-model**; unit-level is a shortcut for homogeneous units, and it's "a little loose" (units may have models that don't all share a rule). So the general direction is likely "unit-level default + per-model overrides, unioned by the dispatcher everywhere," not "hero special cases."

## What #006 bakes in now (the exceptions to revisit)
- **Per-model STATS** via `HeroStatRules` (Quality/Defense/wounds), read model-override-then-unit at exactly three stages (hit / save / morale). Generalizes cleanly but is currently reached only through hero-aware helpers.
- **Per-model COMBAT rules** via `ModelData.RuleDefinitions` + `ERuleScope.Model` + model-aware dispatch on hit/save/wound hooks (slice F). This is the seed of the general mechanism — make sure it's built general, not hero-only.
- **Hero-specific behaviors** that are NOT generic per-model and should stay special: wounds-last, morale-on-behalf (unit borrows a model's Quality), last-model Defense (a model *suppresses* its own stat until sole survivor). Confirm these stay layered on the Hero marker.
- **Weapon-scoped rules** already per-model (ride the weapon; `WeaponComparer` keeps a rule-bearing weapon its own batch). Already consistent — leave alone.

## Deferred corners #006 punts here
- **Per-model MOVEMENT rules** (Fast/Slow on one model, e.g. a joined hero): the move budget is unit-level. Needs per-model budgets and a coherency interaction (a faster model still can't break coherence). Movement-subsystem change.
- **Per-model ACTIVATED abilities** (a joined hero with Caster / Vanguard / Martial Prowess): the activation/offer layer assumes unit-level abilities; needs to offer/resolve a single model's ability inside a host unit.
- **Defensive / morale "all models have this rule" rules** (Stealth, Regeneration, Fearless): rule text gates on *every* model having it. With mixed-rule units this needs an explicit all-models check rather than "the unit has the rule." Audit these once models can carry rules independently.
- **Unit-wide stat/rule read sites not yet audited**: any place that reads `unit.Quality`/`unit.Defense`/`unit.RuleDefinitions` assuming homogeneity. Catalogue them; route through the per-model-aware accessors.

## Deferred corners #006 punts here (cont.)
- **Impact(X) per-model** (a joined hero with Impact): fires on `Melee_OnChargeContact`, which is not weapon-batched and has no "firing model" concept, so it can't ride slice F's weapon-batch-owner dispatch. Needs a relevant-model resolution for charge-contact hooks (which models with the rule are in contact). Confirmed during slice F.
- **Dispatcher dedup keyed by `UnitID`, not `ModelID`** (`RuleEvaluator.DedupState`): argument-less rules fire at most once *per unit*. Fine while only one model (the hero) carries per-model rules, but a genuinely mixed unit (two models with the same argless rule) would wrongly collapse to one. Re-key to `ModelID` when per-model becomes general.
- **`ERuleScope.Model` as an authoring level**: slice F deliberately does NOT add it (the merge moves rules onto a model in engine code, bypassing army-load scope validation). If models ever become army-file authoring targets, scope needs a Model level + loader support.

## Folded in: combat-kind condition (melee-only / shooting-only)
Originally spun off from the #015/#016 close-out as its own #093 ("combat-kind condition"); folded here on 2026-06-21 when a number collision surfaced at merge time (both items had independently claimed 093) and the two were judged close enough to live together as #042-framework refinements. Different axis from per-model *scope* (this is combat *kind*), but tracked here per that decision.

**Goal:** Make "applies in melee only" / "applies in shooting only" a first-class `Condition` in the #042 data-driven `Condition × Effect` system, so a rule (or the #015/#016 effect seams) can declare its combat-kind gating as data instead of hand-wiring an `IsMelee` check. "Done" = a rule definition can carry a combat-kind condition the `RuleEvaluator` honours, with at least one existing ad-hoc gate migrated onto it to prove the path.

**Background:** combat kind is already known at evaluation time — `IsMelee`/`IsCharging` are threaded through `HitRollModifierContext`/`HitRollCompleteContext` — but every rule that cares gates on it imperatively and individually: Indirect (`Not(IsMelee)`), Thrust (melee + charging), Furious's melee gate, #051's charging gate. That scatters the concept and makes new combat-kind-specific effects (the #015 attack-count and #016 per-hit-save seams) re-invent the gate.

**Pointers:** Conditions live under `Rules/Dispatch/` (`CoreRuleCatalog.cs`, `RuleEvaluator`); mirror an existing context-evaluated condition. The combat-kind flag is already on the contexts — a read of existing state, not new plumbing. Migrate Indirect's `Not(IsMelee)` first as the proof-of-path. Relevant to #015 (attack-count mods are often shooting-only, e.g. Rapid Fire) and #016 (per-hit save effects may be combat-kind-specific).

## Notes
- 2026-06-21: Folded the combat-kind-condition follow-up (above) into this item; removed its separate `093-combat-kind-condition.md` and re-pointed the #015/#016 ledger here.
- 2026-06-19: Slice F (building now) lands the first real per-model carriage — `IModel.RuleDefinitions` + a model-aware `RuleEvaluator.EvaluateAll` overload on the hit hooks. Built general (not hero-only): the seam to generalize from. Recorded the corners it punts above.
- 2026-06-15: Opened at the user's request while building #006. Captures the per-model generalization so the hero-shaped exceptions don't silently become the de facto (inconsistent) model. Pick up after #006's slices land, when the per-model seam (`ModelData.RuleDefinitions`, model-aware dispatch, `HeroStatRules`) actually exists to generalize from.
