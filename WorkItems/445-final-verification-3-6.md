# 445 — Final verification of the OPR 3.6 upgrade

**Status**: pass 1 done; pass 2 after sign-off changes (pass 1 2026-10-31: 14 green / 0 red / 4 amber)
**Related**: #415, #444 (merge happens only after this passes), #167 (scenario tools), #196 (`--rule-coverage`)
**Branch**: `feature/opr-3.6`. Model/effort: Opus 5.5 / medium.

## Goal

An evidence-backed statement that the feature branch is releasable, written into this file, before #444
merges. The checklist is plan section 7; this item runs it, records the outputs, and files follow-up items
for anything red instead of patching around it.

## Checklist (record each result with the command and its tail)

1. `dotnet test FutureOfDarkGrimness/FutureOfDarkGrimness.csproj` green; `dotnet build` clean (0 warnings
   introduced by the upgrade - compare against the master build's warning count).
2. Headless smokes exit 0: the default built-in army; `--army` with one GDF and one AoF 3.6 army produced by
   `--book-to-army` from a re-baked book; at least one book from each restructured family (a Prime Brothers
   book, a Disciples book, Infected Colonies, Rebel Guerrillas, Voidspace Stalkers, Chivalrous Kingdoms).
3. `FdgRaylib --rule-coverage FdgRaylib/Assets/Books` -> `Dead: 0`, or every remaining name appears in
   #436's deferred ledger with a reason and a player-visible "not implemented" diagnostic (#168).
4. `--validate-rules` passes on `GdfRuleSupplement.json`, `AofRuleSupplement.json`, and each
   `AofBookOverrides/*.json` (alone and merged).
5. Every bundled `.fdgbook` has `version` = `OPR v3.6.0`; counts 48 GDF + 40 AoF; no file for the 0-unit
   parent entries; the Random Army catalog (`armies/`) regenerated and non-empty for both systems (#395 is a
   separate pre-existing bug about where the catalog is scanned; do not fix it here, but note it).
6. `--import-army` with a fresh 3.6 Army Forge share link succeeds; with a 3.5 link it refuses with the
   message #437 specifies.
7. Each `Scenarios/*.json` compiles with `--make-scenario` and runs headless (`--scenario ... --headless`).
8. Legacy load: the pre-upgrade `armies/` files kept by #440 as fixtures load into a lobby and a headless
   game runs to completion with the Q5 notice logged.
9. GUI hand-verify list for Chris: one line per GUI-touching item (#417, #422, #423, #424, #425, #429, #441)
   naming the screen and what to click; collected from those items' "awaiting hand-verify" notes.
10. `docs/` numbers spot-check: open `docs/ResolverGuide.md` and `docs/EngineNotes.md` and confirm the lines
    #444 changed match the code (melee 3", Brace, Indirect, Scout, Transport, Hero morale).

## Rules for the agent

Plan section 1. This item changes no production code; a red step becomes a new numbered item (fetch
origin first) or a reopen of the responsible item, and this file records which.

## Pass 1 - 2026-10-31

Run on `feature/opr-3.6` in both repos at super `a9633b2` / engine `cbd4cf79` (branch == `origin/feature/opr-3.6`),
Opus 5.5 / medium. Binary: `./FdgRaylib/bin/Debug/net8.0/FdgRaylib` from the full rebuild in point 1. Scratch logs:
the session scratchpad `445/` folder (not kept). No production file, data, book, army or test was touched; the runs
left `git status` exactly as found.

**Verdicts.** Checklist: 8 GREEN / 0 RED / 2 AMBER. #440/#441 additions: 6 GREEN / 0 RED / 2 AMBER. Total **14 green
/ 0 red / 4 amber**; no new item filed.

| # | Check | Verdict |
|---|-------|---------|
| 1 | Engine + app suites, full build, warnings vs master | AMBER - all green; 208 = 208 warnings, but 5 new texts offset by 5 removed |
| 2 | Headless smokes (default, 6 restructured families + AoF Disciples) | GREEN |
| 3 | Shipped `--rule-coverage` | GREEN - Dead 0 / 30864 |
| 4 | `--validate-rules` | GREEN - merged OK 401; AofBookOverrides gone (#435 D11) |
| 5 | 88 books at `OPR v3.6.0`, 48 + 40, no parent files, catalog | GREEN |
| 6 | Share-link import 3.6 accept / 3.5 refuse | GREEN (3.5 by test; no live 3.5 link exists) |
| 7 | 28 scenarios `--make-scenario` + headless `--all-ai` | GREEN - 28/28 |
| 8 | Legacy 3.5 fixtures headless + Q5 notice | GREEN (the lobby half is in the point-9 list) |
| 9 | Consolidated GUI hand-verify list | AMBER - compiled below, all owed |
| 10 | Docs numbers vs code | GREEN |
| A1 | #440: `--retrofit-editable armies FdgLab/armies --dry-run` | GREEN - 0 / 40 / 0 |
| A2 | #440: all 40 catalog lists `--headless --army`, 0 `[rules]` | GREEN - 40/40 exit 0, 0 warnings |
| A3 | #440: 28-scenario run | GREEN (= point 7) |
| A4 | #440: `Legacy35ArmyTests` | GREEN - 12/12 |
| A5 | #441: `ArmyForge36Tests` (48 + 40, 3.6 names) | GREEN - 19/19 |
| A6 | Repo state: submodule pointer, master untouched, tags | GREEN |
| A7 | #440: Random Army lobby hand-verify (GDF + AoF) | AMBER - owed (in point 9) |
| A8 | #441: forks F1-F9 sign-off | AMBER - owed |

### 1. Suites, build, warnings - AMBER

- `dotnet test FutureOfDarkGrimness/FutureOfDarkGrimness.csproj` ->
  `Passed!  - Failed: 0, Passed: 4455, Skipped: 1, Total: 4456` (baseline 4455 / 1).
- `dotnet test FdgRaylib.Tests/FdgRaylib.Tests.csproj` -> `Passed!  - Failed: 0, Passed: 4036, Skipped: 0` (baseline 4036).
- `dotnet build --no-incremental fdg-raylib.sln` (all four projects) -> `208 Warning(s)`, `0 Error(s)`, exit 0.
- Master comparison: temporary `git worktree add --detach <scratch>/master-wt origin/master` (super `16ffcdc`, its
  submodule pointer engine `479888db`, `git -C <wt> submodule update --init`), same command -> `208 Warning(s)`,
  `0 Error(s)`; worktree removed (`git worktree remove --force`, `prune`; `git worktree list` shows only the main tree).
- **Delta 0 by count, but not by text.** Diffing the warnings per file + code + message (line numbers dropped):
  - removed (5): `StormStage.cs` CS8600, CS8602, CS8618 x3 (the file was deleted by #438);
  - introduced (5): `FdgRaylib/Rendering/LobbyScreen.cs(1301,46)` CS8602 (`_viewModel` is `ILobbyViewModel?`,
    dereferenced in `SetArmySource`; #430 `9df1281`, the only production one); `FutureOfDarkGrimness/Tests/
    BraceRuleIntegrationTests.cs(173,27)` and `(194,27)` CS1998 async method without await (#422 `4f23f9b2`);
    `FutureOfDarkGrimness/Tests/TacticianTargetChoiceTests.cs(276,25)` and `(311,25)` CS8602 (#442 `45b90523`,
    `d9b91aa5`).
  - Read: the "compare against the master count" test passes (208 = 208), the stricter "0 introduced" reading does
    not. All five are nullable / async-hygiene warnings with no behaviour impact. Follow-up (not filed as an item):
    a one-line null guard at `LobbyScreen.cs:1301` and the four test warnings can ride any sign-off change before
    pass 2, or #415's post-merge comment sweep - Chris's call.

### 2. Headless smokes - GREEN

- Default: `printf "2\n2\n" | FdgRaylib --headless` -> exit 0,
  `Game result: outcome=Win winner="Player 1" rounds=4 scores=[1, 0]`, 0 `[rules]` lines.
- Per family: `FdgRaylib --book-to-army FdgRaylib/Assets/Books/<book>.fdgbook <scratch>.fdgarmy`, then
  `FdgRaylib --headless --army <scratch>.fdgarmy --seed N`; `[rules]` = `grep -c '\[rules\]'` on the game log.

| Book | `--book-to-army` tail | Seed | Exit | Game result | `[rules]` |
|------|-----------------------|------|------|-------------|-----------|
| PrimeBrothers (GDF) | all 29 units compiled (7375 pts); wrote 4-unit army | 11 | 0 | Tie, rounds=4 | 0 |
| ChangeDisciples (GDF Disciples) | all 25 units compiled (5760 pts) | 12 | 0 | Win P2 (AI) [0, 2] | 0 |
| AoF-WarDisciples (AoF Disciples) | all 19 units compiled (2200 pts) | 13 | 0 | Tie | 0 |
| InfectedColonies | all 20 units compiled (2870 pts) | 14 | 0 | Win P2 (AI) [0, 1] | 0 |
| RebelGuerrillas | all 20 units compiled (2930 pts) | 15 | 0 | Tie | 0 |
| VoidspaceStalkers (new) | all 22 units compiled (3375 pts) | 16 | 0 | Tie | 0 |
| AoF-ChivalrousKingdoms (AoF) | all 16 units compiled (1595 pts) | 17 | 0 | Tie | 0 |

  PrimeBrothers and AoF-ChivalrousKingdoms double as the "one GDF and one AoF 3.6 army via `--book-to-army`" of plan
  section 7.1. No log contains "exception" / "unhandled".

### 3. Rule coverage - GREEN

`FdgRaylib --rule-coverage FdgRaylib/Assets/Books` -> exit 0, `Total references: 30864` / `Dead: 0` (baseline 30864 /
0). No deferred-ledger or #168 check needed.

### 4. `--validate-rules` - GREEN

- `AofBookOverrides/*.json` no longer exists (`ls FdgRaylib/Assets/Books/AofBookOverrides` -> No such file; deleted at
  #435 D11, every override moot). Stale copies survive only in gitignored `bin/Release/**` build output, not in
  `dist/`.
- `--validate-rules GdfRuleSupplement.json` -> `OK: 327 definitions, no problems.`
- `--validate-rules GdfRuleSupplement.json AofRuleSupplement.json` -> `OK: 401 definitions, no problems.` (baseline).
- `--validate-rules AofRuleSupplement.json` alone -> `4 problem(s) in 74 definitions.`: Great Banner -> Banner, Great
  Musician -> Musician, Piercing Fighting Mark -> Piercing Fighter, Piercing Charge Buff -> Piercing Charge "resolves
  neither in the core catalog nor in the supplement". All four names are defined in `GdfRuleSupplement.json`; the AoF
  file is a layer on the GDF file by design (#375 `RuleSupplementSet`; `scripts/bake-books.sh` bakes AoF books
  against GDF + AoF), so the merged run is the contract. (The master AoF file alone gives 44 problems under the
  current validator - it was never standalone.) Not a defect.

### 5. Books and catalog - GREEN

- `FdgRaylib/Assets/Books`: 88 `.fdgbook` = 48 GDF + 40 `AoF-*`; every `version` = `OPR v3.6.0` (88/88, read with
  `json.load`); no `Wormhole*` / `Rift*` file (the 0-unit parents); `VoidspaceStalkers.fdgbook`, `DaemonsofChange/
  Lust/Plague/War`, `SaurianStarhosts` present. `ArmyForge36Tests` pins the same (A5).
- Random Army catalog: `armies/GDF` 28 + `armies/AoF` 4 + `FdgLab/armies` 8 = 40 lists, every one stamped
  `"version": "OPR v3.6.0"`; non-empty for both systems. #395 (catalog scanned beside the executable, so Random Army
  is a no-op outside a dist build) is still open and untouched, as the checklist says. Cosmetic: #440's Notes say
  "all 33 `armies/` files"; git tracks 32 (`git ls-files armies | grep -c fdgarmy`, the same on origin/master) and
  the 40/40 retrofit total agrees with 32 + 8.

### 6. Share-link importer - GREEN

- 3.6: `FdgRaylib --import-army UGDhJMMH0QBP <scratch>/import-36.fdgarmy` -> exit 0, `points check: our Forge 2380
  pts vs Army Forge 2465 pts`, `Imported 'High Elf Fleets' (High Elf Fleets): 9 units, 2465 pts -> ...`; then
  `--headless --army <that file> --seed 5` -> exit 0, `Game result: outcome=Win winner="Player 2 (AI)" rounds=4`,
  0 `[rules]`. (The 2380 / 2465 gap is the #262 stale-"Replace X" residual #441 D4 explains in the import preview.)
- 3.5: no live 3.5 share link can be produced any more (every Army Forge book reports 3.6.0; #437 Notes). Evidence is
  the fixture test `OprListImporterTests.Import_A35Book_IsRefused_NamingBothVersions` (a 3.5.3 book -> message starts
  "Army book 'Legion Beta' is at OPR version 3.5.3; this game supports 3.6.x." and contains "has not been updated",
  ASCII-only): `dotnet test FutureOfDarkGrimness/... --filter OprListImporterTests` -> `Passed: 24, Failed: 0`.

### 7. Scenarios - GREEN

Per `Scenarios/*.json` (28): `FdgRaylib --make-scenario <json> <scratch>/<name>.fdgsave` (each printed "Compiled
scenario ..."; 28 saves written) then `FdgRaylib --headless --scenario <json> --all-ai`.

| Scenario | make | run | `[rules]` | Result |
|----------|------|-----|-----------|--------|
| 256-friendly-in-lane | 0 | 0 | 0 | Tie r4 |
| 332-match-already-decided | 0 | 0 | 0 | Win P1 r3 [2, 0] |
| 334-forced-charge-band | 0 | 0 | 0 | Tie r4 |
| 336-weapon-rules-showcase | 0 | 0 | 0 | Tie r4 |
| 337-shaken-picker | 0 | 0 | 0 | Win P1 r4 [1, 0] |
| 340-sniper-split-targets | 0 | 0 | 0 | Win P2 r3 [0, 1] |
| 363-wall-shadow-engage | 0 | 0 | 0 | Tie r4 |
| 377-spell-verify | 0 | 0 | 0 | Win P1 r4 [2, 0] |
| 401-deadly-clumps | 0 | 0 | 0 | Tie r4 |
| 401-deadly-clumps-singles | 0 | 0 | 0 | Tie r4 |
| 408-versatile-backout | 0 | 0 | 0 | Win P1 r3 [1, 0] |
| 425-artillery-hold | 0 | 0 | 0 | Tie r4 |
| 446-activate-in-dangerous | 0 | 0 | 0 | Tie r4 |
| crowded-2v2-3k | 0 | 0 | 0 | Win P1 + P2 r4 [2, 1, 0, 0] |
| example-shootout | 0 | 0 | 0 | Win P1 r4 [2, 0] |
| example-walled-advance | 0 | 0 | 0 | Tie r4 |
| instinctive-compelled-shoot | 0 | 0 | 0 | Tie r4 |
| limited-hold-fire | 0 | 0 | 0 | Win P2 r4 [0, 1] |
| limited-weapons-test | 0 | 0 | 0 | Win P1 r2 [1, 0] |
| p16-takedown-extra-attacks | 0 | 0 | 0 | Tie r4 |
| p19-coordinate | 0 | 0 | 0 | Tie r4 |
| pilein-wrap-demo | 0 | 0 | 0 | Tie r4 |
| regenerative-strength | 0 | 0 | 0 | Tie r4 |
| rout-spillout | 0 | 0 | 0 | Tie r4 |
| surprise-attack-ai-target | 0 | 0 | 0 | Tie r4 |
| surprise-attack-burst | 0 | 0 | 0 | Win P1 r4 [1, 0] |
| vengeance-marks-the-killer | 0 | 0 | 0 | Tie r4 |
| vinci-tech-gate | 0 | 0 | 0 | Win P2 r4 [0, 1] |

### 8. Legacy 3.5 fixtures - GREEN (headless half)

`FdgRaylib --headless --army "FdgRaylib.Tests/Fixtures/legacy-3.5/<file>" --seed 3`, all exit 0 with `Game result:`
(4 rounds). The Q5 amber line is logged once per seat (twice per log). The `[rules] Skipping unimplemented` names are
the deleted 3.5 names #440 recorded (Resistance, Piercing Assault, the catalog helper "+6\" range when shooting"),
each logged once per seat.

| Fixture | Q5 line | `[rules] Skipping unimplemented` |
|---------|---------|----------------------------------|
| 1k - Ogres | Army 'Ogres' was built for OPR v3.5.3; it plays with 3.6 rules | 'Resistance' on spell 'Battle Guts' |
| 2k - Dwarf Guilds - Ambush and Scout-Heavy | Army 'Dwarf Guilds' ... v3.5.3 ... | '+6" range when shooting' on spell 'Battle Rune' |
| 2k - Orks - Horde Mixed | Army 'Orc Marauders' ... v3.5.3 ... | 'Piercing Assault' on Berserker Beast Riders, Berserkers, Berserkers (Combined); 'Resistance' on spell 'Elder Protection' |
| 2k - Robot Legions - Mixed | Army 'Robot Legions' ... v3.5.3 ... | none |
| 3k - Saurian Starhost | Army 'Saurian Starhost' ... v3.5.3 ... | none |
| Wormhole Demons of Change 1k | Army 'Dynamic Demons' ... v3.5.3 ... | none |

"Loads into a lobby" is GUI: the last line of #441's checklist (point 9) covers it.

### 9. Consolidated GUI hand-verify list (Chris) - AMBER (owed, not yet done)

Compiled from the "awaiting hand-verify" notes of every non-archived upgrade item. One line per check: item -> screen
-> what to click -> what to see. Each item's own file has the long form (numbered sub-steps) under the named section;
tick here, and the item can then be archived per #444 D6. Build `feature/opr-3.6` and run
`./FdgRaylib/bin/Debug/net8.0/FdgRaylib` unless the line says otherwise. "Built-in army" = 5x Warriors (Vanguard,
Impact(2)) + 3x Heavy Gunners (Scout, Counter on the Fists), the CLI/lobby default.

In game, built-in army (one or two games cover all of these):
- [ ] **#417** (Notes "Awaiting GUI hand-verify") - Movement panel: drag a ghost toward an enemy -> the yellow charge
      line and "N can charge" appear within 3" (not 2"); end a move 2"-3" away -> action menu offers Charge AND Pass,
      no forced-charge highlight; end inside 1" -> "FORCED CHARGE", Pass greyed; end just past 3" -> Charge greyed
      "No enemies within the 3" melee range."; charge from ~2.5" -> defenders pile in up to 3" and everyone within 3"
      strikes.
- [ ] **#422** (Notes) - Charge fresh Heavy Gunners with fresh Warriors -> "Impact Hits" beat first, then the
      "counters the charge - strikes first!" banner and the Gunners' swings, then the Warriors'; charge with Warriors
      that already fought this round -> no Impact beat; charge Gunners that already fought -> no counter banner.
      Brace no longer needs a hand-edited file: Chivalrous Kingdoms (and many AoF books) carry Brace - charge such a
      unit with an Impact unit -> the Impact beat shows the reduced count and the spear row underlines "Brace".
      Combat Calculator, melee, charging, "attacker fatigued", an Impact unit -> no Impact row.
- [ ] **#447** (Notes "GUI hand-verify") - Charge with full-strength Warriors -> the "Impact Hits" beat throws 10
      dice, context "Warriors: 5 models x Impact(2)", Offense colour, panel wide enough; at 3 models -> 6 dice; vs a
      Brace unit -> "... - N Brace", and no beat at all when Brace cancels everything.
- [ ] **#418** (Status) - A fatigued unit in melee (strike back once, then fight again the same round) -> the dice
      panel's 6s do not ring as special-rule 6s; the token tooltip / log line says fatigued hits do not count as 6s.
- [ ] **#426** ("GUI hand-verify") - Deployment: the Heavy Gunners' Scout placement shows the blue band 9" past your
      zone and red 9" keep-out blobs around non-Scout enemies (none around an enemy Scout); a click in a blob is refused
      with the "Too close to a non-Scout enemy unit ..." line; with Scouts on both sides placements alternate; after
      deploying the Warriors, "Use Vanguard on X?" -> green 9" rings, red non-Vanguard blobs, Back leaves it where it
      was; with Ambush reserves on both sides (Forge list), round 2+ prompts alternate starting with the first activator.
- [ ] **#420** (Status) - Movement panel, drag a unit across Dangerous terrain -> the hint reads "the unit rolls N
      dice on commit (wounds to destroy it), each 1 deals 1 wound"; Terrain placement screen -> the Dangerous effect line
      says the same.

Scenarios (each line is one `--scenario` launch, slot 0 = you):
- [ ] **#446** ("GUI hand-verify") - `--scenario Scenarios/446-activate-in-dangerous.json`: activate Lava Squad ->
      "Dangerous Terrain" dice row "activated in Dangerous terrain - 5 dice (wounds to destroy)" before the action
      menu, no Back; move it out -> panel says no second test, no second dice row; Strider Beasts 6 dice; Flying
      Scouts none (Back offered); Edge Squad none on activation, an ordinary row when it moves through; the AI's Mire
      Raiders roll theirs.
- [ ] **#425** ("GUI hand-verify") - `--scenario Scenarios/425-artillery-hold.json`: Field Gun action menu -> Charge
      greyed "Cumbersome - may only use Hold or Advance actions.", Move subtext "Advance only (Cumbersome) ...", the
      forecast at the Enemy Battery nets +1 / -1; its move panel "advance up to 6" - no Rush ...", Shoot greyed after
      moving (also a 0" move); Mortar Team on a Hold is offered Hidden Grunts "ignores line of sight (Indirect)" with no
      cover and Wooded Grunts with cover, 5+ vs the Battery; after an Advance neither grunt unit is offered.
- [ ] **#429** ("GUI hand-verify") - `--scenario Scenarios/377-spell-verify.json` (Coven Magus) or any 3.6 caster:
      a spell without the LoS clause targets an enemy fully behind a Blocking piece and lands; a damage spell shows
      "(LoS)" and greys "(no target in line of sight)" when its only target is behind a wall; a caster with a Caster
      Staff reads "Roll needed: 3+ (base 4+, rules +1)", with one boost token "2+ (... self +1)", banner the same.

Forge lists in a game (build in the Army Forge, then Host -> lobby -> LAUNCH vs an AI):
- [ ] **#421** ("GUI hand-verify (Chris)") - A Tough(9)+ Hero alone within 6" of a worse-Quality friendly unit; break
      that unit (shoot it to half / lose a melee) -> morale panel threshold is the Hero's Quality with chip "Quality N+
      (Hero aura: <name>)" and the log line names him; Hero 7"+ away or Shaken -> no chip; a joined hero -> "(joined
      Hero: <name>)"; no-modifier test looks as before; the "Hero" hover shows the 3.6 text.
- [ ] **#423** (Outcome "GUI hand-verify") - An AoF Hand Weapon unit (Parry(1), e.g. Dwarves / High Elves warriors)
      charged by a Rending unit -> hit beat, then "Parry vs <weapon>" beat (6+ highlighted, "ignored K Rending hits, M
      base hits"), then a save beat "(after Parry)" with fewer dice than hits; an Impact charger -> "Parry vs Impact"
      before the Impact saves and a second pool for the swings; chips "5 models x Parry(1)", "8 hits -> 5 dice", "3 of
      10 Parry dice used this attack".
- [ ] **#424** ("Hand-verify (Chris, GUI)", also #441's list) - Battle Brothers Assault Brothers: shoot with the Heavy
      Pistols at ~9", then charge the same round -> the melee weapon pick offers the pistol's melee line and the table
      hover shows `9"/melee`; the Forge card lists each pistol once.
- [ ] **#433** (Outcome "GUI hand-verify list") - Daemons of Lust hero with a Lust Favour (Enhanced Lustbound) -> +2"/+4"
      move rings; an Enhanced Changebound unit shot at 8" -> -1 to hit on the forecast; an Enhanced Bloodborn unit's save
      beat shows "+1 (Enhanced Bloodborn)" on a successful 5.
- [ ] **#434** (Notes 2026-10-30 "GUI glance (Chris)") - Wolf Brothers / Wolf Prime Brothers Wolfborn (or a Bounding)
      unit: its activation opens the ordinary move tool with one ring of the rolled D3(+1)" (not the "Repositioning"
      panel); Cancel declines.

Army Forge / lobby / calculator screens:
- [ ] **#441** ("Hand-verify checklist (Chris)", 15 lines - work through that section as written): book dropdowns 48 /
      40 with the 3.6 names; Assault Brothers weapon table + the Plasma Pistol / Energy Hammer combo both orders; HDF
      Unique Hero (Rixxon twice) red + amber lines; Alien Hives Spawn hover; long hovers wrap (also at a larger UI
      scale); share link `UGDhJMMH0QBP` import (no "RULES NOT ENFORCED"; the grey stale-"Replace X" note under the
      2380-vs-2465 row); in-game #424 pistol melee line and #438 D5 Stinger Dagger Takedown pick; AoF 750 start, Dwarves
      Gamharl twice, Shadow Stalkers' 5 spells + amber Combat Possession line, Sky-City Dwarves "Loses 2 spell tokens",
      Army Builder AoF warnings, lobby amber Q5 line for a 3.5 fixture. Plus the F1-F9 fork sign-offs.
- [ ] **#430** ("Forge hand-verify (Chris)") - Forge GDF at 1000 with six squads -> amber "Too many units: 6 (max 5 at
      1000 pts; 1 per 200)." and the copies line; switch to AoF (1000 -> 750; caps 5 / 2); hover "pts limit" for the
      suggested sizes; a unit over 35% -> "is too large"; a 20+ model unit -> "Too many models/Tough"; lobby (host) Army
      Source AoF at 2000 -> 1500, back to All -> 2000, a typed 1750 survives the switch, hover for suggested sizes.
      (Item 5, Unique Hero, is covered by #441's Rixxon / Gamharl lines now the books are re-baked.)
- [ ] **#440** (Notes "Chris hand-verify") - Random Army in a GDF and an AoF lobby, a few re-rolls at 1000 and 2000: a
      list is picked every time and no amber legacy line appears for a catalog list (one does for a 3.5 file loaded by
      hand); reopen one rebuilt catalog list in the Forge (lossless). Use a dist build (`scripts/build-dist.sh linux`,
      run `dist/FdgRaylib-linux-x64/FdgRaylib`): outside a dist build Random Army is the known #395 no-op.
- [ ] **#442** (Notes "Awaiting GUI hand-verify") - Combat Calculator, melee, charging: the "Charged from (in), 0 = not
      set" field appears (whole inches, default 0) and only while charging; a Ravage / Regenerative Strength attacker
      shows its own "Ravage" row; the shooting checkbox reads "Attacker advanced (untick = Hold)".

### 10. Docs numbers vs code - GREEN

Lines #444 (`ea56978`) and the phase-2 items left in `docs/EngineNotes.md` / `docs/ResolverGuide.md`, against the
constants (`FutureOfDarkGrimness/Utilities/GameWideConstants.cs`, `TeleportStage.cs`, and the rule seams):

| Fact | Doc line | Code | Match |
|------|----------|------|-------|
| Melee 3" horizontal / 4" vertical | EngineNotes:62, ResolverGuide:261 | `MELEE_RANGE_INCHES_HORIZONTAL = 3`, `_VERTICAL = 4` | yes |
| Standoff 1", optional-charge band 1"-3" | ResolverGuide:261 | `ENEMY_STANDOFF_DISTANCE_INCHES = 1f` | yes |
| Coherency 1" / 9" | ResolverGuide:59, :252 | `MAX_MODEL_DISTANCE_FROM_ANY_... = 1`, `_ALL_... = 9` | yes |
| Difficult 6" cap | ResolverGuide:244 | `DIFFICULT_TERRAIN_MOVE_CAP_INCHES = 6f` | yes |
| Scout 9" band / over 9" keep-out; Ambush over 9" | EngineNotes:75, :74; ResolverGuide:251 | `DEPLOYMENT_DISTANCE_INCHES = 9` (stages read the request discs) | yes |
| Indirect ignores LoS only on a Hold | ResolverGuide:260, EngineNotes:28-29 | #425 Hold gate (`ActionRestrictionQueries`, sight profile with move state) | yes |
| Hero morale: Tough(9)+, 6", not Shaken | EngineNotes:31 | `HeroStatRules.MoraleAuraMinTough = 9`, `MoraleAuraRangeInches = 6f`; `MaxHeroToughForJoin = 6` | yes |
| Fatigued 6s not special | EngineNotes:67 | #418 `HitRollComplete` flag | yes |
| Brace | not stated in either doc | `CoreRuleCatalog.Brace` (3.6 text) | n/a - nothing to drift |
| Transport (contact) | not stated in either doc | `TransportUtilities.EmbarkContactDistanceInches = 1f` (the forgiving "contact" band), exit 6" | n/a |
| Teleport | not stated in either doc | `TELEPORT_RANGE_INCHES = 6f` flat vs 3.6's 3"/6" | known disagreement, already in #444 + release notes |

### #440 / #441 additions

- **A1** `FdgRaylib --retrofit-editable armies FdgLab/armies --dry-run` -> exit 0, `Retrofit complete: 0 converted, 40
  already editable, 0 left alone (of 40). [dry run - nothing written]`. GREEN.
- **A2** every `armies/**/*.fdgarmy` (32) and `FdgLab/armies/*.fdgarmy` (8): `FdgRaylib --headless --army <file>
  --seed <1..40>` -> 40/40 exit 0, 40/40 `Game result:`, 0 `[rules]` lines in all 40 logs. GREEN.
- **A3** = point 7 (28/28). GREEN.
- **A4** `dotnet test FdgRaylib.Tests/... --no-build --filter FullyQualifiedName~Legacy35ArmyTests` -> `Passed: 12,
  Failed: 0`. GREEN.
- **A5** `... --filter FullyQualifiedName~ArmyForge36Tests` -> `Passed: 19, Failed: 0`. GREEN.
- **A6** Repo state. `git ls-tree HEAD FutureOfDarkGrimness` -> `cbd4cf79a82c8a80f3022a6dfe067394c2e29712` =
  `git -C FutureOfDarkGrimness rev-parse HEAD`. Both repos on `feature/opr-3.6`, equal to `origin/feature/opr-3.6`
  before this commit. `master` untouched: super local `8e598a9` (ancestor of the branch), origin `16ffcdc` (3 commits
  the branch lacks - #448 / #449 filings, `docs/replay-plan.md`, `docs/menu-background/*`, WorkItems / index /
  Reconciliations: docs only, no code); engine local `457bb41d`, origin `479888db`, both ancestors of the branch.
  Tags: super `OPR_3_5_1`, `v0.2.0`, `v0.3.0` only; engine none. GREEN.
- **A7** Random Army hand-verify (#440): owed; in point 9. AMBER.
- **A8** #441 forks F1-F9: unsigned; `docs/release-notes-v0.4.0.md:92` still carries the "[Delete or update before
  release]" F1-F9 placeholder (#444 D4). AMBER.
- Benches (not re-run here, per brief): #442 / #191 Notes 2026-10-31, 2,000 pts on the #440-rebuilt 3.6 pool -
  Tactician vs solo-rules 90.3% (3200 games, 0 faults, 0 timeouts); Strategist net vs Tactician 75.6% vs hand leaf
  67.6% (256 games each). Strategist re-train stays the post-release follow-up (plan section 7).


### Reds and follow-ups

- **Reds: none.** No new item filed; no item needs to reopen.
- **Follow-up (AMBER, point 1):** 5 warnings new since master (net 0): `LobbyScreen.cs:1301` CS8602 (#430's
  `SetArmySource`), `BraceRuleIntegrationTests.cs:173, :194` CS1998, `TacticianTargetChoiceTests.cs:276, :311` CS8602.
  Fix with any sign-off change before pass 2 or in #415's post-merge sweep (Chris to pick); not filed as an item.
- **Owed by Chris (AMBER):** the point-9 list (18 lines; ticks let #444 archive the items per its D6), the #441 forks
  F1-F9, and the open owner-review D entries (plan section 6).
- **#444 before the merge:** delete or fill the F1-F9 placeholder at `docs/release-notes-v0.4.0.md:92` once the forks
  are answered. Plan section 7 exit criterion 5 (all of #417-#442 closed or hand-verify waived) is not met until the
  point-9 list is done; criterion 7 (merge + `v0.4.0` tag) is #444 step 8.
- **Pass 2:** after the sign-off changes land, re-run points 1-8 and A1-A6 on the final branch (the commands above are
  the script) and confirm the warning delta.
- **Noted, not actioned:** #395 still open (Random Army outside a dist build); #440's "33 `armies/` files" should read
  32; the `dist/FdgRaylib-linux-x64*` archive in the tree is the #444 local test build (`-dirty` stamp), not a
  release - releases come from CI.

### Releasability

On the scripted checks the branch is releasable: both suites green at the baselines, 0 build errors with the warning
count unchanged from master, shipped coverage Dead 0, both supplements valid merged, all 88 books at OPR v3.6.0,
every book family, catalog list, scenario and legacy fixture plays headless to a result with no `[rules]` warnings
beyond the expected legacy skips, the 3.6 share link imports and plays, and `master` and the tags are untouched.
**Releasable pending:** Chris's consolidated GUI hand-verify list above (point 9, incl. the #440 Random Army check),
his sign-off on #441 F1-F9 (and the release-notes placeholder that goes with it), the optional clean-up of the 5 new
warnings, a pass 2 of the scripted checks on the final branch, and #444 step 8 (merge, `v0.4.0` tag, CI release).

## Notes

- 2026-10-31: **pass 1 run** (section above). 14 green / 0 red / 4 amber; no item filed, none reopened.
  Ambers: 5 warnings new since master (net 0, 208 = 208), the consolidated GUI hand-verify list (point 9), the #440
  Random Army hand-verify, the #441 forks F1-F9. Pass 2 re-runs points 1-8 and A1-A6 after the sign-off changes.
- 2026-10-31 (from #441): checklist item 9's #441 line is the "Hand-verify checklist (Chris)" section of
  `WorkItems/441-army-forge-ui-3-6.md` (15 lines, one GDF and one AoF army, incl. the #424 Heavy Pistol shoot-then-charge
  and the #438 Stinger Dagger melee pick). #441 also leaves forks F1-F9 for Chris; any he signs off before the release
  is built under #441, the rest stay open past it. Item 5's counts are pinned by `ArmyForge36Tests` (48 + 40, 3.6
  names, all `OPR v3.6.0`).
- 2026-10-31 (from #440): add to the checklist - `FdgRaylib --retrofit-editable armies FdgLab/armies --dry-run` (0
  converted, 40 already editable, 0 left alone); every `armies/` and `FdgLab/armies` list playing `--headless --army`
  with 0 `[rules]` warnings; the 28-scenario headless run (`--headless --scenario <json> --all-ai`, table in #440);
  `Legacy35ArmyTests` green; and Chris's Random Army hand-verify in a GDF and an AoF lobby (the lobby should show no
  amber legacy line for any catalog list, and one for a 3.5 file loaded by hand).
- 2026-10-29: filed with the plan.

## Decisions

(none yet)

## Outcome

(open) Pass 1 done 2026-10-31: scripted checks green, no reds; releasable pending the point-9 hand-verify
list, the #441 F1-F9 sign-off, pass 2 and #444 step 8 (see "Releasability").
