# 438 — Re-bake all 88 books from the 3.6.0 snapshots

**Status**: done 2026-10-30 (owner review: D1, D2, D3, D4, D5)
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

- 2026-10-30 (done): the 88 books are the OPR 3.6.0 books, baked by `scripts/bake-books.sh` from the 2026-10-30
  re-fetch. Commits: engine `70fa2c08` (effect-set keywords, Crossing Strike bombs, Voidspace Stalkers row, section
  uid), `dd6f5661` (Forge compile: pass-through reservation, item-bundled weapons), `9b521899` (Unique arm),
  `2ec1f520` (StormStage family); superproject `2a3335b` (bump), `396d72d` (bake script), `31f8632` (rename-only
  git mv), `e196560` (re-baked books + test re-pins + allowlists), `75cc6e8` (Unique supplement entry + bump),
  `98bdde4` (bump). Each pair verified green before committing; the rename-only commit was built in the index and
  verified in a throwaway worktree (app 3578 green with the 3.5 content under the new names).
  - **Bake command**: `scripts/bake-books.sh [gdfSnapshotDir] [aofSnapshotDir] [outDir]` (defaults: the two
    `opr-json-snapshots-3.6.0-refetch-2026-10-30` folders and `FdgRaylib/Assets/Books`; `OFFLINE=1` skips the one
    live-API pass). Usage is documented at the top of the script. A second bake into a scratch dir is byte-identical
    to the shipped books (88/88 `cmp` clean) - the bake is reproducible from the snapshots.
  - **Pass-by-pass summary lines** (shipped bake):
    1. `--import-opr` x 90: "baked 88 book(s)"; the two parent entries print "Skipped 'Wormhole Daemons' / 'Rift
       Daemons' OPR v3.6.0: an Army Forge parent entry ... nothing written". Warnings exactly as #437 predicted: 113
       "no base declared ... estimated", 11 "Spell ... skipped" (#439, list below), nothing else.
    2. `--import-book`: SKIPPED (#437 D7 - every 3.6.0 (unit, option) pair has a per-unit price).
    3. `--import-section-shapes` (live API): "Done: 88/88 book(s) checked, 0 section(s) stamped per-model." (every
       section matched the live book; the importer already classifies the 16 per-model sections).
    4. `--retrofit-effects`: "Retrofit complete: 0/88 file(s) patched." (the import stamps the defaults; every book
       has both default sets, Voidspace Stalkers included).
    5. `--retrofit-bases`: "Base retrofit complete: 0/88 file(s) patched, 0 base(s) estimated" - confirmed no-op.
    6. `--import-spells`: "Spell re-stamp complete: 88 book(s) checked, 517 spell(s) stamped, 0 book(s) changed."
    7. `--rule-coverage FdgRaylib/Assets/Books`: "Total references: 30862 / Dead: 0" (before: Dead 475).
  - **The 11 skipped spells** (#439): GDF Blessed Sisters "Burn the Heretic"; GDF Change Disciples, Daemons of
    Change, Titan Lords Change Disciples and AoF Change Disciples, Daemons of Change, Giant Tribes Change Disciples
    "Shifting Form" (x6); GDF Machine Cults "Corrode Weapons"; AoF Dragon Empire "Ancestral Guidance"; AoF Shadow
    Stalkers "Combat Possession"; AoF Sky-City Dwarves "Aether-Cloud Rune".
  - **Post-bake checks** (hand-offs): no `.fdgbook` contains `allModelsHaveThisRule`, and HumanInquisition's
    Inquisitorial Agent carries `everyModelHasThisRule` (#428); `grep -lE "each moving model rolls|each of its models
    rolls"` is empty (#420); Spell Drain is `moraleTestThen -> onFailure consumeToken(SpellTokens, 2)`, the shape the
    Tactician reads (#442); every one of the 1706 embedded supplement-backed definitions equals its supplement
    definition (GDF, or GDF + AoF for AoF books; 15 more are importer-synthesized spell effects), which covers the
    #435 AoF list (Banner, Great Sergeant / Banner, Ossified, Warden, Wave-Step, Ethereal, Grounded Speed, the five new
    AoF names, Piercing Charge Buff, and the GDF Difficult Terrain Debuff / Fortified Growth / Melee Slayer copies),
    #434 (the reposition family's `move: true`, Regenerative Strength's 3.6 consumer, Ravage Aura, the bare Dangerous
    Terrain Debuff name) and #422 (gated Heavy Impact, Counter-only Counter in Melee); Impact per-model sites
    re-pinned (#447, below); `LC_ALL=C grep -lP '[^\x00-\x7F]'` over the books is empty; 88 books, every `version`
    "OPR v3.6.0".
  - **Forge compile of every unit**: `--book-to-army` over all 88 books - 88/88 succeed, 1850 units, no failures.
  - **Smokes** (headless, `--army`, exit 0, "Game result:"): GDF Daemons of War (Blood Warriors first; Parry(1) on
    the Hand Weapons in play), GDF Battle Brothers (Assault Brothers first; Heavy Pistol Assault), AoF High Elves
    (Warriors first; `--trace-rules` shows "Warriors's Parry(1) at Melee_OnHitsTaken/Subject: fired -> ParryDice"),
    AoF Daemons of Lust with "Enhanced Lustbound" hand-added to the Avatar of Lust (the #433 recipe; loads and plays,
    though movement modifiers are not narrated by `--trace-rules`, Lustbound's own included). Default
    `printf "2\n2\n" | --headless` smoke exit 0 after every engine slice.
  - **Shipped-data re-pins** (every new number checked against the 2026-10-30 snapshots with a census script, not
    copied from the failure): Armor 11 -> 233 option-item grants across 45 GDF books (ratings 2 x207, 3 x12, 4 x11,
    5 x3; renamed `EveryReference_IsAnItemGrant_The36Census`); Extended Buff Range 9 -> 14 (HDF 9 + Voidspace Stalkers'
    5 "Radio Operator"); Hazardous 15 -> 30 (Ratmen Clans 14 + Goblin Reclaimers 16); Inquisitorial Agent 20 -> 21;
    Unpredictable Marks 5 -> 9 (Fighter 7 = Alien Hives 4 + Voidspace Stalkers 3; Shooter 2); per-model Tough 16 -> 18
    Weapon Teams; per-model sections 22 -> 16 (all "Any model may take ... attachment", 15 GDF books; named cases
    re-pointed, the 3.5 Hive Warriors / Robot Snakes sections are gone); per-model Impact items = the #447 four (Orc
    Hover Bikers, AoF Ratmen Storm Ogres -> grants; the two Trample swaps unit-wide), plus a prerequisite pick in the
    test (the Hover Bikers' "Replace one Hand Weapon" needs the all-swap that hands out the Hand Weapon); Regenerative
    Strength carriers Engine of Suffering + Carnivo-Rex (3.5 Psycho-Rex), wound-ignore source read from the same grant
    (Spell Resistance), marker entry picked by hook; Takedown Strike 5 / Shot 3 (coreNumeric "Takedown X(1)", Elven
    Jesters added), melee-Takedown invariant -> exactly HEF Stingers' Stinger Dagger (D5); Sergeant / Split read the
    Daemons of X files, Sergeant's embedded copy is the 3.6 AP(+1) body; Terrain Debuff: no Immediate variant left,
    11 deferred Dangerous sites (Lust / War Disciples included); Forge cross-section census 54 -> 47 (25 books), mutual
    pairs checked from the naked unit's price (D12); Forge screen: War Errant Mini-Titan -> HDF Combat Walker (the
    3.6 holder of the #323 shape, 350 pts), Tactical Grunts re-pinned to Long Rifles / Carbine / 120 pts, Hive Warriors
    -> Veteran Battle Brothers' attachment section; Strafing test reads Crossing Strike only.
  - **Retired** (subject gone from 3.6): `ArmyForgeScreenTests.Guardians_ReplaceAllPistolsAndBashes_TakesTheBashesToo`
    (no "-es" plural target in any 3.6 book; the plural rule stays pinned engine-side), `BookRuleScopeTests.
    SniperDrones_DroneController_UpgradesOnlyThePulseRifle` (no targeted weapon-scoped wargear left; owner ruling stays
    pinned by `WeaponScopedWargearRoutingTests`), engine `StormRuleIntegrationTests` (with the stage), the three 3.5
    `ListValidatorTests` Unique cases.
  - **Allowlists removed**: `BookRuleCensusTests` all 20 (#422 Counter-Attack + Aura, #436's 18); `BookSpellCoverage
    Tests` and `BookSpellCastProbeTests` 6 each (Counter-Attack, Crack, Harassing Boost, Piercing Assault, Piercing
    Hunter, Resistance); `BookRuleFireLintTests` "Counter in Melee" and "Unique"; `RuleSupplementLintTests` "Unique".
    "Ethereal" stays (#435).
  - **Gates**: engine 4389 -> 4417 passed / 1 skipped (+32 effect-keyword cases, +3 importer, +2 compiler; -5 Storm,
    -3 Unique); app 3578 -> 4033 (the per-book / per-embedded-rule fixtures grow with the 3.6 corpus; 2 retired);
    `dotnet build` 0 errors; `--validate-rules` both files "OK: 417" -> "OK: 416 definitions, no problems."; shipped
    Dead 475 -> 0.
  - Not run here: the Strategist-vs-Tactician / #442 benches (the orchestrator runs them), #424's GUI hand-verify
    (Chris: a Battle Brothers unit with Heavy Pistols shoots at 9"/12" and then charges and strikes with the same
    pistols in one round).

- 2026-10-30 (from #437): the importer is ready; what changes for the bake: (1) **Parent entries** - `--import-opr` on
  "Wormhole Daemons" / "Rift Daemons" prints "Skipped ... an Army Forge parent entry ... nothing written" and exits 0,
  so the bake loop needs no skip of its own (keep one anyway if you like; `_index.json` marks them `unitCount: 0`).
  (2) **Renames** - `FDG.ArmyBuilding.OprBookNames` is the one rename table; the GDF effect-set rows are now keyed
  "Daemons of Change / Lust / Plague / War" and "Saurian Starhosts" (carried over from the 3.5 rows), and #437 already
  DELETED the AoF "Rift Daemons of X" rows (#437 D2), so there is nothing to drop and the renamed books get their
  defaults at import. Only **Voidspace Stalkers** still has no row (your item 3). Keep `OprBookNames` after the bake -
  3.5 army files name the old books. File names: compact snapshot names give `DaemonsofChange.fdgbook`,
  `SaurianStarhosts.fdgbook`, `AoF-DaemonsofChange.fdgbook`; `git mv` from `WormholeDaemonsofX` / `SaurianStarhost` /
  `AoF-RiftDaemonsofX`. (3) **Skip `--import-book`** - every (unit, option) pair in the 3.6.0 snapshots has a per-unit
  `costs[]` price, so a 3.6 import has 0 `costUnpriced` options (#437 D7). (4) **Stale snapshots** - High Elf Fleets
  (2026-10-30 14:47; Venator Plane 280 -> 265 at least), Saurian Starhosts (2026-10-29 22:47) and AoF Orcs
  (2026-10-30 14:57) were edited after the 2026-10-29 fetch (`editedAt`, live index vs `_index.json`); re-fetch those
  (or all) before baking, or the shipped books start a day stale. (5) **Expected import warnings** (all-90, Dead 0):
  113 "no base declared ... estimated" and 11 "Spell ... skipped" (#439); nothing else - any other line is new.
  (6) `BundledBookRulebookTests.ARenamedBook_IsFoundUnderEitherName` / `TheShared36DaemonsName_StillSplitsByGameSystem`
  are written to pass before AND after the rename; the #354 `ReportedCases` row still says "Saurian Starhost" on
  purpose (a 3.5 army's faction).

- 2026-10-30 (from #436): the re-bake must clear / settle: (1) **Unique** - delete the GDF-file "Unique", the `Unique` arm
  of `ListValidator.Validate`'s `uniqueRule` ternary, the `["Unique"]` lint exemptions (`RuleSupplementLintTests.cs`,
  `BookRuleFireLintTests.cs`), the UniqueBook fixtures in `ListValidatorTests.cs`,
  `LaunchGateTests.ForgeArmyWithCatalogError_IsAProblem_AttributedToThePlayer` and the `Core("Unique")` in
  `DefaultBaseEstimatorTests` (#430's list; 3.6 books carry the catalog "Unique Hero"). (2) **Allowlists #436 added**
  (each fails as stale once the bundled books stop naming it): `BookRuleCensusTests` - Courage Aura, Crack, Strafing,
  Harassing Boost Aura, Guerrilla Boost Aura, Resistance (+ Aura), Piercing Assault (+ Aura), Piercing Hunter Aura,
  Darkborn (Offensive) / (Defensive), Storm of Change / Lust / Plague / War (shipped `--rule-coverage` Dead 475 until
  then: these 18 names + #422's two); `BookSpellCoverageTests` / `BookSpellCastProbeTests` - Crack, Harassing Boost,
  Piercing Assault, Piercing Hunter, Resistance (granted by bundled 3.5 spells: Brutal Massacre, Magic Arrows, Fire
  Ball, Dark Isle Spirit, Rune of Piercing, Battle Guts, ...; #439's re-import). (3) `StrafingShippedDataTests` now
  reads "Strafing" OR "Crossing Strike" for its carriers-can-fly check; after the bake it should find Crossing Strike
  carriers only (drop the Strafing arm then). (4) Candidates to delete once nothing bundled uses them: StormStage +
  `Effect.StormOfHits` / `RuleOperation.InvokeStorm` + ChooseActionStage's Storm routing (#436 D7; no 3.6 rule uses
  them), and the catalog rules no 3.6 book reaches - "Very Fast", "AP(+1) in melee", "AP(1) when shooting", "Bane when
  attacking", "Shred when attacking", "+6" range when shooting" (#436 D10: re-run #436's `usage2.py`-style check over
  the shipped 3.6 books). Keep the `dangerousTerrainTest`, `attackedFromOverInches` and `reduceImpactDicePerModel`
  kinds - old army files embed them.
- 2026-10-30 (from #423): Parry resolves against the catalog, so the re-bake embeds nothing new for it - the 3.6.0
  books reference it as a numeric core rule ("Parry(1)" / "Parry(3)" on Hand Weapons) and a scratch import of all 90
  snapshots against the current supplements is now Dead 0 (was 264 = Parry alone). After your re-bake the shipped
  `--rule-coverage` should drop from Dead 36 by the Counter-Attack family only. Smoke armies that exercise Parry at base
  size: AoF High Elves (Warriors), Dwarves (Mountain Warriors), Ogres (Devourers, Parry(3)), GDF Daemons of War (Blood
  Warriors) - reorder the book so those units are among the first four `--book-to-army` compiles.
- 2026-10-30 (from #433): the Enhanced family is data in both supplements (112 definitions: 64 `Enhanced X`, 42
  `Enhanced X Aura`, 6 `Enhanced X Buff`), so the re-bake needs nothing new - a scratch import of all 90 3.6.0
  snapshots against the finished supplements leaves Enhanced 0 dead (Dead 264 = Parry alone, #423); the shipped books
  stay at Dead 36 until you re-bake. `--validate-rules` on both files: 447 definitions, no problems. The app test
  `Enhanced36ShippedDataTests.TheFamily_IsExactlyThe36Inventory` pins the 112 names, so a book that names a new
  Enhanced X fails loudly rather than baking dead. Book shape worth knowing: in every 3.6.0 book an Enhanced name is an
  UPGRADE option (or a spell grant), never a unit's base rule, so `--book-to-army` base-size compiles never field one;
  the #433 smokes hand-added `Enhanced Lustbound` / `Enhanced Changebound` (+ Aura) and `Enhanced Cursed Undead` (+ Buff)
  to compiled armies (`scratchpad/433/*.fdgarmy`). Post-bake pass: the `.fdgbook` embeds the Enhanced definitions
  alongside their bases (seen in the import logs: "embedded 8 rule definitions (Lustbound, ..., Enhanced Lustbound)").
- 2026-10-30 (from #424): the re-bake needs nothing new for Assault - the supplement definition is embedded by
  `BookRuleSupplement.Apply` like any other, and `WeaponEffectAssigner` keeps ONE key per weapon (an Assault pistol's
  swing uses the army's melee default through `Weapon.MeleeEffectKey`, derived at load; no keyword-table entry). The
  all-90 scratch re-import with the current supplements reads Assault 0 dead (was 3503). The shipped books' Dead 36
  (Counter-Attack family) stays until this item. After the re-bake, run #424's GUI hand-verify: a Battle Brothers unit
  with Heavy Pistols shoots at 9\"/12\" and then charges and strikes with the same pistols in one round.
- 2026-10-30 (from the orchestrator, after #442): once the 88 books are re-baked, run the Strategist-vs-Tactician
  head-to-head at 2,000 points on the shipped books (recipe: #442 D11, `FdgLab bench`, same seeds both arms) and record
  the number in #191; it decides whether the Strategist leaf is re-trained on 3.6 (plan section 7, post-release
  follow-up). Not a gate for this item.

- 2026-10-30 (from #442): after the re-bake, re-run #442's bench with armies compiled from the SHIPPED 3.6 books (it
  ran on the #435 scratch imports): `FdgLab bench --pool <dir> --profile-a tactician --profile-b solorules`, recipe and
  baseline numbers in `WorkItems/442-ai-and-calculator-parity-3-6.md` (D10/D11). The AI reads Spell Drain /
  Versatile / Ravage / Hazardous / Tag shapes by their EFFECTS, not their names, so renamed data keeps working; a
  change of effect shape (e.g. Spell Drain no longer `MoraleTestThen -> ConsumeToken(SpellTokens)`) would silently turn
  the Tactician's Spell Drain policy off - worth a glance in the bake diff.
- 2026-10-30 (from #435): the AoF re-bake must pick up: Banner's `moraleTestFromShootingOrMelee` condition (GDF file,
  shared), Great Sergeant (AP(+2), weapon scope) and Great Banner (grants Banner), Ossified / Warden (no 9" gate), Wave-Step
  `move: true`, Ethereal's `tokenPresent TeleportedThisActivation` gate, Grounded Speed (terrain gate only), the five new
  AoF names ("AP(+1) / Bane / Unstoppable on its weapons in melee", "Surge on their weapons", Piercing Charge Buff) and the
  AoF file's 3.6 texts. The AoF file no longer carries Difficult Terrain Debuff, Fortified Growth or Melee Slayer - AoF books
  embed the GDF definitions. **No `AofBookOverrides` file exists any more** (#435 D11: every 3.5 entry was moot against the
  3.6 books, so none was renamed to the 3.6 "Daemons of X" names); `scripts/bake-aof-books.sh` keeps the optional hook (keyed
  by the OPR book name) and still defaults to the 3.5 snapshot folder - your generalised script should point at
  `opr-json-snapshots-3.6.0`. Effect sets: the AoF table now has "Daemons of Change / Lust / Plague / War" (a 3.6 import of
  those books had none); the GDF table has no rows for the 3.6 GDF renames ("Daemons of Change / Lust / Plague / War",
  "Saurian Starhosts") or the new "Voidspace Stalkers" - a 3.6 GDF scratch import gives those books null default effect sets.
  After the re-bake, #436 drops the AoF "Rift Daemons of X" rows; `BookRuleFireLintTests`' "Ethereal" allowlist entry stays
  (the teleport ability still trips the lint, as in `RuleSupplementLintTests`).
- 2026-10-30 (from #434): the bake must pick up: (1) the reposition family's `move: true` (Bounding, Dash, Nimble,
  Rapid Blink, Wolfborn) - the bundled 3.5 books embed the placement copies; (2) Regenerative Strength's 3.6 consumer (the
  3.5 "+X attacks" stage code is deleted, so the two bundled 3.5 copies place markers that never spend until re-baked,
  #434 D9); (3) Ravage Aura now lives in the GDF supplement (aura + "Ravage (Aura)" helper) - Daemons of Plague (GDF) and
  the AoF books naming it embed it from there; (4) the importer no longer renames Lust / War Disciples' "Dangerous Terrain
  Debuff" to "(Immediate)" - re-baked books carry the bare name; (5) new JSON fields the baked definitions may carry:
  `repositionAtActivation.move`, `dealAutoWounds.perModel`, value sources `tokenTotal` / `argIfSoleTarget`,
  `selfWoundOnUnmodifiedRoll.countFromBlastOrDeadly`, `triggeredMove.oncePerRoundKey`, `targetSelector.maxCountFromArgument`
  (all optional; old data parses with the 3.5 meaning). Bundled `--rule-coverage` stayed "Dead: 36" through #434.
- 2026-10-30 (from #447): Impact / Heavy Impact are per model, and a one-model Impact item now compiles to a #407 model
  grant (`ListCompiler.ModelPersonalRuleNames`) unless every model still holds the item after the purchase. The
  re-bake brings 42 multi-model profile carriers (3.6.0 snapshots; today's bundled books have 73), 23 more multi-model
  units whose profile ITEM gives every model Impact (mounts / Trample; they fold unit-wide, which is right), and 4
  per-model items: Orc Marauders Hover Bikers "Rotor Blade Attack (Impact(3))", Ratmen Storm Ogres "Replace any ... ->
  Roller-Fists (Heavy Impact(3))" (both become grants), and Dragon Empire Dragonkin Horsemen / Ghostly Undead Dread
  Knights "Replace one ... and Trample -> Trample (Impact(1))" (the guard keeps them unit-wide). Re-pin
  `FdgRaylib.Tests/PerModelImpactShippedDataTests.ThePerModelItemPopulation_IsTheTwoBikers` (it lists the bundled 3.5
  sites, OrcMarauders Hover Bikers and DarkElfRaiders Jetbike Riders) after checking the re-baked sites pass
  `EveryPerModelItem_IsAOneModelGrant_AndRollsOnlyThatModelsDice` (it already expects the Trample swap to stay
  unit-wide); `EveryMultiModelProfileCarrier_RollsXPerModel` asserts at least 42 carriers.
- 2026-10-29 (from #431): `WeaponEffectAssigner.CarriesStrafing` (#379) gives range-0 bomb weapons the aerial-bomb
  effect set by matching the rule NAME "Strafing"; 3.6 bombs carry "Crossing Strike" instead, so add that name to the
  check when the effect-set keywords are redone, or the re-baked bombs fall back to a melee look.
- 2026-10-29 (from #422): the re-bake must clear four allowlist entries #422 added (each test fails once its entry
  goes stale): `BookRuleCensusTests` "Counter-Attack" and "Counter-Attack Aura" (15 bundled 3.5 books, 36 refs -
  `--rule-coverage` reports Dead: 36 until then), `BookSpellCoverageTests` / `BookSpellCastProbeTests`
  "Counter-Attack" (AoF-Orcs "Evil Gaze", ElvenJesters "Asphyxiating Fog" grant it; 3.6 grants "Counter in Melee
  once" - #439), and `BookRuleFireLintTests` "Counter in Melee" (AoF-ChivalrousKingdoms embeds the 3.5 copy with
  the now-inert `reduceImpactDicePerModel` facet). The re-bake also picks up the gated Heavy Impact (7 books embed
  the ungated 3.5 copy) and the gated, Counter-only "Counter in Melee".
- 2026-10-29 (from #420): the two Dangerous Terrain Debuff descriptions in `GdfRuleSupplement.json` ("Dangerous
  Terrain Debuff Effect" and "... (Immediate)") now describe the 3.6 test ("it rolls a die per wound it would
  take to destroy it, taking a wound on each 1"). The baked `.fdgbook` copies (11 books, including the
  spell-synthesized counts-as rules such as Cursed Stride; `OprBookImporter`'s synthesized text is already
  updated) still say "each moving model rolls a die" / "each of its models rolls a die" until this re-bake; after it,
  `grep -lE "each moving model rolls|each of its models rolls" FdgRaylib/Assets/Books/*.fdgbook` should be empty.
- 2026-10-29 (from #428): the bundled books still embed the pre-3.6 `allModelsHaveThisRule` kind (228 uses).
  It parses as the most-models gate, so the books already behave per 3.6, but `HumanInquisition.fdgbook`'s
  Inquisitorial Agent (a 3.6 "all models" holdout) also reads as "most" until this re-bake copies the swept
  supplement's `everyModelHasThisRule`. After the bake, check that no `.fdgbook` contains
  `allModelsHaveThisRule` and that Inquisitorial Agent carries `everyModelHasThisRule`. The bake's authoring
  validation now refuses the legacy kind in supplement input.
- 2026-10-29: filed with the plan.

## Decisions

- **D1 - Snapshot provenance: re-fetch all 90, bake every book from the re-fetch (2026-10-30). OWNER REVIEW.** The
  #437 hand-off named three stale books; a full re-fetch into new sibling folders
  (`~/Projects/GDF Armies/opr-json-snapshots-3.6.0-refetch-2026-10-30/` and the AoF twin; the 2026-10-29 folders are
  untouched) showed seven with real content changes: High Elf Fleets (Strafing AA-Laser Beamer 30" -> 15", 280 -> 265
  pts, a section re-targeted), Saurian Starhosts (Pistol -> Shock Pistol AP(2)), AoF Orcs (a base size), and FOUR
  edited without an `editedAt` bump (only `modifiedAt` moved): Alien Hives (Strafing Stalker-Bugs 30" -> 15"), Machine
  Cults (a Tail Gunner option re-cut: Archangel AA-System at 5 pts), Robot Legions (Strafing Radium Ray Cannon 30" -> 15", 60 ->
  30 pts), Orc Marauders (a unit 230 -> 225). Every other book differs only in section `id`s, which Army Forge
  regenerates on every request (D2). Provenance: **all 88 books come from the 2026-10-30 re-fetch** (48 GDF + 40 AoF;
  for the 81 unchanged books that is content-identical to 2026-10-29). Alternatives: re-fetch only the three (misses
  the four silent edits), or bake 2026-10-29 (a day stale).
- **D2 - Section Id = OPR section `uid`, not `id` (engine importer). OWNER REVIEW.** Two fetches of an unchanged book
  disagree on every section `id`; the `uid` is stable across requests and unique per unit in the whole 3.6.0 corpus
  (5880 sections, 0 duplicates). Keying on it means a re-fetch no longer re-keys every saved Forge pick, and a share
  list's `selectedUpgrades` (which carry the uid) hit the section directly. Caveat: OPR does mint a new uid when it
  edits a section (High Elf Fleets' "Replace all Ancient Swords" -> "Replace all Shard Pistols and Ancient Swords").
  Every section Id in the shipped books changed anyway with the 3.6 bake. Alternative: keep the volatile `id`.
- **D3 - A counted Replace yields to a later PASS-THROUGH rival (engine ListCompiler). OWNER REVIEW.** The 3.6
  Assault Brothers (9 GDF chapters) put "Replace any Heavy Pistol" above a combo "Replace one Heavy Pistol and Hand
  Weapon -> Heavy Pistol, Energy Hammer"; Havoc Destroyers (5 books) do the same with Hand Weapons. Book order let the
  any-swap take every pistol, starving the combo forever - the Energy Hammer was silently dropped and unpaid (the
  #323 corpus test caught it, 15 books red). Now a One/Any swap leaves copies for later-bought rivals whose option
  hands the target BACK; the parked remainder is retried after (#323's fixpoint), so the any-swap still lands in full
  and both purchases are charged. Alternatives: allowlist the 15 pairs (ships the dropped purchase), reorder choices
  globally, or reserve for every counted rival (changes book-order outcomes where the pool genuinely cannot cover
  both - pinned unchanged by `ReplaceAny_DoesNotYieldToAConsumingRival`).
- **D4 - Base-loadout items keep their nested weapons; removing the item removes them (engine). OWNER REVIEW.** OPR
  3.6 bundles mounts' and crews' weapons inside items (12 sites: Jackals' Charger Beast had NO weapon left, the
  Goblin Pump Plane lost its Bombing Run, Saurian Starhosts' Dread Titan its tail and claws, Duchies of Vinci's titan
  its guns...). The importer dropped them for base-loadout items (option-gained items were already routed; the 3.5
  books had the same gap at 19 sites). Now they join the unit's weapons (one copy per item), and the new optional
  `ItemEntry.Weapons` records them so "Replace Beast Gunner" takes the Fumigator with it. Old books and army files
  parse unchanged (the field is null and omitted). Alternatives: route only (a replaced mount leaves its weapons
  behind), or leave the gap.
- **D5 - Melee Takedown is accepted for High Elf Fleets' Stinger Dagger. OWNER REVIEW.** The #197 P16 invariant said
  no melee weapon carries Takedown and "the ruling needs revisiting" if one does. 3.6 has exactly one (Stingers'
  Stinger Dagger, A2, Bane, Takedown); the 3.6 text ("may pick any model in the target unit as its individual target
  for this weapon's attacks") applies to melee, and the lifted melee gate is what Takedown Strike already uses, so the
  pick in close combat is the rule. The test now pins exactly that one carrier.
- **D6 - Effect-set keywords for the 3.6 names (engine).** GDF: "cyclic" (storm rifles), "anti-matter" / "dark-matter"
  (gravity guns), "arc-charger" / "taser" (gauss), "psy-" (psychic guns; DaemonArcaneMelee "staff" / "psy-" for the
  psychic staffs), "magma" (plasma blades), "overrun" (TitanImpact), "chem-" (bio). AoF: "teeth", "sickle", "overrun",
  "titan strike", "barbed / trapper net". Generic words go in TRAILING rows so an established match keeps priority
  ("Acid Launcher" stays bio, "Soul Cannon" a cannon, "Sword & Staff" a sword): GDF launcher / "nade" / "mini-gl" /
  "gl-" / "demo charge" (mortar), "phosphor" (flame), "radium" (gauss); AoF caster "blast" / "spirit" / "soul" / "god
  strike" / "banshee" / "howl" / "shockwave" (arcane bolt), "rocks" / "boulder" / "toss" (sling), "staff" (melee
  energy). A diff of every old vs new match over both corpora changed only previously-unmatched names plus the
  intended Magma blades and Crossing Strike bombs. Known approximation: "Hook-Claw / Harpoon / Spear Launcher" read as
  mortar. `CarriesStrafing` matches "Crossing Strike" (#431) and still "Strafing" (old army files). Voidspace
  Stalkers ("Space Horrors": trench guns, mutant claws) = (ballistic-slug, claw-rend). No Assault per-phase default
  was needed (#424 B1 derives the melee key at load).
- **D7 - StormStage family deleted; catalog phrase helpers deferred to #439, "Very Fast" kept for #440.** Usage check
  over the shipped 3.6 books (and the 3.5 books, for embedded legacy JSON): `stormOfHits` / `invokeStorm` appear
  nowhere (3.5's Storm of X was catalog-only, deleted by #436), so StormStage, `Effect.StormOfHits`,
  `RuleOperation.InvokeStorm`, ChooseActionStage's routing and their lint / reference arms are gone (#436 D7). The
  three legacy JSON kinds stay. "AP(+1) in melee", "AP(1) when shooting", "Bane when attacking", "Shred when
  attacking", "+6\" range when shooting": no 3.6 book reaches them today, but they are spell-phrase targets
  (`SpellPhraseCloneTests`) and one of the 11 skipped spells (Corrode Weapons, "loses AP(1) when shooting") is exactly
  that phrase - the decision belongs to #439's parser. "Very Fast" is still used by the built-in EOF army (#440).
- **D8 - Unique deleted per the #436 list.** GDF entry, `ListValidator`'s Unique arm, both lint exemptions, the
  ListValidator / LaunchGate / DefaultBaseEstimator fixtures (re-pointed at Unique Hero where the test's subject
  survives). Consequence: a reopened 3.5 Forge army (which embeds its 3.5 book) no longer gets a duplicate-Unique
  Error - accepted (3.6 replaces 3.5; old files must load, not validate).
- **D9 - `scripts/bake-aof-books.sh` retired, not delegated.** The new script bakes both systems in one pass; a
  wrapper would have to re-implement "AoF only". Historical mentions in work-item files stay as history;
  `ShippedBooks.cs`' comment points at the new script.
- **D10 - Commit shape.** The renamed books went in a rename-only commit first (3.5 content under the new names +
  filename-only edits to three tests, built in the index, verified green in a worktree): in one combined commit git's
  similarity pairing crossed the GDF and AoF Daemons books, so `git log --follow` would have followed the wrong file.
  Books and test re-pins share one commit (splitting them would commit red).
- **D11 - Bake script shape.** Optional `outDir` (scratch bakes), `OFFLINE=1`, stale books LISTED not deleted, the
  importer's own parent-entry skip relied on. The section-shape / effects / bases / spells passes are no-ops on a
  fresh import and kept anyway so a future importer regression shows up as a non-zero summary.
- **D12 - Mutual cross-section pairs are checked from the naked unit's price (test).** 3.6 Biker Sisters' "Replace one
  Energy Lance -> Sgt. Pistol, Sgt. Hand Weapon" feeds "Replace Sgt. Hand Weapon -> Energy Lance", which hands the
  lance back; the grantor is starved in the baseline, so neither the leftover pool nor the granted price compares.
  Both purchases landing is asserted as naked + grant + swap x n.


## Outcome

Done 2026-10-30. `FdgRaylib/Assets/Books/` holds exactly the 48 GDF + 40 AoF OPR 3.6.0 books, every one stamped "OPR
v3.6.0", baked by one command (`scripts/bake-books.sh`, reproducible byte-for-byte) from the 2026-10-30 re-fetch;
the five renamed books followed their history by `git mv`, Voidspace Stalkers is new, and the 3.5 books live on
off-repo in `~/Projects/GDF Armies/fdgbooks-3.5-final/` (87 files + `SOURCE-COMMIT.txt`) for #440. Shipped
`--rule-coverage` Dead 475 -> 0; every unit of every book compiles; four book smokes and the default smoke exit 0.
The bake surfaced three engine defects, fixed with tests (D2 volatile section ids, D3 dropped combo purchases, D4
dropped item weapons), and the #436 deletions are done (Unique, the stale allowlists, the StormStage family);
the catalog phrase helpers wait for #439 (D7). Awaiting: #424's GUI hand-verify (Chris), the AI benches (orchestrator),
owner review of D1-D5.


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
