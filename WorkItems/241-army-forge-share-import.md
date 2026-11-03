# 241 — Army Forge share-link importer

**Status**: in-progress
**Related**: #218 (bypassed by design), #242 (campaign features, split from this), #156, #167 (reconciliation report)

## Goal
Paste an Army Forge share link (`https://army-forge.onepagerules.com/share?id=...`) into the game and get a playable `.fdgarmy` in a few clicks: fetch OPR's resolved list JSON, gate on army-book version (3.5.x) and game system (`gf`), map to a plain `ArmyListFile`, preview with warnings (inert rules, force-org errors, ignored campaign features), save. Engine importer (`OprListImporter`) + app fetcher/UI (Army Forge screen) + `--import-army` CLI flag for headless use.

## Notes
- 2026-07-19: **Points model was wrong since v1 - imported armies were LIGHT.** Testing a second share
  link (High Elf Fleets, `Al-PoLsC77I8`) showed our Forge at 845 pts vs a reported 835, with a phantom
  "Elven Noble 55 vs 45" delta. Both numbers were wrong. A share list's per-unit `cost` is the unit's
  BASE cost, not the resolved cost with upgrades; the only resolved total is the top-level `listPoints`
  (985 here). Confirmed independently of our data: OPR's own book API lists the Noble at 45, the same
  figure the list repeats for a Noble carrying a 10-pt Energy Sword, and the six unit costs sum 150 short
  of `listPoints`. So v1/v2's "Save As is the exact-points artifact" decision was false - imports came in
  light by every upgrade point, and force-org validation could pass an over-limit list as legal.
  The remainder is **unattributable by construction**: OPR omits the `cost` key entirely on options it
  prices in its internal algorithm (absent, not 0 - it writes an explicit 0 for genuinely free options),
  on BOTH the list and book endpoints. No amount of parsing recovers a per-unit split. Engine commit
  00b9c5b: `UpgradeOption.CostUnpriced`, `ArmyListFile.UnattributedPoints` (fed from `listPoints` so
  TotalPoints matches Army Forge), reconciliation moved to BASE-vs-BASE (the old compiled-vs-base
  comparison flagged every upgraded unit as a phantom delta), unpriced-upgrade count + excluded-unit
  caveat surfaced in the modal and `--import-army`. Fixtures had encoded the old assumption and were
  corrected. 1719 engine green, app green, smoke green; the link re-imports at 985 pts and plays a full
  headless game. Unverified corner CLOSED: `selectedUpgrades` shape confirmed, with the caveat that
  `option.cost` is frequently absent and must never be trusted. New inert rule seen: Piercing Spotter
  (#196/#197). Bundled books predate the flag - see #219.
- 2026-07-16 (v2): "Open in Forge" + pricing reconciliation + unit exclusion (design discussion same day; Save As kept alongside). Engine: `OprListImporter.ReconstructSelections(listJson, book)` rebuilds a `BuilderList` against the bundled book (unit match by preserved OPR ids; combined/join links; defensive selectedUpgrades ladder: section id -> unique option id -> unique option label), excludes unknown units (name + cost reported so the user can pad), and reconciles per-unit/total points via `ListCompiler` (`OprForgeSessionResult`) - every delta is a live #218/#219 repro. App: service exposes `ForgeSession`/`BundledBook`; the modal shows the points check + exclusions/deltas and gains "Open in Forge" (inline replace-list confirm; adopts via `AdoptLoaded(Compile(...))`); `--import-army` prints the reconciliation. Live check on the example link: "points check: OK (1100 pts both ways)". 5 new engine tests (1686 green), app 376 green, smoke green. Renumber: filed as #239/#240, renumbered #241/#242 pre-push (Reconciliations.md, reconciliation 13); the rebase also added weapon effect-set stamping to imports (`WeaponEffectAssigner.ApplyToArmy`).
- 2026-07-16 (later): v1 implemented and verified. Engine: `OprListImporter` (Import/Peek/AttachBookDefinitions/UnresolvedRuleNames, `SupportedVersionPrefix = "3.5"`, typed `OprVersionMismatchException`) + 12 tests, submodule commit 1ed69a7; full engine suite 1661 green. App: `ArmyForgeShareService` (link parse, /api/tts + /api/army-books fetch, bundled-book match by faction name), `--import-army <link> <out>` CLI flag, "Import Link" button + preview modal on the Forge screen. E2E verified against the real share link: 5 units (combined pair merged, hero joined), 1100 pts exact, faction book matched, ZERO inert rules (bundled book defs cover Havocbound etc.), force-org errors surfaced; imported file then played a full 4-round headless game (exit 0). Baseline headless smoke also green. REMAINING: GUI modal awaits hand-verify; fixture lists from user for the unverified corners below.
- 2026-07-16: Started. API probing (example list `iaP7jaKVjbUD`, "Havoc Brothers", 1100/1000 pts):
  - `GET /api/tts?id=<shareId>` returns the RESOLVED list: per-unit quality/defense/cost/size, `loadout` (final effective weapons, counts are unit totals), rules with ratings, `bases`, `joinToUnit`/`selectionId` (hero joins), `combined` twin entries, full `specialRules` text catalog, `forceOrgErrors`, campaign fields (`campaignMode`, `xp`, `traits`). NO version field.
  - `GET /api/army-books/{armyId}?gameSystem=gf` returns the book with `versionString` ("3.5.3"). Accepts the string `gf` (no numeric mapping needed). Every unit carries `armyId`.
  - Bad share id: HTTP 500, empty body.
- Unverified corners — need real share links with these features to pin down (then turn into fixtures):
  - `selectedUpgrades` gains shape (importer parses defensively: `option.gains[]`, dedupe-merges rule gains, trusts `loadout` for weapons).
  - `attacksMultiplier` semantics (importer warns when != 1 and leaves attacks as-is).
  - Whether `loadout` duplicates unit-level `items` (importer uses `loadout` as the sole gear source when present).
  - Whether the TTS export carries spell lists for Caster armies (importer recovers spells from the matched bundled book instead).

## Decisions
- Import maps the RESOLVED list (`loadout`) directly to `ArmyListFile` — never through `ListCompiler` — so costs and loadouts are verbatim Army Forge, and #218's Replace-All cost bug cannot affect imports. Design sign-off 2026-07-16: UI on the Army Forge screen; version gate blocks on major.minor mismatch (3.5.x accepted — OPR's own live books mix 3.5.2/3.5.3); importer core lives in the engine submodule (authorized).
- Faction-rule fidelity: match a bundled `.fdgbook` by name and copy `RuleDefinitions` + `Spells` onto the army (mirrors `ListCompiler.Compile`). No bundled match -> rules ride as name references, engine warn-and-skips (inert), preview discloses which.
- Version gate keys off the CURRENT army-book version (the list JSON carries none): a stale list whose book is still 3.5.x imports as stored; an OPR version bump produces a typed `OprVersionMismatchException`.
- Imported armies are plain `ArmyListFile`s: playable everywhere, editable in the freeform Army Builder, but not re-openable in the in-app Forge (no embedded book/selections).
- **Superseded 2026-07-19** (was: "Save As is the exact-points artifact"): it never was. Save As now
  totals correctly via `listPoints`, but its per-unit costs are BASE costs and the upgrade remainder sits
  in `UnattributedPoints` - the army's total is right, the per-unit attribution is not, and cannot be
  until OPR publishes per-option prices. Disclosed on import rather than papered over.
- v2 keeps BOTH exits deliberately: Save As is the exact-points artifact (works with no bundled book); Open in Forge goes through OUR compiler for editability, and the difference between the two is surfaced as the points reconciliation rather than hidden - that asymmetry IS the validation feature.

## Outcome
(open)
