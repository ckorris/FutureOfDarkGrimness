# 438 — Re-bake all 88 books from the 3.6.0 snapshots

**Status**: todo
**Related**: #415, #437 (importer first), #436 (supplements final first), #378 (`bake-aof-books.sh`), #219/#383/#239/#225/#357/#377 (post-bake passes), #440 (derived data), #441
**Branch**: `feature/opr-3.6`. Model/effort: Sonnet 5.5 / medium.

## Goal

`FdgRaylib/Assets/Books/` contains exactly the 48 GDF + 40 AoF 3.6.0 books, each baked reproducibly from the
off-repo snapshots by ONE script, with every post-bake curation pass re-applied, and the old 3.5 books
deleted (kept off-repo as `~/Projects/GDF Armies/fdgbooks-3.5-final/` for #440's legacy tests).

1. `scripts/bake-books.sh [gdfSnapshotDir] [aofSnapshotDir]` generalising `bake-aof-books.sh`: GDF books bake
   against `GdfRuleSupplement.json`; AoF against GDF + AoF + `AofBookOverrides/<name>.json` last; skip
   0-unit parents; output names `<CompactName>.fdgbook` / `AoF-<CompactName>.fdgbook`. Retire
   `bake-aof-books.sh` (or make it call the new one).
2. Post-bake passes, in the order the 3.5 books received them, each re-run and recorded here with its
   summary line: `--import-book` (unpriced flags, live API - only if #437 finds it still necessary),
   `--import-section-shapes` (live API), `--retrofit-effects` (effect-set keys; FIRST extend the keyword
   tables for 3.6 weapon names - "Overrun", the renamed guns/blades, new AoF weapons - and pick a per-phase
   default for Assault weapons per #424), `--retrofit-bases` (should be a no-op; confirm), `--import-spells`
   (after #439's parser lands - bake once before and once after if the schedule requires).
3. Per-book `defaultRangedEffectSet` / `defaultMeleeEffectSet` for the new book (Voidspace Stalkers) and
   the renamed ones (carry over from Wormhole/Rift Daemons of X).
4. Delete the 3.5 `.fdgbook` files; add the new names; `git mv` where OPR only renamed, so history follows.
5. `--rule-coverage FdgRaylib/Assets/Books` -> Dead: 0 (or the #434/#435 ledger); `--book-to-army` + headless
   smoke for four books (two per system).

"Done" = the bake is one command, the books are 3.6.0, and this file lists the pass-by-pass summaries.

## Acceptance

`ls FdgRaylib/Assets/Books/*.fdgbook | wc -l` = 88; every book's `version` = `OPR v3.6.0`; coverage per
above; the Forge opens every book (quick scripted compile of all units via `--book-to-army` over the folder).

## Code anchors (survey 2026-10-29)

- `scripts/bake-aof-books.sh`, `FdgRaylib/Program.cs` tool flags (`--import-opr`, `--apply-rules`, `--import-book`,
  `--import-section-shapes`, `--import-spells`, `--retrofit-effects`, `--retrofit-bases`, `--book-to-army`),
  effect-set keyword tables (#239/#379, `FdgRaylib/Rendering/` effect sets), `FdgRaylib.csproj` Content globs.
  See the appended survey rows below.

### Survey rows (code survey 2026-10-29)

Findings that shape the item: there is NO GDF bake script - the recipe lives in
`WorkItems/196-faction-rule-data-authoring.md:88-112`; `--import-opr` without supplement args wipes the embedded
definitions (only generated "<Spell> Effect" entries survive); default effect sets are stamped at import only
for faction names present in `WeaponEffectAssigner`'s tables (so Voidspace Stalkers and the renamed Daemons
get none until the tables are updated).

- `scripts/bake-aof-books.sh:24-33` (:27 naming, :28-31 override by name). `ArmyBuilding/WeaponEffectAssigner.cs`
  :71-150 weapon keyword tables, :192-196 `NameOverrides`, :200-296 faction tables, :308 `IsAof`, :345-352
  `ApplyToBook`, :360-385 `ApplyToArmy`; `BookModel.cs:50, :54` default sets; `OprBookImporter.cs:118` stamp;
  `ListCompiler.cs:336` per-weapon keys (fallback :41-42).
- Refresh tools: `--import-book` `Program.cs:186-235` -> `ArmyForgeBookService.cs:82`; `--import-section-shapes`
  :237-287 -> `:132` (pins: `PerModelSectionShippedDataTests.cs:40` = 22); `--import-spells` :289-335;
  `--retrofit-effects` :507-545; `--retrofit-bases` :547-596 -> `BaseOrientationRetrofit.cs:25-45` (importer
  already length-first :808-847, so a no-op). `ArmyForgeBookService.cs:12-21` documents why wholesale re-import
  was avoided in 3.5.
- Project: `FdgRaylib/FdgRaylib.csproj:29-31` `Assets\**` glob; `FdgRaylib.Tests/ShippedBooks.cs:11, :21-24`
  (GDF/AoF split by the `AoF-` prefix).
- Corpus tests that re-run on the new books: `BookRuleCensusTests.cs:27-39`, `BookRuleScopeTests.cs:37, :107, :148-149`,
  `BookRuleFireLintTests.cs:25-29`, `BookSpellCoverageTests.cs:34`, `BookSpellCastProbeTests.cs:25-30`,
  `RuleSupplementLintTests.cs`, `RuleSupplementSetTests.cs`, `BundledBookRulebookTests.cs:80-90`,
  `ArmyForgeScreenTests.cs:330, :365, :401, :426` (compact filenames).
- The ~45 `*ShippedDataTests.cs` pin 3.5 counts / unit names / verbatim text (e.g. `ArmorShippedDataTests.cs:89`
  (11), `InquisitorialAgentShippedDataTests.cs:103` (20), `HazardousShippedDataTests.cs:152` (15),
  `SergeantShippedDataTests.cs:94` (12), `StrafingShippedDataTests.cs:119` (12), `TerrainDebuffShippedDataTests.cs:196`
  (14), `PerModelToughShippedDataTests.cs:54` (16), `ForgeCrossSectionReplaceShippedDataTests.cs:224` (54),
  `InstinctiveShippedDataTests.cs:16` (verbatim 3.5.3 text), plus ExtraAttack / UnpredictableMark / SurpriseAttack /
  RegenerativeStrength / VersatileDefense / Spawn / Split / Vengeance / Coordinate / ExtendedBuffRange / MeleeSlayer /
  GroundedAof) - every one is re-pinned against the 3.6 books in this item.

## Notes

- 2026-10-29: filed with the plan.

## Decisions

(none yet)

## Outcome

(open)

## Rules for the agent (plan section 1, restated)

- Branch `feature/opr-3.6` in BOTH repos; check `git branch --show-current` in each before committing.
  Never commit, merge or push `master`. Submodule-first commit cadence; subjects start with `NNN:`.
- 3.6 rule text: `~/Projects/GDF Armies/Rules 3.6.0/` (core PDFs' text extracts, common-rules JSON) and the
  3.6.0 book snapshots. Never commit anything from that folder. No 3.5 fallbacks or version flags.
- Vertical slice: engine change + CLI resolver + GUI resolver/overlay + AI resolver legality + an
  integration test mirroring the nearest `*RuleIntegrationTests` + the `docs/` line if a number changed.
- Verify per CLAUDE.md (engine suite green, full build, headless smoke exit 0) and
  `FdgRaylib --rule-coverage FdgRaylib/Assets/Books` not regressing. Never commit red.
- Keep this file's Notes/Decisions current and flip the row in `docs/opr-3.6-upgrade-plan.md` section 5.
  A design fork stops for Chris's sign-off with options + a recommendation written here.
