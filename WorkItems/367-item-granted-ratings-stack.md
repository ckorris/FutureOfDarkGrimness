# 367 — An item-granted rated rule was discarded instead of stacking

**Status**: done
**Related**: #241 (share-link importer), #356 (editable-session drift), #153 (ListCompiler), #035 (Transport)

## Goal
A unit that buys an upgrade granting a rule it already has ends up with the SUM of the two ratings, the
way Army Forge shows it - Extended Cargo on a Transport(6) Procession Altar makes it Transport(12), which
AF prints as "Transport(+6)" on the upgrade line. Both list paths (Forge-built and share-link import) must
agree, and the sample armies shipped with the repo must reflect the corrected values.

## Notes

- 2026-08-08: Reported from a real import - the 2k Blessed Sisters list's Procession Altar loaded with
  Transport(6) despite Extended Cargo being bought (and charged). Traced to the rule-merge convention in
  three places: `ListCompiler` items fold (`!unit.SpecialRules.Contains(rule)`), `ListCompiler.AddGains`
  and `OprListImporter.AddRule`. An identical rating was dropped as a duplicate; a differing one left two
  entries where the engine's query layer then took the max (`CapabilityRuleQueries.TransportCapacity`,
  `MaxWoundsSink` - both documented "highest wins").
- 2026-08-08: Corpus scan over all 40 bundled books, upgrades granting a rated rule the unit already has:
  **Tough 97, Impact 23, Transport 7, Caster 2** - every mounted hero in the corpus (Champion of Change on
  a Disc, Celestial High Sister with Holy Wings, Elven Noble on a jetbike) was playing at half its Tough.
  Every one of those arrives through an ITEM; not one is a bare `rulesGained`. A second shape exists in the
  roster itself - a unit whose OWN item repeats a rating it has as a rule (Orc Great Battle Truck /
  Combat Beast / Beast Titan "Extra Space", Eternal Dynasty "Syen & Xaotyan") - and stacks the same way.
- 2026-08-08: Shipped. `ListCompiler.AddGrantedRule` is the single merge point, used by the items fold and
  by `OprListImporter`'s loadout path. 5 tests (3 compiler, 2 importer), all mutation-verified. Suite
  2937/2937, full `dotnet build` clean, headless smoke exit 0. The real book now compiles the Procession
  Altar to exactly the Army Forge screenshot: `Devout | Fast | Fearless | Impact(3) | Tough(6) |
  Transport(12) | Courage Buff | Guarded Buff | Precision Shooter Buff`, 260 pts.

## Decisions

- **Stack at list-build time, not at the engine query.** Fixing `TransportCapacity`/`MaxWoundsSink` to sum
  would not have worked at all: identical ratings never reach the unit, they are dropped by the compiler.
  It would also have put runtime buff sources (auras, spells, hero-join) at risk of double-counting, which
  is exactly what "highest wins" protects against. Those queries are unchanged and stay max.
- **Keyed on "is a rated rule", not on a name list.** Chosen over an allowlist of the four names that
  collide today so a future book's rated rule stacks by default instead of being silently swallowed.
  Un-rated duplicates still dedupe - a second Fearless is not two Fearlesses.
- **Item grants only; bare `rulesGained` keeps the plain dedupe.** No corpus option grants a rated rule
  through the bare shape, and the share-link JSON cannot distinguish a bare gain from an echo of the base
  rule - so summing it would risk double-counting on the import path while the Forge path summed. Keeping
  both paths on the same rule matters more than covering a shape that does not exist yet. Revisit together.
- **Absorption is not a grant.** A #107 combined pair and a joined hero keep `Contains` dedupe: the host
  does not get tougher because its passenger is Tough. Pinned by a test that fails if either is switched
  to `AddGrantedRule`.
- **The import path trusts `loadout`, once.** A share list carries the same rating up to three times -
  `rules` (base), `loadout` (final gear), and the selectedUpgrade's own `gains`. Only the loadout copy is
  a second real grant, so the gains loop no longer re-reads `ArmyBookItem` content when a loadout is
  present; without one, the gear read is only the BASE kit, so gains are read there instead and stack.
  Evidence that `rules` is base-only: the saved Blessed Sisters file has Precision **Shooter** Buff and
  not Precision **Fighter** Buff, so its rule list was built from the final loadout, not from `rules`.
- **`with` cannot be used to change a rating.** `PrintableName` is a positional member of the BASE record
  and a copy-expression keeps the old text, so `existing with { NumericValue = 12 }` yields an entry that
  prints "Transport(6)" - and record equality keys on that name, so it compares unequal to its own value.
  Caught mid-implementation when the real book still compiled to Transport(6) while the tests (asserting
  `NumericValue` alone) passed. The helper constructs a new entry, and the tests now assert by value.
- **The stale sample armies were migrated surgically, not recompiled.** Recompiling a saved `.fdgarmy`
  from its embedded selections rewrites far more than this fix: 18 of 23 files change point costs and the
  AF-imported ones lose their `(Combined)` unit-name markers (the #356 divergence). Instead each affected
  unit's rating was edited in place through a `BuiltArmyFile` round-trip that was first proven byte-identical
  on the three target files, giving a one-line diff each. Deliberately untouched: `3k - High Elf Fleets`,
  whose Elven Nobles differ from their rebuild by extra Fear(1)/Caster(2) rules - pre-existing
  reconstruction drift, not this bug.

## Outcome
`ListCompiler.AddGrantedRule` stacks a rated rule granted by an item onto a same-named rule the unit
already carries, and both list paths route through it. Three sample armies corrected: the Blessed Sisters
Procession Altar (Transport 6 -> 12) and the High Elf Fleets Elven Noble / Orks Veteran Leader mounts
(Tough 3 -> 6). Deferred, stated rather than dropped: bare `rulesGained` collisions (no corpus case, and
undecidable on the import path), and rated rules on WEAPONS (scanned - no corpus collisions). Any other
already-saved `.fdgarmy` keeps its old value until re-imported or re-saved; only the repo's own samples
were migrated.
