# 241 — Army Forge share-link importer

**Status**: in-progress
**Related**: #218 (bypassed by design), #242 (campaign features, split from this), #156, #167 (reconciliation report)

## Goal
Paste an Army Forge share link (`https://army-forge.onepagerules.com/share?id=...`) into the game and get a playable `.fdgarmy` in a few clicks: fetch OPR's resolved list JSON, gate on army-book version (3.5.x) and game system (`gf`), map to a plain `ArmyListFile`, preview with warnings (inert rules, force-org errors, ignored campaign features), save. Engine importer (`OprListImporter`) + app fetcher/UI (Army Forge screen) + `--import-army` CLI flag for headless use.

## Notes
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

## Outcome
(open)
