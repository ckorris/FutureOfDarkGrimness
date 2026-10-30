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
