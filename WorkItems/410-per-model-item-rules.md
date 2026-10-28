# 410 — Decide which other one-model item rules belong to the model, not the unit

**Status**: todo
**Related**: #407 (per-model grants, Tough only), #093 (per-model special rules), #197 (Sergeant champion marks)

## Goal

#407 added `UnitFileEntry.ModelGrants` and routes only Tough through it. Every other rule an item grants
from a one-model or some-models upgrade still folds onto the whole unit. For each such rule, decide
whether it describes the model that bought it or the unit, and route the model ones through the grant.
"Done" = each rule in the census below is either routed per-model, with partial-unit semantics that
work, or recorded as correctly unit-wide.

## Notes

- 2026-10-28: Filed as the follow-up promised in #407. Number taken from `origin/master` at `2ee3458`
  after `git fetch`: index + archive max **409**. No collision.
- 2026-10-28: Census, all 90 books, per-model sections (Affects One/Any) on multi-model units: 574
  item gains.
  - **Auras dominate.** Regeneration, Courage, Versatile Reach/Defense, Steadfast and the like. These
    are most likely correct unit-wide, since an aura already means "this unit".
  - **The candidates to decide:** Caster (20), Vengeance (13), Shielded (13), Precision/Piercing
    Target/Tag/Spotter (8-11 each), Repel Ambushers (10), Fear (8), Spell Accumulator (7), Ambush
    Beacon (5), Spell Conduit (4), Impact (2), Mend (1), Bane in Melee (1).

## Facets to settle

- Per rule: model or unit? Shielded and Impact read like the model; Caster probably the model, but its
  spell tokens are unit state today.
- What a model-carried rule does once its model dies (the rule should go with it).
- Whether the engine's model-rule seams (`EModelRuleScope`, #093) already fire these hooks per model.

## Decisions

## Outcome
