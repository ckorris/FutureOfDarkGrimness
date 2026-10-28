# 407 — Weapon Team's Tough(3) is applied to every model in the unit

**Status**: todo
**Related**: #093 (per-model special rules), #197 (champion upgrades kept off `unit.SpecialRules`), #383 (per-model upgrade sections), #324 / #323 (Replace upgrade accounting)

## Goal

A "Replace one Rifle and CCW -> Weapon Team (..., Crew (A2), Tough(3))" upgrade makes only the one
upgraded model Tough(3); the rest of the squad keeps Tough(1). A 10-model Human Defense Force Infantry
Squad with one Weapon Team has 12 wounds, not 30. "Done" = item-granted defensive rules from a
per-model upgrade land on the bought model only, in both the Army Forge compile and the OPR import, with
an integration test, and a corpus census of other books with the same shape.

## Notes

- 2026-10-28: Filed from a Discord report: "both infantry squads with a single crew weapon in the 1000K
  HDF list have tough 3 applied to all models in the unit, 30 wounds total". Number 407 taken in the
  same filing pass as #406, no collision.
- 2026-10-28: First look at the cause. In `HumanDefenseForce.fdgbook` the Weapon Team options grant the
  weapons plus an `itemsGained` entry "Weapon Team" carrying `Tough(3)`. `ListCompiler.AddGains` adds
  items to the unit's item list, and item rules end up unit-wide, so Tough's `SetMaxWounds` reaches
  every model. #197 already keeps per-model WEAPON-scoped rules off the unit (Sergeant); there is no
  equivalent for unit-scoped rules like Tough on an item bought for one model.

## Facets to settle

- **Where the one model's identity lives.** The army file has no per-model loadout; decide whether
  this rides #093's per-model rule carriage (like a joined hero) or a new per-model item record.
- **Wound allocation.** With one Tough(3) model in a Tough(1) squad, the existing Tough wound-ordering
  rules must take it last and keep its wounds separate.
- **Transport space.** A non-hero Tough(3) model costs 3 spaces (#098); the rest cost 1.
- **Census.** Every "Replace one ..." or "Upgrade one model ..." option whose item grants Tough, or any
  other model-scoped defensive rule, across all bundled books.
- **Display.** Unit card and army list should show the mixed Tough.

## Decisions

## Outcome
