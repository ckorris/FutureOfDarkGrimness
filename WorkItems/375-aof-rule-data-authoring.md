# 375 — AoF rules pt.1: data authoring (renames + composable residue)

**Status**: todo
**Related**: #376 (primitives half), #377 (spells), #378 (books), mirrors the #196/#197 split that closed GDF coverage. Reference doc: `/home/chris/Projects/GDF Armies/Age of Fantasy/Special Rules and Spells by Army.md` (local only, copyrighted extract — never copy its text into the repo; see the CLAUDE.md in that folder).

## Goal

Every AoF-book rule name that can be expressed with the existing effect/condition vocabulary
resolves to a definition that actually fires, authored as data. Done means: the AoF corpus
census (to be built like #196's) reports zero dead refs attributable to authorable rules —
whatever genuinely needs a new primitive is explicitly handed to #376, nothing dropped
silently.

Appraisal baseline (2026-08-22, against the ~347-name GDF vocabulary): 40 books, 306
distinct rule names, 852 instances.

- **181 names** already match by name (shared families: Fortified, Resistance, Steadfast,
  Caster Group, the aura library, the Disciples "-bound" families, ...). Expect mostly
  zero work; spot-check text drift.
- **67 names** are exact-text renames of existing GDF rules (Bestial=Scrapper,
  Shadowborn=Darkborn, Empyrean Spirit=Screened, Destroyer=Warbound, Unmovable=Honor Code,
  Lucky=Ferocious, Cursed Undead=Self-Repair, ...) plus **13** Boost/Aura derivatives that
  resolve after substituting the renamed base. Authoring is aliasing/cloning existing defs
  under the AoF names.
- **~41 residue names** (of 45; the other 4 go to #376) are composable from existing
  effects: Boost upgrades (unconditional AP-reduction, always-on -1 to hit, threshold or
  dice tweaks like 2D3 placement), Buff/Mark/Aura wrappers, terrain-proximity rules
  (`mostModelsWithinInchesOfTerrain` exists), spell-only wound ignores (`isSpell` exists),
  scoped variants (Takedown when Shooting), simple stat rules (Good Fighter).
- **7 same-name rules** have AoF text that diverges from the GDF definition (mechanically
  meaningful: Fortified Growth marker timing; LoS clauses on Difficult Terrain Debuff and
  Quick Shot Mark) — author as per-book `ruleDefinitions` overrides, not global edits.

## Design forks to surface before building

- Where AoF defs live: a separate `AofRuleSupplement.json` vs extending
  `GdfRuleSupplement.json` vs book-embedded only. Interacts with name collisions between
  systems and with #378's book bundling.
- Whether renames are aliases (one def, many names) or cloned defs; the rule-name hover
  glossary (#259) and rule tracing should show the AoF-facing name either way.

## Notes

- 2026-08-22: Filed. Appraisal numbers above come from a machine-verified comparison of the
  reference doc against CoreRuleCatalog + GdfRuleSupplement + book defs; ~94% of the 852
  instances resolve via existing behavior modulo renames.

## Decisions

## Outcome
