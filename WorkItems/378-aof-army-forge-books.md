# 378 — Age of Fantasy armies in the Army Forge

**Status**: todo
**Related**: #156 (Army Forge catalog builder), #375/#376 (rules), #377 (spells), #259 (rule glossary shows unenforced rules). Source PDFs + verified rules reference: `/home/chris/Projects/GDF Armies/Age of Fantasy/` (local only).

## Goal

The Army Forge screen can build armies from all 40 Age of Fantasy faction books exactly as
it does for the 47 GDF books: bundled `.fdgbook` files ship in `FdgRaylib/Assets/Books/`,
import is reproducible from local Army Forge JSON snapshots, and the UI lets the user pick
the game system / find AoF factions. AoF shares the GDF core ruleset, so no engine changes
are expected here beyond what #375/#376 deliver.

Concrete pieces:

- `ArmyForgeBookService.GameSystemSlug` hardcodes `"grimdark-future"`
  (`FdgRaylib/Import/ArmyForgeBookService.cs:27`) — parameterize for `age-of-fantasy`
  (verify the exact slug/system id against the Army Forge API; share links carry
  `header.GameSystem` already).
- Fetch and keep AoF Army Forge JSON snapshots (mirror
  `/home/chris/Projects/GDF Armies/opr-json-snapshots/`, outside the repo).
- Import -> bundle the 40 `.fdgbook` files (units, upgrade packages, weapons,
  ruleDefinitions, spells arrays for #377), with `OprBookImporter.AsciiFold` on all text
  and per-book default effect-set keys chosen for #379.
- Wire whatever supplement file #375 decides on into book load, and make the #259 glossary
  / import summary reflect AoF rule enforcement honestly.

## Design forks to surface before building

- Game-system selection UX in the Forge screen (toggle? separate list? per-book tag) — and
  whether GDF/AoF armies can meet in a lobby (points and core rules are compatible;
  decide, don't drift into it).
- Snapshot versioning: GDF snapshots are pinned at OPR v3.5.x; pick and record the AoF
  snapshot version (the PDFs on disk are v3.5.2-3.5.3).

## Notes

- 2026-08-22: Filed. 40 AoF PDFs on disk; rules/spells appraisal in #375-#377.

## Decisions

## Outcome
