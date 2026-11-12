# 416 — 3.6 reference snapshot + census/coverage tooling

**Status**: done 2026-10-29 (tooling committed on `feature/opr-3.6`; data lives off-repo)
**Related**: #415, #378 (the AoF snapshot convention this follows), #196 (`--rule-coverage`)
**Branch**: `feature/opr-3.6`. Model/effort if re-run: Sonnet 5.5 / low.

## Goal

Every input the upgrade needs is on disk, outside the repo, pinned to the 2026-10-29 fetch, with a
reproducible way to (a) diff an OPR ruleset snapshot against another and (b) list which rule names the
engine cannot resolve. Nothing copyrighted enters the repo.

## What exists now

- `~/Projects/GDF Armies/opr-json-snapshots-3.6.0/` - 49 GDF books from `/api/army-books/{uid}?gameSystem=2`
  plus `_index.json` (the official book index). "Wormhole Daemons" is a 0-unit parent entry.
- `~/Projects/GDF Armies/Age of Fantasy/opr-json-snapshots-3.6.0/` - 41 AoF books (`gameSystem=4`) plus
  `_index.json`. "Rift Daemons" is a 0-unit parent entry.
- `~/Projects/GDF Armies/Rules 3.6.0/` - GF + AoF Core Rules v3.6.0 PDFs and Beginner's Guides with
  `pdftotext -layout` extracts, the common-rules API JSON for both systems, the census report, the two
  scratch-import coverage reports, and a README describing each file.
- `scripts/census-opr-books.py` (repo) - the census generator; usage in its docstring. Re-run after any
  future snapshot refresh.
- The scratch import used for coverage: every snapshot imported with `--import-opr` (GDF books against
  `GdfRuleSupplement.json`; AoF books against GDF + AoF supplements + `AofBookOverrides/<name>.json`) into
  a folder outside the repo, then `FdgRaylib --rule-coverage <folder>`. All 90 imports succeeded on the
  unmodified 3.5 importer (no shape break), which is why #437 is "medium", not "high".

## Notes

- 2026-10-29: Fetched, extracted, censused. Headline numbers are in the plan (section 2.2) and in
  `Rules 3.6.0/README.md`. The 3.5.x snapshot folders were left untouched as the census baseline.

## Decisions

- Snapshot folders are versioned by name (`opr-json-snapshots-3.6.0`) rather than overwriting the 3.5.x
  folders, so the census can be re-run and #378's pinned-bake note stays true for the old folder.
- The census and coverage outputs stay in the local-only folder; the repo gets only the generator and
  name-level summaries (plan Appendix B). Rule descriptions in bulk are OPR text.

## Outcome

Done. Inputs pinned; tooling in `scripts/`; numbers in the plan. Re-fetch only if OPR ships a 3.6.x patch
during the upgrade, and then record the new date here and re-run the census.
