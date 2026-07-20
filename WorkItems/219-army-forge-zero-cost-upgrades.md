# 219 — Audit Army Forge upgrades for missing point costs

**Status**: todo
**Related**: #156 (Army Forge builder), #218 (adjacent Replace-All cost bug), `OprBookImporter.cs`, `FdgRaylib/Assets/Books/*.fdgbook`

## Goal
User has spotted multiple upgrade options in-app that should cost points but show/charge 0. Scope: audit the bundled `.fdgbook` catalog (and the `OprBookImporter` mapping that produced it) for options with a missing or zero `Cost` where the source OPR data has a nonzero price, and fix the importer/data. Done = a sweep across all bundled books turns up the offending options, root cause identified (importer mapping gap vs. source data vs. compiler), and costs corrected.

## Notes
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
