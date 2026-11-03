# 267 — Unit-wide abilities must require every model to have the rule

**Status:** implemented 2026-07-23, awaiting GUI hand-verify
**Related:** #183 (the passive-defensive all-models gate this is the activated sibling of), #197 (Teleport,
Fanatic, the reposition family), #093 (per-model rule dispatch, which is what makes the leak possible)

## Report

Giving a hero Teleport and joining it to a squad **without** Teleport let the Teleport ability move every
model in the combined unit. The ability should not be available unless every model has it. Audit for other
abilities with the same shape.

## Root cause (2026-07-23)

`CoreRuleCatalog.Teleport`'s `ActivatedAbility` was offered on `Condition.Always()`. Since #093, rules
dispatch per model as well as per unit, and `RuleEvaluator.GatherOffers` walks every living model's own
rules - so one hero's copy surfaced the offer for the whole unit, and `TeleportStage` then repositions
*every living model* of that unit.

The engine already has the right mechanism (`Condition.AllModelsHaveThisRule`) and even a validator that
enforces it - but `RuleValidator.CheckAllModelsGate` only inspects **passive** entries at **Subject**-seat
defensive attack hooks (#183's scope). Activated abilities were never checked at all, and neither were
Actor-seat passive entries. That blind spot is why four core rules and four supplement rules all shipped
ungated.

## Audit findings

Everything that repositions or re-activates the whole bearer unit, and what was done with it:

| Rule | Where | Effect | Verdict |
|---|---|---|---|
| Teleport | catalog, activated | `Teleport` | **gated** - the reported bug |
| Vanguard | catalog, activated | `TriggeredMove(9")` at deploy | **gated** |
| Fanatic | catalog, activated | `RepositionOnDeploy(9")` | **gated** |
| Martial Prowess | catalog, activated | `Reactivate` | **gated** |
| Wolfborn | supplement, passive | `RepositionAtActivation(D3)` | **gated** |
| Rapid Blink | supplement, passive | `RepositionAtActivation(D3)` | **gated** |
| Bounding | supplement, passive | `RepositionAtActivation(D3+1)` | **gated** |
| Rapid Blink Boost | supplement, passive | `RepositionAtActivation(D3)` | **gated** (see below) |

Deliberately **not** gated, with the reason:

- **Disembark / Embark** - engine-internal, not book rules and not carried per model; there is no "some
  models have it" state to gate on.
- **Ravage / Crossing Attack / Storm of X** (`DealHits`, `DealAutoWounds`, `StormOfHits`) - these are
  attacks the bearer makes, not a benefit conferred on the unit. A different question from this one.
- **Furious-grant and Heal support abilities** - they target another friendly unit, so the bearer's
  team-mates are irrelevant.
- **Re-Position Artillery** - `TriggeredMove` but `TargetAffinity: Friend`; the bearer orders *someone
  else* to move. Gating it would wrongly stop a lone hero from issuing the order.
- **The Harassing family** (`TriggeredMove` 3-6" after shooting / after being attacked) - shares the
  effect type but is a different class of rule and was not in the audit's scope. **Explicitly deferred,
  not overlooked:** if the same reading applies to them it is a separate call, and the lint does not
  currently cover them.

**Wording tension worth recording:** the corpus text for the reposition family is "place all models *with
this rule*", which would suggest repositioning only the rule-bearing models rather than blocking the
ability outright. Owner chose all-or-nothing for consistency with Teleport (2026-07-23), so
`RepositionPlacement` still moves the whole unit and the gate decides whether it runs at all. Rapid Blink
Boost's own text says "this model", but its effect sums into a radius that applies to every model, so it
is gated the same way rather than letting one hero widen the whole unit's reach.

## Fix

- **Data** — the four catalog rules take `Condition.AllModelsHaveThisRule()`; the four supplement rules
  swap `"kind": "always"` for `"kind": "allModelsHaveThisRule"` in `GdfRuleSupplement.json`, re-embedded
  into the 13 books that reference them (14 occurrences; ElvenJesters carries two).
- **Lint** — new `RuleValidator.ValidateAuthoring()` = `Validate()` plus `CheckUnitWideSelfEffectGate`,
  which covers **both** passive entries and activated abilities and fires on `Teleport`,
  `RepositionAtActivation`, `RepositionOnDeploy`, `Reactivate`, and self-targeted `TriggeredMove` at the
  deploy hook. Wired into `BookRuleSupplement`, so an ungated rule can't be authored or embedded.

**Why a separate method rather than extending `Validate()`:** `Validate()` is the army-**load** gate and
throws `RuleValidationException` on any violation, killing the whole load. Supplement definitions are
copied into saved `.fdgarmy` files, so an army exported before today embeds an ungated Wolfborn - folding
the check into `Validate()` would make every one of those files fail to open. The load gate stays
tolerant; the authoring gate is strict. `UnitWideAbilityGateTests` pins both halves of that split.

## Notes

- 2026-07-23 — implemented. Engine `5b000db` + app-side data. Tests: `UnitWideAbilityGateTests` (11 - the
  catalog-wide lint, the four audited rules by name, accept/reject fixtures on both the activated and
  passive paths, the two not-gated cases, and the load-gate-stays-tolerant split) and four new
  `TeleportRuleIntegrationTests` cases (hero-has/host-lacks, host-has/hero-lacks, both-have, unit grant).
  Engine 2038/2038 (was 2023), app 557/557, build clean, `--validate-rules` OK on 208 definitions,
  headless smoke exit 0.
- **Needs a GUI hand-verify:** rebuild the reported army - a hero with Teleport joined to a squad without
  it - and confirm Teleport no longer appears in the action menu; then confirm it still appears for a unit
  where every model has it, and for a unit under a Teleport Aura.
