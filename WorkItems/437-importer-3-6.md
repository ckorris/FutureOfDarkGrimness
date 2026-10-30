# 437 — Importer 3.6: snapshot shape, version stamp, renamed/parent books, share-link gate, Assault class

**Status**: todo
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
