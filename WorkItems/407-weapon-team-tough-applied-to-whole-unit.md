# 407 — Weapon Team's Tough(3) is applied to every model in the unit

**Status**: done (2026-10-28)
**Related**: #410 (other per-model item rules), #093 (per-model special rules), #197 (champion upgrades kept off `unit.SpecialRules`), #383 (per-model upgrade sections), #324 / #323 (Replace upgrade accounting)

## Goal

A "Replace one Rifle and CCW -> Weapon Team (..., Crew (A2), Tough(3))" upgrade makes only the one
upgraded model Tough(3); the rest of the squad keeps Tough(1). A 10-model Human Defense Force Infantry
Squad with one Weapon Team has 12 wounds, not 30. "Done" = item-granted defensive rules from a
per-model upgrade land on the bought model only, in both the Army Forge compile and the OPR import, with
an integration test, and a corpus census of other books with the same shape.

## Notes

- 2026-10-28: **Implemented.**
  - **Format.** `UnitFileEntry.ModelGrants` is a list of `ModelGrantEntry {Count, Rules}`, meaning
    "N models also have these rules". It is nullable and omitted on save, so older army files and units
    without a grant round-trip byte-for-byte.
  - **Compile.** `ListCompiler` records the items each One/Any section buys (including #323 starved
    retries). After all sections, `PlaceModelGrants` turns their model-personal rules into grants, and
    the unit fold skips those rules. The count is clamped to the copies still held, so an item a later
    section replaced away grants nothing. Single-model units and grants covering every model stay
    unit-wide, so a hero's mount Tough still reaches the #006 join. #107 combined pairs keep each half's
    grant.
  - **Import.** `OprListImporter` does the same when a loadout item's `count` is below the unit size,
    via the shared `ListCompiler.TryAddModelGrant`. The no-loadout fallback shape has no per-model
    count, so it stays unit-wide.
  - **Load.** `GameBootstrap.AttachModelGrants` attaches the rules to the unit's LAST N models, which
    `WoundAllocation.Order` reaches last. `UnitCreationRules` evaluates each rule-carrying non-hero model
    with just its own rules joined to the unit's, so the model takes the higher Tough. Transport space
    follows the per-model wounds for free (12 spaces, not 30). Model rules and `TotalWounds` already
    persist through save/resume.
  - **Display.** The Forge header pill reads "3 (1 model)"; the in-game army-list pill appends
    "(N models)" when fewer than all rank-and-file models share the shown Tough.
  - **Tests.** `PerModelToughGrantTests` (engine, 8) and `PerModelToughShippedDataTests` (app: pins the
    16-site population and compiles each real site). Engine 3374 and app 2997 tests green, build clean,
    headless smoke exit 0.
  - **Known, not fixed:** a saved army plays from its stored units, so an HDF list saved before this
    keeps the unit-wide Tough until it is reopened in the Forge and re-saved (that recompiles it).
    The reporter needs to do this.
- 2026-10-28: Design settled with the owner: **per-model grants in the army file, Tough only for now**.
  Shielded, Caster, Fear, Vengeance and the rest of the one-model item rules stay unit-wide; deciding
  each of them is #410. Census, all 90 books:
  - 294 item-granted Tough sites on per-model sections. Almost all are single-model heroes buying a
    mount, where unit-wide is already right.
  - On multi-model units there are exactly **16, all "Weapon Team"**: HDF Infantry Squad and Veterans,
    and AoF Ratmen Warriors.
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

- **2026-10-28 - per-model grants, Tough only (owner sign-off).** The alternatives were routing every
  non-aura rule per model (each one then needs partial-unit semantics, which is bigger and riskier) and a
  Tough-only field (the next per-model rule would need another format change). The Tough model goes
  last in the unit, so automatic wound allocation reaches it last; the defender can still pick it.

## Outcome

**Done 2026-10-28, hand-verified by the owner** in `Scenarios/408-versatile-backout` (which has both units). Checked: In the Forge, a HDF Infantry Squad with a Weapon Team shows "Tough 3 (1 model)".
In game it has 12 wounds, and the army-list pill reads "3 (1 model)".
