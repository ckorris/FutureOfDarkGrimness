# 378 — Age of Fantasy armies in the Army Forge

**Status**: implemented 2026-08-23 - awaiting GUI hand-verify (checklist in the 2026-08-23 wrap note)
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

## Slice plan (2026-08-23)

1. [x] Engine: `BookFile.GameSystem` + `ArmyListFile.GameSystem` (nullable slug; **absent = GDF**,
   the owner's versioning ruling). Importer stamps from OPR `gameSystemSlug`; ListCompiler copies
   book -> army; OprListImporter's share-list gate widens from gf-only to gf/aof and stamps the army.
   New `GameSystems` (slugs + Normalize/SameSystem). Engine `1ac7b2c`, suite 3038 green.
2. [x] Engine: `WeaponEffectAssigner` system-aware - AoF faction-defaults table (40 entries, from a
   corpus weapon survey) + minted keys (ranged: arrow-loose, crossbow-bolt, sling-stone, thrown-spear,
   ballista-bolt, breath-flame, arcane-bolt; melee: great-weapon-smash, spectral-touch, beast-maw) +
   AoF keyword tables (separate vocabulary - "Bolt Thrower"/"Chain-Sword" must not read as GDF tech).
   Engine `aae7f9e`, suite 3051 green. Key assignments recorded for #379's visuals.
3. [x] App: `ArmyForgeBookService` slug/id per book (grimdark-future=2, age-of-fantasy=4; per-system
   index fetched lazily in --import-book/--import-section-shapes); system-gated matching in
   ArmyForgeShareService.MatchBundledBook, BundledBookRulebook (cache keyed by system|faction),
   --retrofit-editable. `ICurrentRulebook.DefinitionsForFaction` gained the gameSystem param (engine
   `2496790`). App 1372 + engine 3051 green, headless smoke exit 0.
4. [x] Data: all 40 books rebaked from the pinned snapshots via new `scripts/bake-aof-books.sh`
   (GDF+AoF supplements, per-book `AofBookOverrides/<Book>.json` LAST - #375 C9) and bundled as
   `Assets/Books/AoF-*.fdgbook` with gameSystem + AoF effect sets stamped. Census over all 87 books:
   22,079 refs, dead = exactly #381's 14 Retreating Strike (UNIT-attached in AoF Dark Elves, not
   spell-granted as assumed - spell coverage stayed allowlist-empty). New `BookRuleCensusTests` is
   the #375-promised census pin (per-book zero-dead + Retreating Strike allowlist + stale guard).
   The 17 GDF ShippedData census fixtures now enumerate `ShippedBooks.GdfPaths()` (their counts pin
   the 47-book GDF corpus; AoF is pinned by the all-books fixtures). BundledBookRulebook collision
   test added (Musician as the AoF-only discriminator - both books define Changebound, a GDF-origin
   name). GDF books untouched (0 modified). App 1541 + engine 3051 green; headless smokes with
   compiled Wood Elves + AoF Change Disciples armies exit 0, fantasy keys live in the army files.
5. [x] GUI: Forge screen game-system filter combo (GDF | AoF) gating the book dropdown (hidden for a
   single-system library; switching gets the same clear-list confirm as a book switch; AdoptLoaded
   matches by name AND system and flips the filter). Lobby: `ArmyListSummary` gained the GameSystem
   slug (engine `443a0aa`), host stamps it, LobbyScreen shows a yellow mixed-system note (warn,
   never block; absent field = GDF). App 1547 + engine 3051 green, smoke exit 0. **GUI hand-verify
   still owed** (combo, switch confirm, AoF roster/spells/tooltips, lobby note).
6. [x] Docs/ledgers: bake recipe = `scripts/bake-aof-books.sh` (self-documenting); #259 honesty holds
   by construction (RuleGlossary is book-driven - AoF books embed their defs, an undefined name gets
   the "not enforced" tooltip; share-import InertRules resolves against the system-matched book);
   #379 handed the minted keys + assignment tables (dated note there); #381 told its refs are now
   shipped + allowlist-pinned (dated note there); index line updated.

## Notes

- 2026-08-23 (wrap): All six slices done same session (engine `1ac7b2c`/`aae7f9e`/`2496790`/`443a0aa`,
  superproject through the slice-6 commit). Full verification: engine 3051 + app 1547 green, census
  22,079 refs / dead = exactly #381's 14, GDF books byte-untouched, headless smokes (default GDF +
  compiled AoF Wood Elves + AoF Change Disciples) all exit 0. **GUI hand-verify checklist**: (1) Forge
  system combo shows GDF/AoF, defaults GDF, switch with a non-empty list raises the confirm; (2) AoF
  book roster/spells/upgrade editing + #259 hover tooltips (an AoF-renamed rule shows its AoF text;
  Retreating Strike on Dark Elves shows "not enforced"); (3) save an AoF army, reload it - the combo
  flips to AoF and picks the right Disciples book; (4) lobby with one GDF + one AoF army shows the
  yellow mixed-system note and still launches; (5) an AoF battle plays (weapons draw as global-default
  tracers/blades until #379 - expected).

- 2026-08-23: Work started. Verified on-disk state: 40 AoF snapshots pinned (36 at v3.5.3, the four
  Giant Tribes Disciples variants at v3.5.2; fetched 2026-08-22, the corpus #375-#377 verified
  against - do NOT refetch, that would invalidate the 240/240 spell parity + census results).
  `fdgbooks-aofbaked/` exists from #377's full regen (spells restamped, costs/per-model shapes
  correct at import - no #219/#383 live passes needed for fresh imports). Remaining dead refs in
  the AoF corpus = exactly #381's 14 Retreating Strike spell refs. Found live: the four AoF
  Disciples books got GDF effect sets stamped (name-keyed FactionDefaultsTable) - the collision the
  GameSystem field fixes. Share-link import is hard-gated to game system "gf"
  (OprListImporter.SupportedGameSystem) - widening it is part of slice 1.
- 2026-08-22: Filed. 40 AoF PDFs on disk; rules/spells appraisal in #375-#377.

## Decisions

- 2026-08-23 (owner sign-off on the filed forks):
  - **Picker UX: system filter combo** ("Grimdark Future | Age of Fantasy") next to the Forge book
    dropdown; the book list shows only the selected system. Defaults to GDF.
  - **Book identity: `GameSystem` slug field on BookFile** (engine), stamped at import; the 47 GDF
    books retrofitted to carry "grimdark-future". AoF bundle filenames prefixed `AoF-`; book
    Name/Faction stay OPR's real names. Name-keyed matchers become system-aware.
  - **GDF/AoF in one lobby: WARN, don't block** - and for versioning's sake an army with no
    GameSystem field is assumed GDF everywhere.
  - **Effect keys: mint the #379 fantasy vocabulary now** (arrow-loose, crossbow-bolt, sling-stone,
    thrown-spear, ballista-bolt, breath-flame, arcane-bolt + melee gaps as needed); #378 assigns
    per-faction defaults, #379 implements visuals/sounds. Until #379, unknown keys draw as the
    global defaults - accepted.
  - **Snapshot version: pin the 2026-08-22 fetch** (v3.5.3 / v3.5.2 as above), recorded rather than
    refetched.

## Outcome
