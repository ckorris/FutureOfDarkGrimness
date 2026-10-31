# 440 — Regenerate derived data; prove legacy 3.5 `.fdgarmy` files still load

**Status**: done 2026-10-31 (owner review: D1, D2, D3, D4, D5, D6, D10)
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

- 2026-10-31: **done.** Summary per goal (decisions below; per-army and per-scenario tables follow this bullet).
  (1) Random Army catalog: all 33 `armies/` files and the 8 `FdgLab/armies` 2k files rebuilt against the 3.6
  books by the new `--reforge` dev flag (`FdgRaylib/Cli/ArmyReforge.cs`): 33 units solved exactly, 128 by label
  translation, 201 by nearest fit, 4 by hand substitution (two roster units 3.6 removed), 0 unsolved; every file
  then fitted into its file-name bracket (95-100% of 1k/2k/3k/4k - the lobby's Random Army band). Every file now
  embeds the `OPR v3.6.0` book and its selections (`--retrofit-editable armies FdgLab/armies --dry-run`: 0
  converted, 40 already editable, 0 left alone). `git mv` "Wormhole Demons of Change 1k" -> "1k - Daemons of
  Change", "3k - Saurian Starhost" -> "3k - Saurian Starhosts" (the latter's army name followed the book rename,
  D9). All 40 armies play a headless game (`--headless --army <file>`): exit 0, `Game result:`, 0 `[rules]`
  warnings. `FdgLab/armies/pool.json`: 46 stale `armies/...` paths (not 44) re-pointed to `armies/GDF/...`
  (incl. the two renames); `mix.json` / `mix-strategist.json` carry no paths. Six ORIGINAL 3.5 files copied to
  `FdgRaylib.Tests/Fixtures/legacy-3.5/` before the rebuild (D5). (2) Scenarios: table below - 28/28 run headless
  to completion (`--headless --scenario <json> --all-ai`), 0 rule warnings; the 6 `.fdgsave` siblings recompiled
  with `--make-scenario` and each also runs to completion. (3) Built-in EOF army: Very Fast -> Fast; every other
  name it uses is printed by 3.6 books or the core rules (Vanguard and Martial Prowess are book-printed); catalog
  Very Fast deleted (D8). (4) Fixtures: nothing in either test project embeds 3.5 book data except
  `AssaultLegacyArmyTests`, which read the shipped 3.5 Battle Brothers list - re-pointed to the legacy fixture;
  no fixture deleted (D11). (5) Legacy proof: `Legacy35ArmyTests` (12 cases) + the Q5 notice (D6, D7). (6)
  Retrofit: see (1). Dist: `scripts/build-dist.sh linux` (the script supports a single RID) builds
  `FdgRaylib-linux-x64-v0.3.0-OPR_3_5_1.tar.gz`; `diff -rq armies dist/FdgRaylib-linux-x64/armies` is empty, and the
  dist binary plays `armies/AoF/1k - Ogres.fdgarmy` headless. **Chris hand-verify:** Random Army in the lobby, both
  systems (a GDF and an AoF lobby, a few re-rolls at 1000 and 2000), and a reopen of one rebuilt list in the Forge.
  Gates: engine 4442 -> 4454 passed / 1 skipped; app 3995 -> 4016; build 0 errors; `--validate-rules` OK 401;
  shipped `--rule-coverage` Dead 0 (30864 references); smoke exit 0 with `Game result:`.
- 2026-10-31: **rebuild table** (`--reforge armies FdgLab/armies`; "3.5 saved" is the file's own total, which on
  these Army Forge imports is Army Forge's figure; "recovered" is the 3.6 compile of the recovered picks; "final"
  is after the bracket fit). The FdgLab rows are the same lists as their `armies/` twins and produced identical
  reports.

| Army file | Book | Units: solved / translated / nearest / substituted | Points: 3.5 saved -> 3.6 recovered -> final (bracket) | Bracket fit |
|---|---|---|---|---|
| 1k - Duchies of Vinci.fdgarmy | Duchies of Vinci | 0 / 4 / 2 / 0 | 1000 -> 1055 -> **955** (1000) | cut unit Aerea Guard (1055 -> 940 pts); add upgrade 'Stealth Assassin (Ambush, Stealth)' to Vinci Leader (940 -> 955 pts) |
| 1k - Human Empire.fdgarmy | Human Empire | 2 / 1 / 5 / 0 | 1000 -> 965 -> **965** (1000) | - |
| 1k - Ogres.fdgarmy | Ogres | 1 / 3 / 2 / 0 | 1000 -> 1045 -> **970** (1000) | cut upgrade 'Hunting Harpoons (9", A1, AP(2), Deadly(3), Assault), Strike (A1)' from Hunters (1045 -> 970 pts) |
| 1k - Ossified Undead.fdgarmy | Ossified Undead | 4 / 4 / 0 / 0 | 1000 -> 1035 -> **965** (1000) | cut unit Reapers (1035 -> 925 pts); add upgrade 'Bone Throne (Spell Accumulator(2), Tough(3))' to Bone Master (925 -> 965 pts) |
| 1k - Alien Hives.fdgarmy | Alien Hives | 2 / 1 / 2 / 0 | 1000 -> 895 -> **995** (1000) | add upgrade 'Heavy Ravager Gun (18", A3, AP(2)), Claws (A1)' to Hive Guardians (895 -> 995 pts) |
| 1k - Blood Brothers.fdgarmy | Blood Brothers | 1 / 1 / 3 / 0 | 1000 -> 980 -> **980** (1000) | - |
| 1k - Daemons of Change.fdgarmy | Daemons of Change | 1 / 4 / 1 / 0 | 1000 -> 860 -> **960** (1000) | add upgrade 'Disc of Change (Strike (A3), Fast, Strider, Tough(3))' to Champion of Change (860 -> 940 pts); add upgrade 'Banner of Change (Bounding Aura)' to Change Warriors (940 -> 960 pts) |
| 1k - Human Defense Force.fdgarmy | Human Defense Force | 1 / 4 / 1 / 0 | 1000 -> 735 -> **975** (1000) | add upgrade 'Medical Training (Regeneration Aura)' to Infantry Squad (735 -> 780 pts); add upgrade 'Medical Training (Regeneration Aura)' to Infantry Squad (780 -> 825 pts); add upgrade 'Medical Training (Regeneration Aura)' to Infantry Squad (825 -> 870 pts); add upgrade 'Weapon Team (Wrecker-Cannon (36", A3, AP(1), Anti-Cover, Fracture), Tough(3))' to Infantry Squad (870 -> 910 pts); add upgrade 'Pintle Heavy Machinegun (15", A3, AP(1), Assault)' to Light APC (910 -> 945 pts); add upgrade 'Hull Heavy Machinegun (30", A3, AP(1))' to Light APC (945 -> 975 pts) |
| 1k - Prime Brothers.fdgarmy | Prime Brothers | 0 / 1 / 3 / 1 | 1000 -> 1025 -> **1000** (1000) | cut upgrade 'Captain (Bane when Shooting Aura)' from Prime Master (1025 -> 1000 pts) |
| 2k - Alien Hives - Horde Melee.fdgarmy | Alien Hives | 3 / 2 / 3 / 1 | 1990 -> 1790 -> **1900** (2000) | add upgrade 'Skewer Launcher (30", A2, AP(3), Unstoppable), Claws (A1)' to Hive Guardians (1790 -> 1900 pts) |
| 2k - Battle Brothers - Elite Shooting.fdgarmy | Battle Brothers | 1 / 2 / 5 / 0 | 1985 -> 2155 -> **1990** (2000) | cut unit APC (2155 -> 1875 pts); add upgrade 'Assault Bike (Assault Heavy Bike-Rifle (9", A3, AP(1), Assault), Fast, Tough(3))' to Veteran Master Brother (1875 -> 1990 pts) |
| 2k - Blessed Sisters.fdgarmy | Blessed Sisters | 0 / 4 / 5 / 0 | 2000 -> 1830 -> **1930** (2000) | add upgrade 'Holy Wings (Holy Flame Blast (9", A1, Blast(3), Reliable, Assault, Purge), Flying, Tough(3))' to Celestial High Sister (1830 -> 1930 pts) |
| 2k - Dark Elf Raiders - Transport.fdgarmy | Dark Elf Raiders | 0 / 3 / 6 / 0 | 2000 -> 2015 -> **2000** (2000) | cut upgrade 'Shield of Darkness (Stealth)' from Light Skimmer (2015 -> 2000 pts) |
| 2k - Dwarf Guilds - Ambush and Scout-Heavy.fdgarmy | Dwarf Guilds | 1 / 1 / 9 / 0 | 1990 -> 1990 -> **1990** (2000) | - |
| 2k - High Elf Fleets - Caster-Heavy.fdgarmy | High Elf Fleets | 0 / 1 / 8 / 0 | 1980 -> 2345 -> **1955** (2000) | cut unit Great Elemental (2345 -> 1890 pts); add upgrade 'Flicker Shield (Regeneration Aura)' to Jetbike Protectors (1890 -> 1955 pts) |
| 2k - Human Defense Force - Tough and Vehicle-Heavy.fdgarmy | Human Defense Force | 0 / 2 / 6 / 0 | 1965 -> 1910 -> **1910** (2000) | - |
| 2k - Orks - Horde Mixed.fdgarmy | Orc Marauders | 0 / 6 / 6 / 0 | 1990 -> 2510 -> **1975** (2000) | cut unit Combat Walker (2510 -> 2185 pts); cut unit Berserkers (2185 -> 1975 pts) |
| 2k - Robot Legions - Mixed.fdgarmy | Robot Legions | 1 / 4 / 3 / 0 | 1965 -> 1750 -> **1920** (2000) | add a copy of Forge Spider (1750 -> 1920 pts) |
| 3k - Battle Brothers.fdgarmy | Battle Brothers | 1 / 5 / 3 / 0 | 3000 -> 2900 -> **2900** (3000) | - |
| 3k - DAO Union.fdgarmy | DAO Union | 0 / 1 / 10 / 0 | 2990 -> 2430 -> **2880** (3000) | add a copy of Hover Tank (2430 -> 2880 pts) |
| 3k - Dark Elf Raiders.fdgarmy | Dark Elf Raiders | 0 / 1 / 10 / 0 | 2990 -> 2685 -> **2960** (3000) | add a copy of Light Skimmer (2685 -> 2960 pts) |
| 3k - Eternal Dynasty.fdgarmy | Eternal Dynasty | 1 / 9 / 2 / 0 | 3000 -> 3030 -> **3000** (3000) | cut upgrade 'Shield Drone (Stealth)' from ONI Walker (3030 -> 3000 pts) |
| 3k - Goblin Reclaimers.fdgarmy | Goblin Reclaimers | 0 / 8 / 4 / 0 | 2985 -> 2990 -> **2990** (3000) | - |
| 3k - High Elf Fleets.fdgarmy | High Elf Fleets | 0 / 1 / 8 / 0 | 3000 -> 2440 -> **2875** (3000) | add a copy of Great Elemental (2440 -> 2875 pts) |
| 3k - Knight Brothers.fdgarmy | Knight Brothers | 0 / 2 / 7 / 0 | 2995 -> 2415 -> **2920** (3000) | add a copy of Knight Veteran Combat Walker (2415 -> 2920 pts) |
| 3k - Robot Legions.fdgarmy | Robot Legions | 1 / 7 / 5 / 0 | 2995 -> 2860 -> **2860** (3000) | - |
| 3k - Saurian Starhosts.fdgarmy | Saurian Starhosts | 2 / 7 / 4 / 0 | 3000 -> 3310 -> **2910** (3000) | cut unit Raptor Riders (3310 -> 2910 pts) |
| 3k - Soul-Snatcher Cults.fdgarmy | Soul-Snatcher Cults | 0 / 6 / 7 / 0 | 2995 -> 2465 -> **2910** (3000) | add a copy of Battle Tank (2465 -> 2910 pts) |
| 3k - Titan Lords.fdgarmy | Titan Lords | 1 / 0 / 5 / 0 | 2990 -> 2910 -> **2910** (3000) | - |
| 4k - Alien Hives.fdgarmy | Alien Hives | 2 / 7 / 5 / 1 | 4000 -> 3615 -> **3815** (4000) | add upgrade 'Heavy Perforator Cannon (30", A3, AP(4), Deadly(6))' to Hive Titan (3615 -> 3815 pts) |
| 4k - Havoc Brothers.fdgarmy | Havoc Brothers | 0 / 5 / 6 / 0 | 3995 -> 3680 -> **3950** (4000) | add a copy of Havoc Destroyers (3680 -> 3950 pts) |
| 4k - High Elf Fleets.fdgarmy | High Elf Fleets | 1 / 0 / 9 / 0 | 4000 -> 3615 -> **3885** (4000) | add a copy of Elemental Strikers (3615 -> 3885 pts) |
| Alien Hives 2k - Horde Melee.fdgarmy | Alien Hives | 3 / 2 / 3 / 1 | 1990 -> 1790 -> **1900** (2000) | add upgrade 'Skewer Launcher (30", A2, AP(3), Unstoppable), Claws (A1)' to Hive Guardians (1790 -> 1900 pts) |
| Battle Brothers 2k - Elite Shooting.fdgarmy | Battle Brothers | 1 / 2 / 5 / 0 | 1985 -> 2155 -> **1990** (2000) | cut unit APC (2155 -> 1875 pts); add upgrade 'Assault Bike (Assault Heavy Bike-Rifle (9", A3, AP(1), Assault), Fast, Tough(3))' to Veteran Master Brother (1875 -> 1990 pts) |
| Dark Elf Raiders 2k - Transport.fdgarmy | Dark Elf Raiders | 0 / 3 / 6 / 0 | 2000 -> 2015 -> **2000** (2000) | cut upgrade 'Shield of Darkness (Stealth)' from Light Skimmer (2015 -> 2000 pts) |
| Dwarf Guilds 2k - Ambush and Scout-Heavy.fdgarmy | Dwarf Guilds | 1 / 1 / 9 / 0 | 1990 -> 1990 -> **1990** (2000) | - |
| High Elf Fleets 2k  - Caster-Heavy.fdgarmy | High Elf Fleets | 0 / 1 / 8 / 0 | 1980 -> 2345 -> **1955** (2000) | cut unit Great Elemental (2345 -> 1890 pts); add upgrade 'Flicker Shield (Regeneration Aura)' to Jetbike Protectors (1890 -> 1955 pts) |
| Human Defense Force 2k - Tough and Vehicle-Heavy.fdgarmy | Human Defense Force | 0 / 2 / 6 / 0 | 1965 -> 1910 -> **1910** (2000) | - |
| Orks 2k - Horde Mixed.fdgarmy | Orc Marauders | 0 / 6 / 6 / 0 | 1990 -> 2510 -> **1975** (2000) | cut unit Combat Walker (2510 -> 2185 pts); cut unit Berserkers (2185 -> 1975 pts) |
| Robot Legions 2k - Mixed.fdgarmy | Robot Legions | 1 / 4 / 3 / 0 | 1965 -> 1750 -> **1920** (2000) | add a copy of Forge Spider (1750 -> 1920 pts) |

  Catalog warnings left in place (force-org Warnings, never Errors; none blocks a launch, all show in the Forge):
  `1k - Alien Hives` Hive Lord 355 pts (35% cap 350); `1k - Human Defense Force` 3 Infantry Squads (max 2) and 6
  units (max 5) - both already true of the 3.5 list; `2k - Robot Legions - Mixed` (and its FdgLab twin) Great
  Monolith 805 pts (cap 700); `3k - Goblin Reclaimers` 158 models/Tough (max 150). The fit refuses to make any of
  them worse; clearing them means changing the list's character and is Chris's call (D4).
- 2026-10-31: **scenario run table** (`--headless --scenario Scenarios/<name>.json --all-ai`, current build):

| Scenario | Exit | Rule warnings | Result |
|---|---|---|---|
| 256-friendly-in-lane | exit=0 | warnings=0 | outcome=Tie winner=none rounds=4 scores=[0, 0] |
| 332-match-already-decided | exit=0 | warnings=0 | outcome=Win winner="Player 1 (AI)" rounds=3 scores=[2, 0] |
| 334-forced-charge-band | exit=0 | warnings=0 | outcome=Tie winner=none rounds=4 scores=[0, 0] |
| 336-weapon-rules-showcase | exit=0 | warnings=0 | outcome=Tie winner=none rounds=4 scores=[0, 0] |
| 337-shaken-picker | exit=0 | warnings=0 | outcome=Win winner="Player 1 (AI)" rounds=4 scores=[1, 0] |
| 340-sniper-split-targets | exit=0 | warnings=0 | outcome=Win winner="Player 2 (AI)" rounds=3 scores=[0, 1] |
| 363-wall-shadow-engage | exit=0 | warnings=0 | outcome=Tie winner=none rounds=4 scores=[0, 0] |
| 377-spell-verify | exit=0 | warnings=0 | outcome=Win winner="Player 1 (AI)" rounds=4 scores=[2, 0] |
| 401-deadly-clumps | exit=0 | warnings=0 | outcome=Tie winner=none rounds=4 scores=[0, 0] |
| 401-deadly-clumps-singles | exit=0 | warnings=0 | outcome=Tie winner=none rounds=4 scores=[0, 0] |
| 408-versatile-backout | exit=0 | warnings=0 | outcome=Win winner="Player 1 (AI)" rounds=3 scores=[1, 0] |
| 425-artillery-hold | exit=0 | warnings=0 | outcome=Tie winner=none rounds=4 scores=[0, 0] |
| 446-activate-in-dangerous | exit=0 | warnings=0 | outcome=Tie winner=none rounds=4 scores=[0, 0] |
| crowded-2v2-3k | exit=0 | warnings=0 | outcome=Win winner="Player 1 (AI) and Player 2 (AI)" rounds=4 scores=[2, 1, 0, 0] |
| example-shootout | exit=0 | warnings=0 | outcome=Win winner="Player 1 (AI)" rounds=4 scores=[2, 0] |
| example-walled-advance | exit=0 | warnings=0 | outcome=Tie winner=none rounds=4 scores=[0, 0] |
| instinctive-compelled-shoot | exit=0 | warnings=0 | outcome=Tie winner=none rounds=4 scores=[0, 0] |
| limited-hold-fire | exit=0 | warnings=0 | outcome=Win winner="Player 2 (AI)" rounds=4 scores=[0, 1] |
| limited-weapons-test | exit=0 | warnings=0 | outcome=Win winner="Player 1 (AI)" rounds=2 scores=[1, 0] |
| p16-takedown-extra-attacks | exit=0 | warnings=0 | outcome=Tie winner=none rounds=4 scores=[0, 0] |
| p19-coordinate | exit=0 | warnings=0 | outcome=Tie winner=none rounds=4 scores=[0, 0] |
| pilein-wrap-demo | exit=0 | warnings=0 | outcome=Tie winner=none rounds=4 scores=[0, 0] |
| regenerative-strength | exit=0 | warnings=0 | outcome=Tie winner=none rounds=4 scores=[0, 0] |
| rout-spillout | exit=0 | warnings=0 | outcome=Tie winner=none rounds=4 scores=[0, 0] |
| surprise-attack-ai-target | exit=0 | warnings=0 | outcome=Tie winner=none rounds=4 scores=[0, 0] |
| surprise-attack-burst | exit=0 | warnings=0 | outcome=Win winner="Player 1 (AI)" rounds=4 scores=[1, 0] |
| vengeance-marks-the-killer | exit=0 | warnings=0 | outcome=Tie winner=none rounds=4 scores=[0, 0] |
| vinci-tech-gate | exit=0 | warnings=0 | outcome=Win winner="Player 2 (AI)" rounds=4 scores=[0, 1] |

  `.fdgsave` siblings recompiled and run (`--scenario <save> --all-ai`): 334-forced-charge-band, 340-sniper-split-
  targets, 401-deadly-clumps-singles, 401-deadly-clumps, 408-versatile-backout, vinci-tech-gate - all exit 0 with
  `Game result:`, 0 warnings. Note (not a #440 regression): in `377-spell-verify` the all-AI run casts nothing (the
  solo-rules bot spends its activations moving/attacking); the scenario is a hand-verify one.

- 2026-10-31 (from #439): (1) the books were re-baked with the 3.6 spell grammar (45 books changed, 527 spells): spell
  grants and marks now carry canonical names ("Shred when shooting", "Bane when attacking", Increased Shooting Range,
  ...), 8 new token-drain spells, Corrode Weapons and Ancestral Guidance - regenerate derived data from these books,
  not the 10-30 bake. (2) Legacy-load check: names a saved army may still grant that NO LONGER RESOLVE from the catalog
  or supplements - the catalog helper "+6\" range when shooting" (deleted, #439 D8; a 3.5 Battle Rune / Eternal
  Guidance / Clearview Leaves grants it) and the 15 supplement phrase clones ("Shred on its weapons when shooting",
  "Unstoppable on their weapons when shooting", "AP(1) on their weapons when shooting", "Indirect on their weapons",
  "Rending on their weapons in melee", "+6\" range on its/their weapons when shooting", "Bane on their weapons when
  attacking", "Shred on its weapons in melee", "Shred on their weapons when attacking", "Unwieldy on its weapons in
  melee", "AP(+1) / Bane / Unstoppable on its weapons in melee", "Surge on their weapons"; #439 D9). An army saved
  between #438 and #439 embeds the clone definitions it used, so it still resolves them; a 3.5 army naming
  "+6\" range when shooting" drops that one grant at load (warns, no crash) - decide whether the Q5 notice should say so.
  (3) `Scenarios/377-spell-verify.json` exercises 3.5 spell shapes (Burn the Heretic as "-3 to casting rolls") - 3.6's
  Burn the Heretic is a 2-token drain; re-author it with the scenarios.
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

- **D1 (OWNER REVIEW) - the catalog is rebuilt mechanically, not by hand.** Alternatives: (a) re-import Chris's original
  Army Forge share links against 3.6 (the files carry no link; not available); (b) hand-rebuild 41 lists (~400 units)
  in the Forge; (c) a dev tool that recovers each unit's picks against the 3.6 book and recompiles. Chose (c),
  `--reforge` (`FdgRaylib/Cli/ArmyReforge.cs`, beside the other retrofit flags; `--dry-run` reports only). Per unit, in
  order of trust: `SelectionSolver.Solve` (exact size + weapon loadout, as `--retrofit-editable`); label translation of
  the file's own embedded 3.5 selections (same roster unit by id/name, each pick mapped by section/option label, the
  option's name before its profile, or identical gains; rejected if the translated picks raise a catalog Error);
  nearest fit (`SelectionSolver.SolveNearest`, new, D2); the substitution table (D3). Then `ListCompiler.Compile`
  with the 3.6 book, so stats, rules, spells, effect sets and points are 3.6 and the file reopens in the Forge. The
  per-unit ledger at the bottom lists every translated/nearest/substituted unit with its weapon diff.
- **D2 (OWNER REVIEW) - nearest fit for units 3.6 reprinted.** 201 of the 366 unit rebuilds (41 files) have no exact or label counterpart:
  3.6 moved options into base loadouts (Human Empire Marksmen now carry Matchlock Rifles), renamed profiles (Rifle ->
  Battle Rifle, CCW -> Strike / Hand Weapon) or merged sections. `SolveNearest` walks the same option space as the
  exact solver and keeps the pick-set that `ArmyReforge.Closeness` rates highest: size first (-2 per model), then each
  saved weapon copy paired greedily with its most similar candidate copy (same name 1.0, else word overlap; melee vs
  ranged role; range; attacks; AP; weapon rules), -0.25 per unused candidate copy, +0.3 / -0.15 per shared / extra
  unit rule; price closeness breaks ties. The two-copy combined reading is tried for any candidate too small to be the
  unit alone. Alternative: leave such units unsolved for hand work (200 hand substitutions). Every one is in the
  ledger for review; outliers worth a look: `3k - Saurian Starhosts` Raptor Riders (5 -> 6 models, 400 pts; one of
  the two was then cut by the fit), `3k - Soul-Snatcher Cults` Neophyte Bikers (Combined) 6 -> 10 models (3.6's
  bikers come in fives), `2k - High Elf Fleets` Elven Noble 115 -> 250 (the nearest jetbike-mounted noble).
- **D3 (OWNER REVIEW) - hand substitutions for units 3.6 removed** (`ArmyReforge.Substitutions`; the loadout then
  follows by nearest fit): `1k - Prime Brothers` Blaster Brothers (5 models, plasma, Relentless; 270) -> Support Prime
  Brothers as a combined pair with Plasma Rifles (6 models; 350). `2k - Alien Hives - Horde Melee` (and its FdgLab
  twin) Winged Grunts (10, Bio-Borer + Razor Claws, Flying; 125) -> Shooter Grunts combined pair with Wings (10,
  Bio-Ravager + Claws, Flying; 160); `4k - Alien Hives` Winged Grunts (10, Bio-Ravager; 155) -> the same (160).
  Candidates offered: Assault Grunts and Shooter Grunts (3.6 makes Wings an upgrade on both); the closeness chose
  Shooter Grunts for all three.
- **D4 (OWNER REVIEW) - bracket fit.** Every rebuilt list is brought to 95-100% of its file-name bracket (the lobby's
  `BotArmyPicker.BandPercentUnderLimit` band, so Random Army still offers it at that size): over, cut upgrade picks
  before units (the cut landing closest under; if none gets under, the largest non-hero unit); under, add upgrade
  picks before copies of existing non-hero units (the addition landing closest to the bracket without passing it).
  Heroes are never cut or copied; no repeat application of a pick already taken (except added models); an addition
  may not raise any catalog Error or force-org Warning the list does not already show word for word, a cut may not
  raise a new kind. Each army's actions are in the table's last column. Alternatives: leave lists wherever 3.6
  pricing put them (735-2510 in a 1000/2000 bracket - unusable for Random Army), or hand-fit 30 lists. Notable cuts:
  `2k - High Elf Fleets` lost its Great Elemental, `2k - Orks` its Combat Walker and a Berserker squad, `2k - Battle
  Brothers` its APC, `3k - Saurian Starhosts` one Raptor Riders unit, `1k - Duchies of Vinci` its Aerea Guard,
  `1k - Ossified Undead` its Reapers.
- **D5 (OWNER REVIEW) - the six legacy fixtures** (`FdgRaylib.Tests/Fixtures/legacy-3.5/`, originals copied before the
  rebuild; their rebuilt versions stay in `armies/` so the catalog keeps all 33): `Wormhole Demons of Change 1k`
  (renamed GDF Daemons family), `3k - Saurian Starhost` (renamed Saurian family), `2k - Orks - Horde Mixed` (deleted
  catalog names Resistance + Piercing Assault, #436), `1k - Ogres` (AoF; Resistance), `2k - Dwarf Guilds - Ambush and
  Scout-Heavy` (Battle Rune grants the deleted "+6\" range when shooting", #439 D8), `2k - Robot Legions - Mixed`
  (plain). No catalog list names Strafing; Crack is defined by these 3.5 books themselves, so it resolves from the
  embedded copy and is not a drop. The AoF Rift Daemons family has no shipped list, so its case is the faction string.
- **D6 (OWNER REVIEW) - the legacy path gets the diagnostic only, no dropped-name -> 3.6-successor table** (#436 D2).
  A table (Strafing -> Crossing Strike, Courage Aura -> Bravery Aura, Piercing Assault -> Piercing Charge, ...) would
  silently change how an old list plays: the successors have different mechanics and are supplement rules the old file
  does not embed. The #168 drop is honest, the Q5 notice tells the player the list plays under 3.6, and the measured
  cost is small (the six fixtures drop at most two names each: Resistance, Piercing Assault, the +6" helper - 3.5
  lists embed their books' own definitions, so most old names still resolve). Not cheap-and-clearly-right, so not
  built.
- **D7 - Q5 notice shape.** `FDG.ArmyBuilding.LegacyArmyNotice`: the ruleset is the embedded book's version stamp; a
  parseable stamp whose major.minor is not 3.6 is legacy; a hand-authored list (no book) gets no notice. Text: "Army
  '<name>' was built for OPR v3.5.3; it plays with 3.6 rules" (the actual stamp, not the literal "v3.5.x"). The stamp
  rides the roster as `ArmyListSummary.RulesVersion` (new optional field; the wire round-trip is tested), so every
  machine draws the same amber lines under the lobby's player table - where the roster shows army problems - never
  blocking; the CLI prints the line after "Loaded '<name>' ...". The Forge is not touched (#441 hand-off).
- **D8 - Very Fast deleted from the catalog.** Its last users were the EOF army (now Fast, keeping a movement rule), the
  FdgLab copy of that army (synced; it also still carried Strafing, deleted by #436 - gone too), the engine's
  rich test army (same) and its own movement test (deleted). No data file names it. Shipped Dead stays 0.
- **D9 - an army still named after its book follows the rename** ("Saurian Starhost" -> "Saurian Starhosts"); names a
  user chose are kept.
- **D10 (OWNER REVIEW) - scenario armies re-authored from the 3.6 books.** `408-VersatileAndWeaponTeam`: the hero's
  Versatile Attack -> Versatile Expert (its 3.6 successor, same activation-start pick, so the #408 back-out still has a
  pick to back out of); definitions = the 3.6 Prime Brothers book's + HDF's Hold the Line. `377-Verifiers`: the four
  spells re-taken verbatim from the 3.6 books (Burn the Heretic is a 2-token drain; Battle Rune / Eternal Guidance grant
  Increased Shooting Range; thresholds and targets are 3.6's). `VinciGateDemo`: `gameSystem: age-of-fantasy` + the 3.6
  Vinci Tech definitions (shooting over 9" / charging over 6"); the hand-authored Heavy Crossbow stays (the demo needs
  a gun; 3.6 Aerea Guard have only claws). `limited-weapons-test.json` pointed at a root file deleted by 17c0081; the
  hand-authored army is restored from 9753d71 as `Scenarios/armies/LimitedWeaponsTest.fdgarmy`.
- **D11 - fixtures (Goal 4): nothing deleted.** The survey's named tests carry names only (`CombatCalculatorScreenTests`,
  `GameAnalyticsSessionTests`), the `*ShippedDataTests` read the bundled (re-baked) books, `FdgRaylib/GoodGuys.fdgarmy`,
  `FutureOfDarkGrimness/armies/example-*.fdgarmy` and `FdgLab/probes/armies` are hand-authored without a book; the one
  test reading a shipped 3.5 list (`AssaultLegacyArmyTests`) now reads the Robot Legions fixture.
- **D12 - two solver fixes found by the rebuild** (engine): `SolveNearest` must try the pair reading for a candidate too
  small to be the unit alone even when another candidate matched the size; and a recovered combined pair must link its
  halves when the saved unit had no id (an Army Forge import often has none) - `Solve` had the same latent bug (#357
  tool), now fixed for both, with tests.

## Outcome

Done 2026-10-31. The Random Army catalog (33 lists) and the FdgLab 2k pool (8) are 3.6 Forge lists inside their
brackets, rebuilt by `--reforge` with every recovery, substitution and bracket adjustment recorded (D1-D4, tables
above, ledger below); scenarios are on 3.6 and all 28 run headless; the EOF army is 3.6-legal and catalog Very Fast is
gone; 3.5 lists keep loading and playing, proven by `Legacy35ArmyTests` over six original catalog lists (exact #168
drops, both renamed GDF families plus the AoF one), with the Q5 amber notice in the lobby and CLI. Owner review: D1,
D2, D3, D4, D5, D6, D10. Awaiting Chris's hand-verify of Random Army in the lobby (GDF and AoF) and one Forge reopen.

Commits - engine: a32a897b, f38dcd6f, e3b0311c, 8576e3f9, 97ea3cf0; superproject: 4a5fc0f, 7a882d6, 6953436, 5dd21a7,
fa70650, 60044a0 (+ the docs commit).

### Appendix - per-unit rebuild ledger (`armies/`; translated, nearest and substituted units with weapon diffs)

`{...}` is the weapon-copy diff of the 3.6 unit against the saved 3.5 unit; `[models a -> b]` a size change.

- **1k - Duchies of Vinci.fdgarmy**
  - translated  Automa Drones: 210 -> 230  {-Rotor Blades x10, +Strike x10}
  - nearest     Vita Aerea: 210 -> 200  {-Crew Attacks x1, +Overrun x1}
  - translated  Aerea Guard: 115 -> 115  {+Dual Claws x3, -Dual Heavy Claws x3}
  - nearest     Vinci Leader: 95 -> 90  {-Advanced Crossbow x1, -Heavy Hand Weapon x1, +Rapid Crossbow x1, +Strike x1}
  - translated  Harlequins: 150 -> 140  {+Jester Club x10, -Juggler Bombs x10, -Knife x10}
  - translated  Automa Guard: 220 -> 280  {+Automa Sword x10, -Hand Weapon x10}
- **1k - Human Empire.fdgarmy**
  - translated  Mage Council: 170 -> 130  {-Hand Weapon x5}
  - nearest     Battle Master: 120 -> 125  {-Heavy Spear x1, +Master Spear x1}
  - nearest     Marksmen: 180 -> 160  {-Hand Weapon x10, +Matchlock Rifle x8, -Rifle x8, +Strike x10}
  - nearest     Horse Marksmen: 110 -> 95  {-Hand Weapon x5, +Matchlock Rifle x4, -Rifle x4, +Strike x5}
  - nearest     Marksmen: 180 -> 160  {-Hand Weapon x10, +Matchlock Rifle x8, -Rifle x8, +Strike x10}
  - nearest     Battle Master: 115 -> 130  {-Heavy Great Weapon x1, +Master Great Weapon x1}
- **1k - Ogres.fdgarmy**
  - translated  Tribe Master: 130 -> 145  {+Dual Hand Weapons x1, -Heavy Hand Weapon x1}
  - nearest     Ogre Warriors: 125 -> 125  {+Dual Hand Weapons x3, -Dual Heavy Hand Weapons x1, -Heavy Hand Weapon x2}
  - translated  Sabre-Tooths: 160 -> 160  {-Jagged Claws x3, +Rough Claws x3}
  - translated  Shooters: 200 -> 175  {+Cannon Slug-Gun x3, -Hand Cannon x3, -Hand Weapon x3, +Strike x3}
  - nearest     Hunters: 215 -> 250  {-Great Javelin x3, +Hunting Harpoons x3, -Knife x3, +Strike x3}
- **1k - Ossified Undead.fdgarmy**
  - translated  Bone Master: 65 -> 70  {+Dual Hand Weapons x1, -Halberd x1}
  - translated  Bone Master: 35 -> 40  {+Dual Hand Weapons x1, -Halberd x1}
  - translated  Guardian Archers: 200 -> 140  {+Bow x10, -Hand Weapon x10, -Heavy Bow x10, +Strike x10}
  - translated  Skeleton Horror: 165 -> 175  {+Dual Hand Weapons x1, -Heavy Halberd x1}
- **1k - Alien Hives.fdgarmy**
  - nearest     Hive Lord: 395 -> 355  {+Claws x2, +Great Serrated Blade x1, -Heavy Razor Claws x2, -Heavy Slashing Blade x1, -Stomp x1}
  - nearest     Ravenous Beasts: 205 -> 155  {+Dual Razor Claws x3, -Razor Claws x3, -Razor Whip x3}
  - translated  Hive Guardians: 215 -> 165  {+Dual Smashing Clubs x3, -Razor Claws x2, -Serrated Blade x4}
- **1k - Blood Brothers.fdgarmy**
  - nearest     Blood Battle Brothers: 180 -> 165  {-CCW x5, -Gravity Rifle x1, +Heavy Rifle x1, +Strike x5}
  - translated  Blood Battle Brothers: 200 -> 195  {-CCW x5, +Strike x5}
  - nearest     Blood Assault Brothers: 200 -> 230  {+Great Chain-Sword x1, +Hand Weapon x3, -Heavy CCW x3, -Heavy Chainsaw Sword x1, +Heavy Pistol x1, -Sgt. Heavy Pistol x1}
  - nearest     Blood APC: 280 -> 280  {+Burst Cyclic Rifle x1, -Heavy Fusion Rifle x1, +Overrun x1, +Pintle Heavy Fusion Rifle x1, -Storm Rifle x1}
- **1k - Daemons of Change.fdgarmy**
  - translated  Change Horrors: 205 -> 150  {-Fireballs x5}
  - translated  Champion of Change: 70 -> 80  {+Dual Hand Weapons x1, -Power Hammer x1, -Staff x1}
  - translated  Change Warriors: 180 -> 140  {-Fireballs x10, -Hand Weapon x10, +Power Sword x10}
  - translated  Screecher Chariot: 215 -> 180  {+Screechers x1, -Sharp Beak x1}
  - nearest     Hex-Furies: 115 -> 90  {-Claws x5, +Strike x5}
- **1k - Human Defense Force.fdgarmy**
  - translated  Infantry Squad: 220 -> 150  {+Battle Rifle x7, -CCW x9, -Crew x1, -Rifle x7, +Strike x10}
  - translated  Infantry Squad: 230 -> 170  {+Battle Rifle x7, -CCW x9, -Crew x1, +Heavy Machinegun x1, -Missile Launcher x1, -Rifle x7, +Strike x10}
  - translated  Snipers: 125 -> 75  {-CCW x3, -Sniper Rifle x3, +Strike x3, +Wrecker-Sniper Carbine x3}
  - translated  Light APC: 200 -> 145  {+Overrun x1, -Turret Heavy Flamer x1}
  - nearest     Infantry Squad: 135 -> 125  {+Battle Rifle x9, -CCW x9, -Drum Pistol x1, -Rifle x9, +Shredder Drum-Pistol x1, +Strike x9}
- **1k - Prime Brothers.fdgarmy**
  - nearest     Prime Master: 100 -> 120  {-CCW x1, +Hand Weapon x1, -Master Plasma Pistol x1, +Plasma Pistol x1}
  - nearest     Prime Brothers: 180 -> 185  {-CCW x5, +Heavy Rifle x1, -Precision Rifle x1, +Strike x5}
  - nearest     Prime Brothers: 190 -> 185  {-CCW x4, +Heavy Pistol x1, +Heavy Rifle x3, -Precision Rifle x3, -Sgt. Heavy Pistol x1, +Strike x4}
  - SUBSTITUTED Blaster Brothers -> Support Prime Brothers: 270 -> 350 [models 5 -> 6]  {-CCW x5, -Plasma Auto-Rifle x2, +Plasma Rifle x3, +Strike x6}
  - translated  Prime ATV: 245 -> 185  {-Gatling Gun x1, +Heavy Bike-Rifle x1, +Heavy Flamer x1, +Overrun x1, -Twin Heavy Rifle x1}
- **2k - Alien Hives - Horde Melee.fdgarmy**
  - nearest     Hive Warriors: 175 -> 170  {+Bug-Spewer x2, -Razor Whip x6, +Shredder Gun x1, +Toxin Whip x3}
  - nearest     Ravenous Beasts (Combined): 550 -> 310  {+Dual Toxin Whip-Limbs x6, -Razor Whip x6, -Slashing Blade x6}
  - nearest     Hive Lord: 455 -> 480  {+Great Serrated Blade x1, -Heavy Razor Claws x2, -Stomp x1}
  - translated  Hive Guardians: 245 -> 165  {+Dual Smashing Clubs x3, -Serrated Blade x6}
  - SUBSTITUTED Winged Grunts -> Shooter Grunts: 125 -> 160  {-Bio-Borer x10, +Bio-Ravager x10, +Claws x10, -Razor Claws x10}
  - translated  Hive Swarms: 75 -> 85  {-Swarm Attacks x3, +Toxin Claws x3}
- **2k - Battle Brothers - Elite Shooting.fdgarmy**
  - nearest     Master Brother: 85 -> 175  {-Master Plasma Pistol x1, +Plasma Pistol x1}
  - translated  Battle Brothers: 180 -> 195  {-CCW x5, +Strike x5}
  - translated  Battle Brothers: 165 -> 175  {-CCW x5, +Strike x5}
  - nearest     Master Destroyer: 195 -> 230  {+Burst Cyclic Rifle x1, -CCW x1, +Hand Weapon x1, -Rapid Storm Rifle x1}
  - nearest     Destroyers: 465 -> 385  {+Burst Cyclic Rifle x3, -CCW x3, -Cyclone Missiles x1, +Hand Weapon x3, +Missile Rack x1, -Storm Rifle x3}
  - nearest     Battle Tank: 530 -> 610  {+Autocannon x1, -Heavy Fusion Rifle x1, +Overrun x1, +Pintle Heavy Fusion Rifle x1, -Rapid Autocannon x1, +Sponson Heavy Machineguns x1, -Twin Heavy Machinegun x1}
  - nearest     APC: 290 -> 280  {+Burst Cyclic Rifle x1, -Heavy Fusion Rifle x1, +Overrun x1, +Pintle Heavy Fusion Rifle x1, -Storm Rifle x1}
- **2k - Blessed Sisters.fdgarmy**
  - nearest     Celestial High Sister: 105 -> 105  {+Burst Shotgun x1, -Master Shotgun x1}
  - translated  Warrior Sisters: 270 -> 265  {+Battle Rifle x8, -CCW x10, -Heavy Rifle x8, +Strike x10}
  - translated  Biker Fanatics: 205 -> 150  {+Assault Bike-Rifle x3, +Chain-Sword x3, -Heavy Chainsaw Sword x3, -Twin Heavy Rifle x3}
  - translated  Fanatic Sisters: 200 -> 150  {+Chain-Sword x10, -Chainsaw Sword x10}
  - nearest     Procession Altar: 260 -> 245  {+Overrun x1}
  - nearest     Celestial Warrior Sisters: 180 -> 195  {-CCW x3, +Fusion Rifle x3, -Heavy Fusion Rifle x3, +Strike x3}
  - nearest     Celestial High Sister: 105 -> 155  {-Master Heavy Rifle x1, +Plasma Rifle x1}
  - translated  Warrior Sisters: 260 -> 245  {+Battle Rifle x8, -CCW x10, -Heavy Rifle x8, +Strike x10}
  - nearest     Assault Walker: 325 -> 320  {+Dual Assault Heavy Flamers x1, +Dual Walker Fists x1, -Heavy Fist x2}
- **2k - Dark Elf Raiders - Transport.fdgarmy**
  - nearest     Light Skimmer: 275 -> 275  {+Dark-Energy Cannon x1, -Laser Cannon x1, +Overrun x1, +Twin Barb Rifle x1}
  - nearest     Light Skimmer: 235 -> 270  {-Destroyer Cannon x1, +Overrun x1, +Plas-Burster Cannon x1, +Twin Barb Rifle x1}
  - nearest     Light Skimmer: 205 -> 260  {+Dark-Energy Cannon x1, -Laser Cannon x1, +Overrun x1, +Twin Barb Rifle x1}
  - translated  Dark Lord: 55 -> 90  {-CCW x1, -Elite Barb Pistol x1, +Hand Weapon x1, +Haze-Launcher x1}
  - translated  Nightmares (Combined): 310 -> 170  {-Energy Sword x10, +Heavy Jagged Cleaver x10}
  - nearest     Dark Lord: 65 -> 110  {-Agony Whip x1, +Barb Pistol x1, +Charged Flail x1, -Elite Barb Pistol x1}
  - translated  Witches (Combined): 210 -> 200  {-CCW x10, +Hand Weapon x10}
  - nearest     Warriors (Combined): 225 -> 190  {-CCW x10, -Plas-Blaster x1, +Plas-Burster Rifle x2, -Scythe Launcher x1, +Strike x10}
  - nearest     Heavy Skimmer: 420 -> 450  {-Destroyer Cannon x3, +Overrun x1, +Plas-Burster Cannon x3}
- **2k - Dwarf Guilds - Ambush and Scout-Heavy.fdgarmy**
  - nearest     Miners (Combined): 280 -> 270  {-CCW x8, -Drill A-Platform x2, +Hand Weapon x8, -Iron Pistol x6, +Iron-Slug Pistol x6, +Magma Sword x2, +Ore-Shredder Pistol x2}
  - nearest     Sniper Team: 135 -> 100  {-CCW x3, +Strike x3}
  - nearest     Sniper Team: 135 -> 100  {-CCW x3, +Strike x3}
  - nearest     Jetpack Warriors: 115 -> 120  {-Bash x5, +Hand Weapon x5, +Pistol x5, -Twin Glove-Pistols x5}
  - nearest     Jetpack Warriors: 130 -> 140  {-CCW x2, +Magma Sword x4, -Plasma Sword x2}
  - nearest     Dwarf Champion: 70 -> 80  {+Burst Battle Rifle x1, -CCW x1, -Champion Iron Blaster x1, +Hand Weapon x1}
  - nearest     Dwarf Warriors (Combined): 170 -> 150  {-Auto H-Rifle x1, +Battle Rifle x10, -CCW x10, -Rifle x9, +Strike x10}
  - nearest     Battle Tank: 400 -> 380  {+Heavy Machinegun Array x1, +Overrun x1, -Quad Attack Machinegun x1, +Sponson Heavy Flamers x1, -Twin Heavy Flamer x1}
  - nearest     Dwarf Walker: 310 -> 370  {-Light Walker Fist x1, -Shock Gauntlet x1, +Walker Shock Fist x2}
  - translated  Iron Veterans: 115 -> 130  {-CCW x5, -Iron Shotgun x4, +Iron-Slug Carbine x4, +Strike x5}
- **2k - High Elf Fleets - Caster-Heavy.fdgarmy**
  - nearest     Elven Noble: 145 -> 150  {+Ancient Spear x1, -Energy Spear x1}
  - nearest     Revenants: 215 -> 185  {+Ancient Sword x4, -Energy Sword x4, -Mirror Scythe x1, +Revenant Scythe x1}
  - nearest     Elemental Strikers: 230 -> 270  {+Ancient Sword x3, -Energy Sword x3}
  - nearest     Elemental Strikers: 230 -> 280  {+Ancient Spear x3, -Energy Spear x3}
  - nearest     Great Elemental: 400 -> 455  {+Assault Shard Cannon x1, +Dual Arm-Shardguns x1, +Elemental Sword x2, -Great Elemental Sword x2, -Shardgun x2}
  - nearest     Elven Noble: 115 -> 250  {+Bike-Shardguns x1, +Burst Phaser Blasters x1, -CCW x1, +Hand Weapon x1, -Master Laser Blaster x1, -Mounted Shardguns x1}
  - nearest     Jetbike Protectors: 245 -> 290  {+Bike-Shardguns x3, -CCW x3, -Shard Cannon x3, -Shard Pistol x3, +Sniper Rifle x3, +Strike x3}
  - nearest     Retributors (Combined): 250 -> 300  {-CCW x10, -Shard Carbine x8, +Shard Rifle x10, +Strike x10, -Twin Shard Carbine x2}
  - translated  Scorchers: 150 -> 165  {-CCW x3, +Strike x3}
- **2k - Human Defense Force - Tough and Vehicle-Heavy.fdgarmy**
  - nearest     Battle Tank: 395 -> 450  {-Heavy Machinegun x1, +Hull Assault Heavy Machinegun x1, -Nova Cannon x1, +Overrun x1, +Sponson Heavy Machineguns x1, -Twin Heavy Machinegun x1, +Wrecker-Cannon x1}
  - nearest     Attack Vehicle: 300 -> 230  {+Disposable Guided Missile x1, -Heavy Fusion Rifle x1, +Hull Heavy Fusion Rifle x1, -Hunter Missiles x1, +Overrun x1}
  - nearest     Combat Walker: 265 -> 325  {-Energy Sword x1, +Walker Sword x1}
  - nearest     Light Walker: 205 -> 195  {+Autocannon x1, -Rapid HE Autocannon x1}
  - translated  Infantry Squad: 145 -> 140  {+Battle Rifle x8, -CCW x10, -Rifle x8, +Strike x10}
  - translated  Light APC: 270 -> 190  {-Heavy Flamer x1, +Overrun x1}
  - nearest     Company Leader: 35 -> 75  {+Burst Battle Rifle x1, -CCW x1, +Hand Weapon x1, -Master Rifle x1}
  - nearest     Support Vehicle: 350 -> 305  {-Heavy Machinegun x1, +Hull Heavy Machinegun x1, +Overrun x1}
- **2k - Orks - Horde Mixed.fdgarmy**
  - nearest     Veteran Leader: 65 -> 100  {-Heavy Claw x1, -Leader Pistol x1, +Marauder Claw x1, +Pistol x1}
  - nearest     Orc Warriors: 170 -> 250  {-CCW x8, +Hand Weapon x9, -Heavy Axe x1, -Heavy Claw x1, +Marauder Axe x1}
  - translated  Orc Warriors: 155 -> 245  {-CCW x10, +Strike x10}
  - translated  Berserkers (Combined): 170 -> 210  {-Berserker Claws x10, +Berserker Spear x10}
  - translated  Assault Buggy: 210 -> 285  {+Overrun x1}
  - nearest     Melee Walker -> Combat Walker: 285 -> 325  {-Walker Claw x2, +Walker Combat Claw x2}
  - translated  Jump Pack Orcs (Combined): 210 -> 240  {-CCW x10, +Strike x10}
  - translated  Berserker Beast Riders: 170 -> 165  {-Beast Jaws x3, -Berserker Claws x3, +Berserker Lance x3}
  - nearest     Orc Warriors: 175 -> 260  {-CCW x8, +Hand Weapon x9, -Heavy Claw x1, -Heavy Saw x1, +Marauder Saw x1}
  - nearest     Veteran Leader: 115 -> 200  {+Bike-Gun x1, -Heavy Claw x1, +Marauder Claw x1, -Twin Mini-MG x1}
  - nearest     Orc Bikers: 180 -> 150  {+Bike-Gun x3, -CCW x2, +Hand Weapon x3, -Heavy Axe x1, -Twin Mini-MG x3}
  - translated  Berserkers: 85 -> 80  {-Berserker Claws x5, +Berserker Spear x5}
- **2k - Robot Legions - Mixed.fdgarmy**
  - translated  Bot Swarms: 100 -> 125  {+Bot Claws x3, -Swarm Attacks x3}
  - translated  Bot Swarms: 100 -> 125  {+Bot Claws x3, -Swarm Attacks x3}
  - nearest     Warriors (Combined): 200 -> 170  {-CCW x10, -Gauss Rifle x10, +Ion Rifle x10, +Strike x10}
  - nearest     Eternals: 425 -> 135  {-CCW x5, -Heavy Gauss Rifle x5, +Photon Rifle x5, +Strike x5}
  - translated  Forge Spider: 195 -> 170  {+Radium Cannon x1, -Twin Atom-Beamer x1}
  - translated  Flesh-Eaters: 130 -> 100  {-Metal Claws x5, +Rusted Claws x5}
  - nearest     Robot Lord: 55 -> 120  {+Atomizer Sword x1, +Burst Photon Rifle x1, -Hyper Sword x1, -Lord Gauss Rifle x1}
- **3k - Battle Brothers.fdgarmy**
  - translated  Master Brother: 85 -> 100  {-CCW x1, +Hand Weapon x1}
  - translated  Battle Brothers: 370 -> 330  {-CCW x10, +Strike x10}
  - translated  Battle Brothers: 380 -> 360  {-CCW x10, +Strike x10}
  - translated  Assault Brothers: 400 -> 370  {-Energy Fist x2, +Energy Hammer x2, +Hand Weapon x8, -Heavy CCW x8}
  - nearest     Destroyers: 285 -> 275  {+Burst Cyclic Rifle x3, -CCW x3, +Hand Weapon x3, -Storm Rifle x3}
  - nearest     Battle Tank: 565 -> 675  {-Heavy Fusion Rifle x1, +Overrun x1, +Pintle Heavy Fusion Rifle x1, +Sponson Heavy Machineguns x1, -Twin Heavy Machinegun x1, +Twin Laser Cannon x1, -Twin Storm Cannon x1}
  - nearest     Veteran Combat Walker: 520 -> 490  {-Heavy Rifle Array x1, +Linked Heavy Rifle-Array x2, -Missile Array x1}
  - translated  Pathfinder Bikers: 220 -> 185  {+Assault Grenade Launcher x3, -CCW x3, -Grenade Launcher x3, +Hand Weapon x3, -Heavy Pistol x3}
- **3k - DAO Union.fdgarmy**
  - nearest     Battle Suit Elite: 255 -> 195  {-Bash x1, +Drone Guns x1, +Heavy Suit-Shredder Cannon x1, -Rapid Heavy Suit-Burst x1, -Rapid Suit-Burst x1, +Strike x1}
  - nearest     Battle Suits: 295 -> 240  {+Assault Suit-Shredder x1, -Bash x3, +Drone Guns x1, +Dual Suit-Shredders x3, +Strike x2, -Suit-Burst x5, -Suit-Frag x1, -Suit-Missiles x3}
  - nearest     Stealth Suits: 275 -> 175  {-CCW x3, -Rapid Burst Carbine x3, +Shredder Rifle x3, +Strike x3}
  - nearest     Heavy Battle Suit: 460 -> 405  {+Heavy Gauss Tandem-Rifle x1, +Suit-Hull Homing Missiles x1, -Twin Heavy Rail Rifle x1, -Twin Suit Missile Pod x1}
  - nearest     Spotter Squad -> Spotters: 210 -> 120  {-CCW x5, +Carbine x4, +Helix Tandem-Rifle x1, -Pulse Carbine x4, -Scoped Rifle x1, +Strike x5}
  - nearest     Spotter Squad -> Spotters: 210 -> 120  {-CCW x5, +Carbine x4, +Helix Tandem-Rifle x1, -Pulse Carbine x4, -Scoped Rifle x1, +Strike x5}
  - nearest     Tactical Grunts (Combined): 290 -> 220  {-CCW x10, +Carbine x10, -Pulse Carbine x10, +Sgt. Hand Weapon x2, +Strike x8}
  - nearest     Grunt Leader: 40 -> 55  {-Holy Baton x1, +Pistol x1, +Plasma Batons x1}
  - nearest     Security Grunts (Combined): 230 -> 250  {-CCW x10, -Pulse Shotgun x10, +Shotgun x10, +Strike x10}
  - nearest     Hover Tank: 515 -> 450  {+Helix Cannon x1, +Hull Homing Missiles x1, +Overrun x1, -Sub-Railgun x1, -Twin Heavy Smart Missiles x1}
  - translated  Tactical Grunts (Combined): 230 -> 200  {-CCW x10, +Long Rifle x10, -Pulse Rifle x10, +Strike x10}
- **3k - Dark Elf Raiders.fdgarmy**
  - nearest     Dark Lord: 80 -> 130  {+Barb Pistol x1, -Elite Barb Pistol x1, +Heavy Saw-Glaive x1, -Shell Sword x1}
  - translated  Nightmares (Combined): 310 -> 170  {-Energy Sword x10, +Heavy Jagged Cleaver x10}
  - nearest     Light Skimmer: 345 -> 275  {+Dark-Energy Cannon x1, -Laser Cannon x1, +Overrun x1, -Phantom-GL x1, +Twin Barb Rifle x1}
  - nearest     Light Skimmer: 345 -> 275  {+Dark-Energy Cannon x1, -Laser Cannon x1, +Overrun x1, -Phantom-GL x1, +Twin Barb Rifle x1}
  - nearest     Heavy Skimmer: 440 -> 445  {-Destroyer Cannon x3, +Disposable Haze-Launcher x1, +Overrun x1, -Phantom-GL x1, +Plas-Burster Cannon x3}
  - nearest     Heavy Skimmer: 440 -> 445  {-Destroyer Cannon x3, +Disposable Haze-Launcher x1, +Overrun x1, -Phantom-GL x1, +Plas-Burster Cannon x3}
  - nearest     Dark Lord: 95 -> 120  {+Haze-Launcher x1, +Heavy Saw-Glaive x1, -Phantom-GL x1, -Shell Sword x1}
  - nearest     Witches (Combined): 230 -> 240  {-CCW x8, -Dual Crystal Claws x2, +Dual Witch Claws x2, +Hand Weapon x8}
  - nearest     Leech Engine: 240 -> 180  {+Leech-Shot Blaster x1, -Soul Blaster x1}
  - nearest     Warriors (Combined): 210 -> 180  {-CCW x10, -Scrapper x2, +Strike x10, +Torrent Gun x2}
  - nearest     Jetbike Riders: 235 -> 225  {+Barb Bike-Rifle x3, -CCW x2, +Hand Weapon x2, -Heavy Fusion Gun x1, +Scythe-Hook x1, -Twin Barb Rifle x2}
- **3k - Eternal Dynasty.fdgarmy**
  - translated  Dynasty Leader: 90 -> 105  {-CCW x1, +Dual Ninja Sickles x1, -Dynasty Pistol x1}
  - translated  Warriors: 200 -> 250  {-CCW x10, +Strike x10}
  - translated  Dynasty Leader: 90 -> 105  {-CCW x1, +Dual Ninja Sickles x1, -Dynasty Pistol x1}
  - translated  Warriors: 210 -> 260  {-CCW x10, +Strike x10}
  - translated  ONIs: 195 -> 185  {-Bash x3, +Dual Flame-Fists x3, -Heavy Fist x6, +Strike x3}
  - translated  ONIs: 195 -> 185  {-Bash x3, +Dual Flame-Fists x3, -Heavy Fist x6, +Strike x3}
  - translated  Attack Drones: 240 -> 200  {+Drone Toxin Gun x10, -Laser Gun x10, +Strike x10, -Taser x10}
  - translated  Ninja Walker: 370 -> 270  {-Heavy Burst Auto-Gun x1, +Ninja Sword x1, -Rope-Sickle x1}
  - nearest     Dynasty Tank: 440 -> 470  {+Anti-Tank Cannon x1, -Heavy Anti-Tank Cannon x1, -Heavy Laser Gun x1, +Hull Laser Gun x1, +Overrun x1}
  - nearest     ONI Captain: 135 -> 180  {+Dual Plasma-Fists x1, -Heavy Bash x1, -Heavy Glaive x1, +Strike x1}
  - translated  Royal Guard: 375 -> 380  {+Royal Spear x10, -Spear x10, +Spear-Flame x10, -Spear-Shot x10}
- **3k - Goblin Reclaimers.fdgarmy**
  - translated  Shooter Mob (Combined): 190 -> 220  {-CCW x20, +Strike x20}
  - translated  Shooter Mob (Combined): 190 -> 220  {-CCW x20, +Strike x20}
  - translated  Shooter Mob (Combined): 190 -> 220  {-CCW x20, +Strike x20}
  - translated  Storm Mob (Combined): 220 -> 250  {-CCW x20, +Hand Weapon x20}
  - translated  Storm Mob (Combined): 220 -> 250  {-CCW x20, +Hand Weapon x20}
  - nearest     Scout Bikers: 100 -> 100  {-Bubble Pistol x1, -CCW x4, +Hand Weapon x4, +Pistol x1}
  - translated  Mega Walker: 405 -> 410  {-Bubble Cannon x1, +Death Saw x1, +Frag-Tosser Mortar x1, -Great Saw x1, -Lobber Mortar x1, +Plas-Blob Cannon x1}
  - translated  Mega Walker: 405 -> 410  {-Bubble Cannon x1, +Death Saw x1, +Frag-Tosser Mortar x1, -Great Saw x1, -Lobber Mortar x1, +Plas-Blob Cannon x1}
  - nearest     Super Tank: 505 -> 455  {+Atom-Smasher Cannon x1, -Heavy Bolt-Smasher Cannon x1, +Overrun x1, +Sponson Heavy Machineguns x1, -Sponson-MGs x1}
  - nearest     Artillery Gun: 160 -> 125  {-Boosted Chucker Cannon x1, +Chucker Mortar x1}
  - nearest     Boss Mob: 175 -> 125  {-Bolt-Smasher Rifle x1, -Bolt-Smasher-Mod x1, -Boss Carbine x1, +Boss Pistol x2, -CCW x5, +Hand Weapon x5}
  - translated  Killer Tanks: 225 -> 205  {-Crew x3, +Overrun x3}
- **3k - High Elf Fleets.fdgarmy**
  - nearest     Elven Noble: 160 -> 100  {+Burst Phaser Blasters x1, -CCW x1, +Hand Weapon x1, -Master Web Spinner x1}
  - nearest     Shifters (Combined): 550 -> 440  {-CCW x10, +Heavy Phaser Blaster x2, -Heavy Web Spinner x2, +Phaser Blaster x8, +Strike x10, -Web Spinner x8}
  - nearest     Elven Noble: 190 -> 155  {+Burst Phaser Blasters x1, -CCW x1, +Hand Weapon x1, -Master Web Spinner x1}
  - translated  Retributors (Combined): 235 -> 190  {+Ancient Sword x10, -Energy Sword x10}
  - nearest     Retributors (Combined): 430 -> 300  {-CCW x10, -Shard Carbine x8, +Shard Rifle x10, +Strike x10, -Twin Shard Carbine x2}
  - nearest     Retributors (Combined): 430 -> 300  {-CCW x10, -Shard Carbine x8, +Shard Rifle x10, +Strike x10, -Twin Shard Carbine x2}
  - nearest     Great Elemental: 405 -> 435  {+Assault Shard Cannon x1, +Dual Arm-Shardguns x1, +Elemental Spear x1, -Great Spear x1, -Shardgun x2}
  - nearest     Elemental Strikers: 275 -> 280  {+Ancient Spear x3, -Energy Spear x3}
  - nearest     Combat Walker: 270 -> 240  {+High-Energy Cannon x1, -Rapid Laser Cannon x1}
- **3k - Knight Brothers.fdgarmy**
  - nearest     Knight Veteran Master Brother: 155 -> 160  {+Burst Heavy Rifle x1, -CCW x1, +Hand Weapon x1, -Master Storm Rifle x1}
  - translated  Knight Battle Brothers: 245 -> 160  {-Assault Storm Rifle x5, -CCW x5, +Cyclic Rifle x5, +Strike x5}
  - nearest     Knight Master Brother: 165 -> 145  {+Assault Cyclic Rifle x1, -Master Storm Rifle x1}
  - nearest     Knight Destroyers: 405 -> 380  {+Burst Cyclic Rifle x2, -CCW x2, -Dual Energy Falchions x1, +Energy Sword x1, +Hand Weapon x2, -Rapid Psychic Cannon x1, +Slayer Blaze-Flamer x1, -Storm Rifle x2}
  - nearest     Knight Veteran Combat Walker: 510 -> 505  {+Heavy Flamer x1, +Light Walker Fist x2, -Twin Heavy Machinegun x1, -Walker Fist x1}
  - translated  Knight Battle Brothers (Combined): 530 -> 325  {-Assault Storm Rifle x10, -CCW x10, +Cyclic Rifle x10, +Strike x10}
  - nearest     Knight Light Gunship: 410 -> 285  {-Laser Talon x1, -Minigun x1, +Strafing Gatling Cannons x1, +Strafing Heavy Machineguns x1, +Strafing Laser Machinegun x1, -Twin Laser Cannon x1}
  - nearest     Knight Attack APC -> Knight Attack Vehicle: 320 -> 280  {+Overrun x1, +Plasma Carbine x1, -Twin Plasma Rifle x1}
  - nearest     Knight Battle Brothers: 255 -> 175  {+Assault Cyclic Rifle x5, -Assault Storm Rifle x4, -CCW x5, +Hand Weapon x5, -Psychic Cannon x1}
- **3k - Robot Legions.fdgarmy**
  - translated  Robot Lord: 85 -> 110  {-CCW x1, +Hand Weapon x1, +Wrist-Spark x1}
  - nearest     Warriors: 220 -> 170  {-CCW x10, -Gauss Rifle x10, +Ion Rifle x10, +Strike x10}
  - translated  Robot Lord: 85 -> 110  {-CCW x1, +Hand Weapon x1, +Wrist-Spark x1}
  - nearest     Warriors: 220 -> 170  {-CCW x10, -Gauss Rifle x10, +Ion Rifle x10, +Strike x10}
  - translated  Flesh-Eaters: 280 -> 200  {-Metal Claws x10, +Rusted Claws x10}
  - translated  Snipers: 110 -> 90  {-CCW x3, -D-Mark x3, +Reaper Sniper Rifle x3, +Strike x3}
  - nearest     Destroyer Snakes: 295 -> 240  {+Dual Ion Blades x2, -Dual Reaper Blades x2, +Heavy Ion Blade x1, -Heavy Reaper Blade x1}
  - nearest     Heavy Annihilator: 235 -> 215  {-Exterminator Cannon x1, -Heavy CCW x1, +Plasma Ray Cannon x1, +Strike x1}
  - translated  Bot Swarms: 100 -> 125  {+Bot Claws x3, -Swarm Attacks x3}
  - translated  Bot Swarms: 100 -> 125  {+Bot Claws x3, -Swarm Attacks x3}
  - translated  Bot Swarms: 100 -> 125  {+Bot Claws x3, -Swarm Attacks x3}
  - nearest     Doom Tank: 400 -> 415  {+Fusion Pulse Cannon x1, -Gauss Rifle Array x1, -Heavy Doom Cannon x1, +Overrun x1, +Photon Gun Array x1}
- **3k - Saurian Starhosts.fdgarmy**
  - translated  Frog-Mage: 210 -> 260  {+Great Magic Pulse x1, -Heavy Pulse x1, -Magic Shock x1}
  - translated  Saurian Warriors (Combined): 230 -> 240  {-CCW x10, +Strike x10}
  - nearest     Raptor Riders: 185 -> 400 [models 5 -> 6]  {+Pistol x1, +Power Lance x4, -Power Mace x1, -Power Spear x2}
  - nearest     Raptor Riders: 195 -> 400 [models 5 -> 6]  {+Pistol x3, +Power Lance x4, -Power Mace x1, -Power Spear x2, -Spike Pistol x2}
  - nearest     Ripjawdactyl Riders: 210 -> 220  {+Burst Shock Pistol x3, -Shock Pistol x3}
  - translated  Ankylosaurus: 445 -> 440  {+Heavy Beam Cannon x1, -Mace Tail x1, -Twin Beam Cannon x1}
  - translated  Triceratops: 310 -> 275  {+Heavy Horns x1, -Horns x1}
  - nearest     Saurian Guardians (Combined): 315 -> 240  {-Great Hammer x1, +Hammer x1}
  - translated  Gecko Snipers: 120 -> 100  {-CCW x3, +Strike x3}
  - translated  Saurian Warriors (Combined): 230 -> 240  {-CCW x10, +Strike x10}
  - translated  Vulturedactyls: 130 -> 95  {-Claws x3, +Jagged Claws x3}
- **3k - Soul-Snatcher Cults.fdgarmy**
  - translated  Minions: 140 -> 130  {+Battle Rifle x9, -CCW x10, -Rifle x9, +Strike x10}
  - translated  Minions: 140 -> 130  {+Battle Rifle x9, -CCW x10, -Rifle x9, +Strike x10}
  - translated  Minions: 140 -> 130  {+Battle Rifle x9, -CCW x10, -Rifle x9, +Strike x10}
  - nearest     Soul-Snatchers: 195 -> 135  {-Heavy Claws x5, +Razor Claws x5}
  - nearest     Soul-Snatchers: 195 -> 135  {-Heavy Claws x5, +Razor Claws x5}
  - nearest     Acolytes (Combined): 270 -> 250  {-Heavy Buzz Saw x2, -Mutant Claws x6, +Razor Claws x6, +Serrated Blade x2}
  - nearest     Acolytes (Combined): 270 -> 250  {-Heavy Buzz Saw x2, -Mutant Claws x6, +Razor Claws x6, +Serrated Blade x2}
  - translated  Neophyte Bikers (Combined): 300 -> 200 [models 6 -> 10]  {+Battle Rifle x8, -CCW x6, -Grenade Launcher x4, +Strike x10}
  - nearest     Light Walker: 180 -> 155  {+Heavy Machinegun x1, -Twin Heavy Machinegun x1}
  - nearest     Light Walker: 180 -> 155  {+Heavy Machinegun x1, -Twin Heavy Machinegun x1}
  - translated  Light APC: 215 -> 175  {-Heavy Flamer x2, +Overrun x1, +Turret Heavy Flamer x1}
  - translated  Light APC: 215 -> 175  {-Heavy Flamer x2, +Overrun x1, +Turret Heavy Flamer x1}
  - nearest     Battle Tank: 490 -> 445  {+Disposable Guided Missile x1, -Heavy Autocannon x1, -Hunter Missiles x1, +Overrun x1, +Sponson Heavy Fusion Rifles x1, +Twin Autocannon x1, -Twin Heavy Fusion Rifle x1}
- **3k - Titan Lords.fdgarmy**
  - nearest     Vassal Micro-Titan: 205 -> 190  {-Heavy Hammer x2, +Titan Hammer x2}
  - nearest     Vassal Micro-Titan: 205 -> 190  {-Heavy Hammer x2, +Titan Hammer x2}
  - nearest     Errant Mini-Titan: 330 -> 320  {-Heavy Fist x1, +Titan Fist x1}
  - nearest     Knight Titan: 890 -> 865  {+Assault Laser Machinegun x1, -Titan Pulse Laser x1}
  - nearest     Paladin Light Titan: 570 -> 545  {-Heavy Fist x2, +Heavy Titan Fist x2}
- **4k - Alien Hives.fdgarmy**
  - nearest     Hive Lord: 540 -> 480  {+Great Serrated Blade x1, -Heavy Serrated Blade x1, -Stomp x1}
  - nearest     Hive Lord: 540 -> 480  {+Great Serrated Blade x1, -Heavy Serrated Blade x1, -Stomp x1}
  - translated  Hive Titan: 895 -> 840  {+Heavy Smashing Tail x1, +Smashing Claws x1, -Titanic Heavy Claws x1}
  - translated  Invasion Carrier Spore: 135 -> 130  {+Bug-Spewer x1, -Razor Tendrils x1, +Stomp x1}
  - translated  Snatcher Lord: 115 -> 115  {-Heavy Claws x1, +Razor Claws x1}
  - translated  Snatcher Lord: 115 -> 115  {-Heavy Claws x1, +Razor Claws x1}
  - nearest     Ravenous Beasts: 250 -> 180  {+Dual Serrated Claws x3, -Razor Whip x3, -Serrated Blade x3}
  - nearest     Ravenous Beasts: 250 -> 180  {+Dual Serrated Claws x3, -Razor Whip x3, -Serrated Blade x3}
  - translated  Spores: 60 -> 60  {+Strike x5, -Tendrils x5}
  - translated  Spores: 60 -> 60  {+Strike x5, -Tendrils x5}
  - translated  Hive Guardians: 245 -> 165  {+Dual Smashing Clubs x3, -Serrated Blade x6}
  - nearest     Shooter Grunts: 140 -> 150  {+Claws x10, -Razor Claws x10}
  - SUBSTITUTED Winged Grunts -> Shooter Grunts: 155 -> 160  {+Claws x10, -Razor Claws x10}
- **4k - Havoc Brothers.fdgarmy**
  - translated  Havoc Brothers: 455 -> 415  {-CCW x10, +Strike x10}
  - translated  Havoc Brothers: 455 -> 415  {-CCW x10, +Strike x10}
  - nearest     Chosen Havoc Brothers: 300 -> 205  {-CCW x3, +Fusion Rifle x3, -Laser Cannon x3, +Strike x3}
  - nearest     Havoc Tank -> Havoc Battle Tank: 490 -> 445  {+Battle Cannon x1, -Demolition Cannon x1, +Overrun x1, +Sponson Heavy Machineguns x1, -Twin Heavy Machinegun x1}
  - nearest     Infernal Brute: 425 -> 465  {-Missile Array x1, -Super-Heavy Fusion Rifle x1, +Twin Heavy Fusion Rifle x2}
  - translated  Cultists: 160 -> 150  {+Battle Rifle x8, -CCW x10, -Rifle x8, +Strike x10}
  - translated  Cultists: 160 -> 150  {+Battle Rifle x8, -CCW x10, -Rifle x8, +Strike x10}
  - translated  Cultists: 160 -> 150  {+Battle Rifle x8, -CCW x10, -Rifle x8, +Strike x10}
  - nearest     Havoc Destroyer Champion: 220 -> 255  {+Burst Heavy Rifle x1, +Flamer-Mod x1, -Rapid Storm Rifle x1}
  - nearest     Havoc Destroyers: 275 -> 270  {+Assault Cyclic Rifle x2, -CCW x3, +Hand Weapon x3, +Heavy Flamer x1, -Storm Rifle x2, -Twin Heavy Flamer x1}
  - nearest     Despoiler Walker: 740 -> 760  {-Anti-Tank Cannon x1, +Assault Anti-Tank Cannon x1, +Brute Flail x2, +Dual Fusion Beams x1, -Dual Maul Cutters x1, -Hell Flail x2}
- **4k - High Elf Fleets.fdgarmy**
  - nearest     Anti-Gravity Tank: 660 -> 645  {+Heavy Focal Cannon x1, +Hull Shard Cannon x1, -Light Shard Cannon x1, +Overrun x1, -Prism Cannon x1}
  - nearest     Retributors: 380 -> 260  {-CCW x10, -Shard Carbine x8, +Shard Rifle x10, +Strike x10, -Twin Shard Carbine x2}
  - nearest     Retributors: 380 -> 260  {-CCW x10, -Shard Carbine x8, +Shard Rifle x10, +Strike x10, -Twin Shard Carbine x2}
  - nearest     Retributors: 380 -> 260  {-CCW x10, -Shard Carbine x8, +Shard Rifle x10, +Strike x10, -Twin Shard Carbine x2}
  - nearest     Shifters: 505 -> 440  {-CCW x9, +Dual Shifter Picks x1, +Phaser Blaster x10, +Strike x10, -Web Spinner x10}
  - nearest     Elemental Strikers: 245 -> 270  {+Ancient Sword x3, -Energy Sword x3}
  - nearest     Elemental Strikers: 245 -> 270  {+Ancient Sword x3, -Energy Sword x3}
  - nearest     Elven Noble: 145 -> 150  {+Ancient Spear x1, -Energy Spear x1}
  - nearest     Revenants: 210 -> 185  {+Ancient Sword x5, -Energy Sword x4, +Revenant Chakram x1, -Sword x1, -Tri-Chakram x1}

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
