# 219 — Audit Army Forge upgrades for missing point costs

**Status**: todo
**Related**: #156 (Army Forge builder), #218 (adjacent Replace-All cost bug), `OprBookImporter.cs`, `FdgRaylib/Assets/Books/*.fdgbook`

## Goal
User has spotted multiple upgrade options in-app that should cost points but show/charge 0. Scope: audit the bundled `.fdgbook` catalog (and the `OprBookImporter` mapping that produced it) for options with a missing or zero `Cost` where the source OPR data has a nonzero price, and fix the importer/data. Done = a sweep across all bundled books turns up the offending options, root cause identified (importer mapping gap vs. source data vs. compiler), and costs corrected.

## Notes
- 2026-07-22: **Premise correction + Slice 1 shipped.** Signed-off plan: (decision) warn on lists
  carrying unpriced upgrades (not the "+? pts" disclosure, not a hard block); (decision) light up the
  bundled catalog via a surgical flag-transfer, NOT a destructive raw re-import.
  - **The 2026-07-19 "prices not recoverable" note was diagnosed off the SHARE endpoint (`/api/tts`).**
    The bundled catalog is built from a DIFFERENT endpoint, `/api/army-books/{uid}`, which I checked
    directly (Alien Hives, uid `w7qor7b2kuifcyvk`, gameSystem=2): of 152 options, 71 carry a real
    nonzero `cost` and 81 omit the key (0 explicit zeros). So the book endpoint DOES distinguish
    priced vs unpriced - and the priced ones already imported correctly (bundled Serrated Blade=15,
    Smashing Club=5, matching the endpoint). The hidden numbers on the 81 stay unrecoverable; the
    priced/unpriced DISTINCTION is fully recoverable. That is what Slice 2 captures.
  - **Why the catalog looks already-flagged but isn't:** every bundled `.fdgbook` now carries a
    `costUnpriced` field on every option (present via `has_field`), but `costUnpriced=true` count is
    ZERO across all 47 books. The field was stamped `false` by a later re-serialize pass (effect-sets
    #239 / rules #153), NOT by a real re-import - so the distinction was never actually captured. An
    unpriced option (e.g. bundled `Razor Whip (A3, Bane, Precise)`) sits at `cost:0, costUnpriced:false`,
    indistinguishable from free. THAT is the user's "should cost points, shows 0".
  - **Slice 1 DONE (engine 3651174 / bump 0b72cca):** `ListValidator` emits a per-unit Warning listing
    any selected option with `CostUnpriced`, "...count as free...; the list total may be under the true
    value." Warning not hard-block, matching #003 amber. +2 engine tests. Surfaces automatically via the
    Forge screen's generic issue renderer (yellow line + toolbar count). Fires on share-imports now; goes
    live on the bundled catalog after Slice 2. Engine 1806/1806, app build clean.
  - **Deferred (not cut):** the Forge picker still shows an unpriced option's label with no cost hint
    (line ~996). Left as-is on purpose - the chosen policy is warn-on-list, not the "+? pts" per-option
    disclosure. Revisit if the warning alone reads as confusing.
  - **Slice 2 DONE (app-side only, no engine change).** `--import-book <book.fdgbook | dir>` in Program.cs +
    `FdgRaylib/Import/ArmyForgeBookService.cs` (fetch official GF book index name->uid; fetch
    `/api/army-books/{uid}?gameSystem=2`; pure `RefreshCostFlags` re-imports to a throwaway book and copies
    ONLY `costUnpriced` onto the existing book by option Id). All 47 bundled books are Grimdark Future
    (gameSystem=2) - verified every name resolves in the GF official list, so uid discovery is one fetch.
    Ran across the catalog: **47/47 refreshed, 3517 options flagged unpriced, 0 unmatched, 0 cleared, 0
    base-cost deltas** (books are current on published prices). Catalog diff is nothing but
    `costUnpriced: false -> true` flips (7034 changed lines, added==removed) - effect sets #239 / embedded
    rules #153 / base retrofits #225 all untouched. +3 app tests (`ArmyForgeBookServiceTests`: flag-only,
    unmatched-untouched, stale-true-cleared). Build 0 err, app suite 396/396, engine 1806/1806, headless
    smoke exit 0.
  - Note: the tool is idempotent (a second `--import-book` on an already-flagged book reports 0 flagged).
    All books are GF; if a future book is Age of Fantasy, `ArmyForgeBookService` needs the AoF slug/gameSystem.
- 2026-07-19: **Root cause found, and it is not a data-entry gap.** Surfaced while importing a High Elf
  Fleets share link (#241). OPR's book JSON omits the `cost` key ENTIRELY on options it prices inside its
  own points algorithm, and writes an explicit `"cost": 0` only for genuinely free ones (both shapes
  appear in `HighElfFleets`; 8 explicit zeros alongside the absent ones). `OprBookImporter`'s DTO had
  `public int Cost`, so an absent key deserialized to 0 and the two cases became indistinguishable -
  every unpriced upgrade imported as free. Fixed in engine 00b9c5b: DTO is `int?`, and
  `UpgradeOption.CostUnpriced` records the distinction.
  **The real prices are NOT recoverable.** They appear on neither the list nor the book endpoint - only
  in aggregate, as a list's `listPoints`. On the example list that is 140 pts spread across 6 Energy
  Sword swaps, 2 Psy-Markers, an Elemental Hexer and a Shield Projector. So this item can no longer be
  "correct the costs" from OPR data; see the fork below.
- **REMAINING (deferred 2026-07-19, not silently cut):** the bundled `.fdgbook` snapshots were generated
  by the old importer and record unpriced options as a plain `0` with no flag, so the new disclosure stays
  dark on them. They must be re-imported from OPR to populate `CostUnpriced` (~30 books; needs network +
  a re-import path - there is no `--import-book` CLI flag today, unlike `--import-army`).
- 2026-07-15: Filed from user playtest feedback. No specific offending upgrades listed yet — first step is to reproduce/enumerate.

## Decisions
- 2026-07-19: "Free" and "unpriced" are now distinct states rather than both being 0. We charge 0 for
  unpriced options (no number exists to charge) but disclose them, so a total that falls short of Army
  Forge's reads as a known data limit and not as a #218-class arithmetic bug.

## Open fork (needs sign-off)
Since OPR never publishes these prices, an in-app Forge list containing unpriced upgrades cannot be
costed exactly. Options: (a) show them as "+? pts" and mark the list's total approximate; (b) hand-author
prices in `GdfRuleSupplement.json`-style data, accurate but a maintenance burden that drifts each OPR
release; (c) block/warn on lists carrying them. No work until this is decided.

## Outcome
