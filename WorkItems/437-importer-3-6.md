# 437 — Importer 3.6: snapshot shape, version stamp, renamed/parent books, share-link gate, Assault class

**Status**: done 2026-10-30 (owner review: D1, D2)
**Related**: #415, #153 (importer), #241 (share-link importer), #378 (game systems), #424 (Assault), #438 (bake)
**Branch**: `feature/opr-3.6`. Model/effort: Opus 5.5 / medium. Depends on #424 for the weapon classification.

## Goal

`OprBookImporter` / `OprListImporter` / `ArmyForgeShareService` / `ArmyForgeBookService` handle 3.6.0 data
exactly: the 2026-10-29 scratch import of all 90 snapshots already succeeds on the 3.5 importer, so this item
is about correctness, not crashes.

1. Version: books stamp `OPR v3.6.0` (from `versionString`); the share-link import gate (today "3.5.x") becomes
   "3.6.x" and refuses 3.5 lists with an ASCII message naming both versions; `OprListImporter` version field.
2. Roster: skip 0-unit parent entries (`Wormhole Daemons`, `Rift Daemons` - `units` empty) in any index-driven
   fetch; the bundled file naming follows OPR's new names (`DaemonsofChange.fdgbook`, `SaurianStarhosts`,
   `AoF-DaemonsofWar`...); `BundledBookRulebook` / Forge book list keyed by book name - audit for hardcoded
   old names (plan appendix A, app survey).
3. Weapons: Assault weapons imported once with the marker rule (#424's model); `Overrun` weapons import as
   melee with Thrust; `attacksMultiplier`, `originalCount`, `label` fields unchanged - verify.
4. Spells: `effectSkirmish` (new field) ignored; `costs[].questCost` ignored; spell `threshold` read as now.
5. Upgrade options: `costs[]` per unit still present (the #219/#261 pricing path) - verify a 3.6 book's
   `--import-opr` prices match Army Forge for three units (record in Notes); the `costUnpriced` recovery
   (`--import-book`) still needed? (OPR omits `cost` on hidden-price options - check a 3.6 book).
6. `AsciiFold` on all new text (en-dashes in new rule texts, curly quotes).
7. `--import-army` end to end with a real 3.6 share link (ask Chris for one or build one on Army Forge).

"Done" = the above verified with tests in `OprBookImporterTests` / `OprListSelectionsTests` /
`ArmyForgeSerializationTests` using small fixtures (not whole books), and a 3.6 share link imports to a
playable `.fdgarmy`.

## Acceptance

Tests green; `--import-opr` on all 90 snapshots exit 0 with 0 warnings that are not expected (list expected
ones here); `--import-army` on a 3.6 link exit 0; on a 3.5 link exit 1 with the message.

## Code anchors (survey 2026-10-29)

- `FutureOfDarkGrimness/ArmyBuilding/OprBookImporter.cs` (`VersionString` ~line 108, `MapRule` ~333, spells
  section), `OprListImporter.cs` (version gate), `FdgRaylib/Import/ArmyForgeShareService.cs:16` (3.5.x gate
  comment), `ArmyForgeBookService.cs` (`GameSystemIds`), `BundledBookRulebook.cs`. See the appended survey rows.

### Survey rows (code survey 2026-10-29)

Findings that shape the item: the share-link gate checks each referenced book's LIVE `versionString` (the
list JSON carries no version) against `SupportedVersionPrefix = "3.5"`, so every share import is refused
until it is bumped; `Book.Version` is stamped but never read except in a `Program.cs` print; book names are
matched as plain strings in ~10 places (effect-set tables, overrides, rule-name fixes, Forge matching, the
refresh tools) so renamed books silently lose curation.

- `ArmyBuilding/OprListImporter.cs:40` `SupportedVersionPrefix` (docs :25-28, :37-39); :45-50 systems; :70-75
  system gate; :77-87 version gate; :229-239 `VersionMatches`; :552-565 `OprVersionMismatchException` (message:
  "Army book 'X' is at OPR version 3.6.0; this game supports 3.5.x. ..."). Surfaces: `ArmyForgeScreen.cs:388-392,
  :442-445`; `Program.cs:137-139`.
- `ArmyBuilding/OprBookImporter.cs:108` version stamp (read only at `Program.cs:133`); :355-361 `AmbiguousRuleNames`
  by book name; :468 "47 bundled books"; :314/:498 range; :433-447 section shapes; :465-481 costs; :808-847 bases;
  :864-872 DTO; :118 default effect sets (only if the faction is in `WeaponEffectAssigner`'s tables).
- `FdgRaylib/Import/ArmyForgeShareService.cs:16` (comment), :72-91 (flow), :150-167 `MatchBundledBook` by name;
  `ArmyForgeBookService.cs:28-39` (ids), :82 `RefreshCosts`, :132 shapes, :212/:263 lookups by name.
- `ArmyBuilding/WeaponEffectAssigner.cs:192-196` `NameOverrides` ("Saurian Starhost"), :200-249 GDF table (47
  names), :255-296 AoF table (40 names), comments :251/:344.
- Tests: `OprListImporterTests.cs:75-76` (3.5.x pass), `:309` (3.6.1 refused fixture), `:325-335`
  (`VersionMatches("3.6.0")` is False), `PerModelToughGrantTests.cs:174` ("3.5.0"), `OprBookImporterTests.cs:53,
  :61, :183`, `FdgRaylib.Tests/ArmyForgeBookServiceTests.cs:20`, `AnalyticsTests.cs:144` ("+opr.3.5.1").

## Notes

- 2026-10-30 (#437 run, Opus 5.5). Commits: engine `080a5249` + super `c67f5fa` (slice 1: gate, list pricing, fold,
  parent entries); engine `57846d95` + super `7b62dd2` (slice 2: `OprBookNames`). Findings, item by item:
  1. **Version.** `OprBookImporter` already stamped `OPR v{versionString}`; the all-90 scratch import stamps
     `OPR v3.6.0` on all 88 books. `SupportedVersionPrefix` 3.5 -> 3.6. The mismatch message names both versions and
     now picks its remedy by direction (D3): "Army book 'X' is at OPR version 3.5.3; this game supports 3.6.x. That book
     has not been updated to this OPR version yet, so lists using it cannot be imported." / "... The game's data needs
     an update ..." for a newer book. Tests re-pinned: `OprListImporterTests` (books 3.6.0/3.6.1, VersionMatches,
     refusal of a 3.5.3 and a 3.7.0 book), `PerModelToughGrantTests`, `AssaultImportCompileTests` (stub 3.6.0),
     `OprBookImporterTests` + `ArmyForgeBookServiceTests` fixtures (cosmetic). `AnalyticsTests` "+opr.3.5.1" is the
     BUILD's ProductVersion (`RULES_VERSION` from `build-dist.sh`), not importer-derived - left for #444.
  2. **Roster.** Index rows with `unitCount: 0` (Wormhole Daemons, Rift Daemons) are dropped from
     `ArmyForgeBookService.FetchBookIndexAsync`; `OprBookImporter.Import` warns on a book with no units and no spells
     and `--import-opr` writes nothing for it (exit 0) (D6). Renamed books: one table, `OprBookNames` (D1), through
     which every name-keyed lookup now goes: `WeaponEffectAssigner.FactionDefaults` / `NameOverrides`,
     `OprBookImporter.Disambiguate` (`AmbiguousRuleNames`), `OprListImporter` (book name for disambiguation),
     `ArmyForgeShareService.MatchBundledBook`, `BundledBookRulebook` (legacy-list backfill), `--retrofit-editable`,
     `--import-book` / `--import-section-shapes` (index lookup), `--import-spells` (snapshot file). The GDF effect-set
     rows were renamed in place ("Daemons of X", "Saurian Starhosts"), the AoF "Rift Daemons of X" rows deleted (D2).
     The ArmyForgeScreen book list reads whatever is bundled (no name matching), the Random Army catalog / FdgLab
     pool / scenarios reference army FILE paths (#440's regen). Bundled-file naming is the bake script's (#438): the
     compact snapshot name already gives `DaemonsofChange.fdgbook` / `AoF-DaemonsofChange.fdgbook` /
     `SaurianStarhosts.fdgbook`. Both systems now have a "Daemons of X"; every comparison is system-gated (pinned).
  3. **Weapons.** No mapping change needed. All-90 scratch import: 3503 Assault weapon entries, every one ranged and
     ONE entry carrying `Assault` (0 split copies); 153 Overrun entries, all range 0 with exactly `Thrust` (= the
     snapshot count). `attacks` is already the multiplied value: the label's A-number equals `attacks` on all 15,583
     labelled 3.6.0 weapons (3849 with attacksMultiplier > 1), "2x Shard Cannon (A6)" is count 2 / attacks 6 /
     multiplier 2 per cannon, and shared upgrade packages never mix multipliers (0 of 271 gains). So the list
     importer's "attacksMultiplier (unsupported)" warning was wrong and is gone (D5); `originalCount` / `label`
     stay unread.
  4. **Spells.** `effectSkirmish`, `costFormula`, `generation`, `costs[].questCost` / `exactCost` are not in the DTOs and
     import silently (pinned on a trimmed High Elf Fleets excerpt); `threshold` is an int on all 528 spells, read as now.
  5. **Pricing.** Every one of the 16,315 (unit, option) pairs in the 90 snapshots has a per-unit `costs[]` entry, so
     a 3.6 import has 0 `costUnpriced` options and the `--import-book` recovery pass is not needed for 3.6 data (D7).
     Three High Elf Fleets units, `--import-opr` output vs the snapshot's `costs[]` vs the live Army Forge list's own
     selection prices: Anti-Gravity Tank (base 535, 8 options, 0 mismatches; Heavy Focal Cannon 80 / Hologram Field
     30 = Army Forge), Stingers (170, 7 options, 0; Ancient Sword 5, Sgt. swap 0 = AF), Shifters (165, 5 options, 0;
     Heavy Phaser Blaster 20 = AF). Found and fixed on the way: the SHARE-LIST importer still read the flat `cost`
     before the per-unit `costs[]` (the #261 bug, fixed in the book importer only) - the tank's Hologram Field is flat
     65 vs 30 for that tank (D4). With the fix the live list attributes exactly its `listPoints` (2465).
  6. **AsciiFold.** The only non-ASCII 3.6.0 characters are curly quotes, ellipsis, em dash, i-macron (all folded
     before) and U+200D ZERO WIDTH JOINER (seven AoF books' lore), which survived as '?': zero-width characters now fold
     to nothing (D8). All-90 import: 0 non-ASCII characters in the 88 books, no fold warnings.
  7. **Share link, live.** `--import-army UGDhJMMH0QBP` (Chris's July High Elf Fleets list; Army Forge serves it
     resolved against the live 3.6.0 book - Assault pistols, Overrun A4, Fracture) passes the gate, exit 0, 9 units,
     2465 pts, and plays a headless game to `Game result:` (exit 0). Against today's bundled 3.5 book it reports
     Anti-Cover / Armor(3) / Assault / Fracture as not enforced and base-cost drift (expected until #438). Against a
     copy of the app with the scratch-imported 3.6 HEF book in place: no inert rules, points check 2395 vs 2465 - the
     70 is (a) #262 (stale selections whose replace target is absent - Retributors 65, Shifters 20; ruled an upstream
     Army Forge bug) and (b) OPR patched the HEF book on 2026-10-30, after the snapshot (Venator Plane 280 -> 265,
     our side +15). A 3.5 link cannot be produced any more (every live book reports 3.6.0); the refusal is pinned by
     fixture. The live check counts as done (D10); a list built fresh in 3.6 would add only selection coverage.
  - **Expected warning set** for `--import-opr` over the 90 snapshots (all-90 scratch import, Dead 0): 113 "no base
    declared ... estimated ..." (vehicles/monsters OPR exports with base "none"; #225 estimator, by design), 11 "Spell
    ... skipped: unrecognized effect text" (#439's parser), and for the 2 parent entries the "has no units and no
    spells" warning plus the "Skipped ... nothing written" line. Nothing else (was +7 U+200D fold warnings).
  - **Snapshot staleness** (live index vs `_index.json`, 2026-10-30): High Elf Fleets, Saurian Starhosts and AoF Orcs
    were edited after the 2026-10-29 fetch (every GDF book was also re-published at 23:48-23:52 that night, editedAt
    unchanged). Handed to #438.
  - Gates: engine 4366 -> 4389 passed / 1 skipped; app 3573 -> 3578; build 0 errors; `--validate-rules` OK 417;
    shipped `--rule-coverage` Dead 475 (unchanged); all-90 scratch Dead 0; headless smoke exit 0.

- 2026-10-30 (from #424): the Assault definition ships in `GdfRuleSupplement.json` (weapon scope, `enableMeleeUse` +
  `enableRangedUse` at `Lifecycle_OnCapabilityQuery`), so a scratch import of a 3.6 snapshot now resolves every Assault
  reference with NO importer change - both importers already map it to `SpecialRuleEntry_Core("Assault")` and keep one
  entry per weapon (pinned in `Tests/AssaultImportCompileTests.cs`). Two things remain yours: (1) `OprListImporter`
  refuses a 3.6.0 book (`OprVersionMismatchException`, "this game supports 3.5.x") - the share-link gate; the #424 pin
  stubs a 3.5.3 book to get past it; (2) nothing in #424 touched the version stamp or the renamed/parent books.
- 2026-10-30 (from #442): nothing for the importer from the AI side; the AI keys on rule effects, not names. If the
  importer starts emitting Assault weapons (#424 A1), see the AI notes in `WorkItems/424-assault-weapons.md`.
- 2026-10-29: filed with the plan.

## Decisions

- **D1 (OWNER REVIEW) - renamed books: one rename table, keyed by name.** `FDG.ArmyBuilding.OprBookNames` maps each
  pre-3.6 name to its successor per game system (GDF Wormhole Daemons of X -> Daemons of X, Saurian Starhost ->
  Saurian Starhosts; AoF Rift Daemons of X -> Daemons of X); every name-keyed lookup compares `Current` names
  (system-gated: both systems now have "Daemons of X"), and the curated tables are keyed by current names only.
  Alternatives: (a) key by Army Forge uid (stable across these renames) - rejected for now: `BookFile` and
  `ArmyListFile` carry no uid, a saved army knows only its Faction string, so the legacy-list backfill would still
  need the table, and adding a uid field changes every `.fdgbook`; it remains a good addition for #438's refresh
  tools if wanted. (b) duplicate rows under both names in every table (the #435 stopgap) - rejected: curation drifts
  per copy and every future rename touches ~10 sites. The table stays after #438 (3.5 armies still name old books).
- **D2 (OWNER REVIEW) - the AoF "Rift Daemons of X" effect-set rows were deleted now, not left for #438.** The brief
  said keep them until the re-bake; with D1 they are unreachable (a "Rift Daemons of X" faction canonicalises to
  "Daemons of X" before the lookup), so keeping them would be dead rows. The GDF "Wormhole Daemons of X" and "Saurian
  Starhost" rows were renamed in place, so the 3.6 GDF imports now get default effect sets (before: null). Pinned:
  both names give the same sets, per system. Nothing is left for #438 to drop here.
- **D3 - the version-mismatch message picks its remedy by direction.** The gate reads the book's LIVE version, so an
  older book (3.5 under a 3.6 game) is OPR's lag - "That book has not been updated to this OPR version yet, so lists
  using it cannot be imported." - while a newer one keeps "The game's data needs an update ...". Both name the found
  and supported versions; ASCII. Alternative (one generic sentence) told a 3.5 user to wait for a game update.
- **D4 - share-list option prices: the unit's `costs[]` entry wins over the flat `cost`.** The book importer has done
  this since #261; the list importer still read flat-first, mispricing 3.6 lists (live: +10 on one tank). Same order
  and fallback as `OprBookImporter.MapOption`.
- **D5 - `attacksMultiplier` is informational.** Verified corpus-wide that `attacks` already includes it, so the list
  importer's warning was wrong; removed, field dropped from the DTO. `originalCount` / `label` likewise unread.
- **D6 - parent entries.** Detected as "no units AND no spells" (`OprBookImporter.IsParentEntry`; spell-only test
  fixtures are not parents). Import warns; `--import-opr` writes nothing and exits 0 (a bake loop passes over it
  without a special case and no empty faction can reach the Forge list); the official-index map drops
  `unitCount: 0` rows. Alternative (exit 1) would mark every bake loop failed.
- **D7 - `costUnpriced` recovery is not needed for 3.6 data.** 0 of 16,315 (unit, option) pairs lack a per-unit price.
  `--import-book` is kept (harmless, still correct) but #438 should skip the pass; retiring it is a later cleanup.
- **D8 - zero-width characters (U+200B/C/D, U+2060, U+FEFF) fold to nothing** in `AsciiFold` - invisible by
  definition, so dropping them is lossless; the alternative (keep + warn) renders '?' in lore text.
- **D9 - Assault needs no importer change** (#424's model): one entry per weapon carrying the rule, verified over the
  whole corpus and pinned by both importers' 3.6 fixtures.
- **D10 - the live share-link check used Chris's existing list** (`UGDhJMMH0QBP`, built in 3.5, served by Army Forge
  resolved against the live 3.6.0 book). The served JSON is what the importer consumes and it carries 3.6 data
  (Assault, Overrun, Fracture, 3.6 prices), so the check counts; a freshly built 3.6 list would add only
  3.6-authored selections. Not owed.

## Outcome

Done 2026-10-30. The importers handle OPR 3.6.0 data: the share-link gate is 3.6.x (3.5 refused with a message naming
both versions), books stamp `OPR v3.6.0`, Assault weapons import as one entry with the rule, Overrun as melee with
Thrust, option prices read the per-unit `costs[]` in both importers, the new 3.6 fields are ignored silently, all
imported text is ASCII, parent entries are skipped, and a single rename table (`OprBookNames`) keeps renamed books'
curation (effect sets, overrides, rule-name fixes, share-link book match, legacy-list backfill, refresh tools). A
live 3.6 share link imports to a playable army. Tests: `OprListImporterTests`, `OprBookImporterTests` (trimmed 3.6
fixtures), `OprBookNamesTests`, `WeaponEffectAssignerTests`, `BundledBookRulebookTests`, `ArmyForgeBookServiceTests`.
Owner review: D1, D2. Hand-offs: #438 (bake), #440 (legacy armies / derived data), #441 (UI). Not done here, by
design: re-baking the bundled books (#438), Voidspace Stalkers' default effect sets (#438 item 3), the spell grammar
(#439), the build's `+opr.3.5.1` version string (#444).

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
