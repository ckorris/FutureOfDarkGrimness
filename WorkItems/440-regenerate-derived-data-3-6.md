# 440 — Regenerate derived data; prove legacy 3.5 `.fdgarmy` files still load

**Status**: todo
**Related**: #415, #438 (books first), #395 (Random Army path bug - separate), #167 (scenarios), #357 (editable retrofit), #436 (deleted names degrade gracefully)
**Branch**: `feature/opr-3.6`. Model/effort: Sonnet 5.5 / medium. Open question: plan Q5 (legacy notice).

## Goal

Everything derived from the books is regenerated from the 3.6 books, and the one backward-compatibility
promise - old `.fdgarmy` files with embedded rules still load and play - is proven by tests.

1. Random Army catalog: the `armies/` folder (and whatever `build-dist.sh` ships beside the executable) is
   regenerated from 3.6 books with the same generator/process the 3.5 set used (find it: app survey, plan
   appendix A); the 3.5 files move to `FdgRaylib.Tests/Fixtures/legacy-3.5/` (a handful, not all) as
   fixtures.
2. Scenarios: `Scenarios/*.json` reference unit/book names - re-point renamed units (Prime Brothers, Disciples,
   Infected Colonies, Rebel Guerrillas restructures) and re-run `--make-scenario` for each; `Scenarios/README.md`.
3. The CLI's built-in two-unit EOF test army (5x Warriors with rifles + 3x Heavy Gunners): keep, but make sure
   its weapons/rules are 3.6-legal (plain rifles are fine).
4. Test fixtures that embed book data (`FdgRaylib.Tests`, engine tests referencing "Battle Brothers" etc.):
   regenerate or re-point; delete fixtures for units that no longer exist.
5. Legacy: tests that load a 3.5 `.fdgarmy` (embedded definitions referencing 3.5 names, including a deleted
   catalog name) into a `GameDataStore`, run a headless game to completion with seeded dice, and assert the
   #168 diagnostic lists the unresolved names. Lobby notice per Q5 default: amber one-liner "Army '<name>' was
   built for OPR v3.5.x; it plays with 3.6 rules" when the army's book version is not 3.6 - not blocking.
6. `--retrofit-editable` on the regenerated `armies/` so the Forge can reopen them.

"Done" = the five points verified and the legacy test in the suite.

## Acceptance

Suite green; every scenario compiles and runs headless; Random Army works in a dist build (`scripts/
build-dist.sh` local run, pick Random Army in the lobby - Chris hand-verify) for both systems.

## Code anchors (survey 2026-10-29)

- `armies/` (repo root), `scripts/build-dist.sh` (what it copies), `Scenarios/`, `FdgRaylib.Tests/` fixtures,
  `Program.cs --retrofit-editable` (~line 411), `#168` diagnostics surface. See the appended survey rows below.

### Survey rows (code survey 2026-10-29)

Findings that shape the item: `armies/` is 33 HAND-BUILT files (28 GDF: 1k x4, 2k x9, 3k x11, 4k x3, "Wormhole
Demons of Change 1k"; 4 AoF + `.gitkeep`) with no generator - built in the Forge / share-imported, then patched
by `--retrofit-editable` / `--retrofit-effects` / `--retrofit-bases`. Each embeds a full book snapshot
(`OPR v3.5.3`), 2-24 rule definitions and 6 spells, and the army's own definitions WIN over the bundled book
(`SaveLoad/ArmyListRuleResolution.cs:79-110`), so re-baking books does not touch them. They must be rebuilt
from 3.6 books (share links or Forge) - keep a few originals as the legacy fixtures.

- Catalog: `FdgRaylib/ArmyCatalog.cs:36, :72-76`, `ArmyPaths.cs:20, :31`, `Rendering/LobbyScreen.cs:94, :376-377,
  :544-551`, `Rendering/BotArmyPicker.cs:52, :77-81` (:20 "five armies at exactly 1000 points"). Shipped by
  `scripts/build-dist.sh:113-118` (readme text :137, :177, :206). Forge reopen uses the embedded book
  (`ArmyForgeScreen.cs:651-655`, `EditableSessionDrift.cs:76-80`).
- Copies / stale: `FdgLab/armies/*.fdgarmy` (8, v3.5.3) + `FdgLab/armies/pool.json` (44 paths stale since the
  GDF/AoF split); `FdgRaylib/GoodGuys.fdgarmy`; `FutureOfDarkGrimness/armies/example-*.fdgarmy` (hand-authored,
  no book; `ExampleArmyFileTests.cs:23, :56`); root `*.fdgsave` files embed armies + terrain names.
- Scenarios: 28 JSON; 26 use the 30 hand-authored `Scenarios/armies/*.fdgarmy` (insulated). Already broken:
  `Scenarios/crowded-2v2-3k.json:10-13` (paths moved to `armies/GDF/`), `Scenarios/limited-weapons-test.json:8`
  (missing file). Book-derived: `Scenarios/armies/408-VersatileAndWeaponTeam.fdgarmy` (Prime Brothers; embeds
  Versatile Attack - dropped in 3.6), `VinciGateDemo.fdgarmy` (no gameSystem); `377-Verifiers/Hostiles.fdgarmy`
  (hand-copied spells granting phrase rules). `Scenarios/*.fdgsave` embed compiled armies.
  `SaveLoad/ScenarioCompiler.cs:510-537` parses terrain by name; `Scenarios/README.md:38-39`.
- Tests: `FdgRaylib.Tests/ArmyCatalogTests.cs:108-150` (walks the real folder), `CombatCalculatorScreenTests.cs:423-432`,
  `GameAnalyticsSessionTests.cs:33, :57`, `WeaponEffectAssignerTests.cs:58-60`, the ~45 `*ShippedDataTests.cs`
  (listed in #438), `InstinctiveShippedDataTests.cs:16`.

## Notes

- 2026-10-30 (from #438): (1) the 3.5 books are off-repo at `~/Projects/GDF Armies/fdgbooks-3.5-final/` (87
  `.fdgbook` + `SOURCE-COMMIT.txt`, the superproject commit they were copied from) for the legacy tests. (2) Section Ids
  in the 3.6 books are OPR's stable section `uid` (#438 D2), so EVERY section Id differs from the 3.5 books (unit ids
  mostly survive: 1671 of the 1828 3.6 units in renamed-or-kept books match a 3.5 id); anything derived that stores Forge picks (`SectionId` / `OptionId`) against a bundled book -
  scenarios, fixtures, the Random Army catalog - must be regenerated, not patched. Old `.fdgarmy` files embed their own
  book, so they keep loading. (3) Keep catalog "Very Fast" until the built-in EOF army (`FdgRaylib/Cli/ArmyLoader.cs`)
  is rebuilt - it is that rule's last user (#438 D7). (4) `ItemEntry` gained an optional `weapons` list (a 3.6 mount /
  crew item's own attack profiles; null and omitted otherwise) - old files parse unchanged (#438 D4). (5) `ListValidator`
  no longer knows 3.5 "Unique" (#438 D8): a reopened 3.5 Forge army does not get its duplicate-Unique Error any more -
  include that in the legacy-load check's expectations.
- 2026-10-30 (from #437): legacy `.fdgarmy` files whose Faction is a 3.5 book name ("Wormhole Daemons of X", "Rift
  Daemons of X", "Saurian Starhost") keep finding the re-baked "Daemons of X" / "Saurian Starhosts" books - the
  rulebook backfill (`BundledBookRulebook`), `--retrofit-editable`, the share-link book match and the effect-set
  defaults all compare through `FDG.ArmyBuilding.OprBookNames` (system-gated; both systems now have "Daemons of X").
  Worth one legacy-load check per renamed family in your regression set. The derived data names old books only as
  FILE paths (`FdgLab/armies/pool.json`: "Wormhole Demons of Change 1k.fdgarmy", "3k - Saurian Starhost.fdgarmy";
  `Scenarios/crowded-2v2-3k.json`) - renaming those files is your call; nothing resolves them by book name.

- 2026-10-30 (from #436): (1) **Legacy Strafing** (#436 D2, owner review): a 3.5 `.fdgarmy` naming Strafing on a range-0
  bomb now loads with the #168 drop, and the bomb - no longer strafe-only - counts as a melee weapon. Decide whether
  the legacy path wants a dropped-name -> 3.6-successor table (Strafing -> Crossing Strike, Courage Aura -> Bravery
  Aura, Piercing Assault -> Piercing Charge, Piercing Hunter -> Piercing Marksman, Darkborn (Offensive) / (Defensive)
  -> Darkborn); the successors are supplement rules an old file does not embed, so it needs the supplement at load. (2)
  The in-memory pattern for "a 3.5 list naming deleted catalog names" is `Dropped35CatalogLegacyDataTests` (engine)
  beside `CounterLegacyDataTests` - the fixture-file version of point 5 can reuse its name list (Courage Aura, Crack,
  Strafing, Harassing / Guerrilla Boost (+ Auras), Resistance (+ Aura), Piercing Assault (+ Aura), Piercing Hunter (+
  Aura), Darkborn (Offensive) / (Defensive), Storm of Change / Lust / Plague / War). (3) The built-in EOF test army no
  longer carries "Strafing" (#436); it still has "Very Fast", which no 3.6 book prints (point 3).
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
