# 415 — OPR 3.6 upgrade umbrella

**Status**: in-progress (planning done 2026-10-29; implementation not started)
**Related**: #416-#445 (the upgrade's items), `docs/opr-3.6-upgrade-plan.md` (the plan), #378 (AoF books), #153 (book importer), #042 (rules architecture)
**Branch**: `feature/opr-3.6` in both repos. Recommended model/effort for the owner sessions: Fable 5.1 / medium.

## Goal

Future of Dark Grimness implements OnePageRules v3.6.0 (Grimdark Future + Age of Fantasy) end to end:
core rules, the 39 core special rules, every faction rule the 88 official books reference, re-imported
3.6.0 books, 3.6 spells, and a release tagged `v0.4.0` (OPR 3.6.0). 3.5 support is dropped except that
previously saved `.fdgarmy` files (with embedded rule definitions) still load. "Done" = the #445 checklist
passes and the feature branch is merged to master in both repos.

## Rules for every agent on this item (restated from the plan, section 1)

- Work only on `feature/opr-3.6` (superproject AND `FutureOfDarkGrimness`). Never commit/merge/push `master`.
- Submodule-first commit cadence; subjects start with the item number; push `feature/opr-3.6` only.
- Only this item's owner merges `master` into the feature branch (after each phase, submodule first).
- Rule text comes from the local-only `~/Projects/GDF Armies/Rules 3.6.0/` folder and the 3.6.0 snapshots
  (plan section 3). Never commit anything from that folder.
- No version switches: 3.6 replaces 3.5. Legacy `.fdgarmy` loading is the one exception (#440).
- Verify per CLAUDE.md plus `--rule-coverage` non-regression; keep the plan's status column current.

## Notes

- 2026-10-31 (from #444): post-merge cleanup slice handed here - the stale "not-yet-rebaked books" / "until #438" /
  "until #436" code comments (15 sites, listed in #444's 2026-10-31 note) now describe a state the re-bake ended;
  comment-only, outside #444's v3.5.1-citation scope.
- 2026-10-29: OPR released 3.6.0 today. Local `master` was 22 commits behind `origin/master` (#406/#412-#414
  analytics + fixes landed from another session); fast-forwarded, then branched `feature/opr-3.6` in both
  repos from `c5c7ebf` / engine `479888d`. Snapshotted all 90 Army Forge books (49 GDF incl. the empty
  "Wormhole Daemons" parent, 41 AoF incl. the empty "Rift Daemons" parent), the 4 rules PDFs (text extracted
  with pdftotext), and the common-rules API for both systems into `~/Projects/GDF Armies/`. Ran the census
  (`scripts/census-opr-books.py`) and a scratch import + `--rule-coverage` of every 3.6 book against the
  current supplements: GDF 4774 dead references across 122 names, AoF 2038 across 111 (today's bundled 3.5
  books: 0). Wrote the plan and filed #415-#445. Numbers reserved on `master` by a docs-only commit
  (push pending Chris's OK, plan Q1).
- 2026-10-29: Open questions Q1-Q9 posted to Chris (plan section 6) with the defaults the plan assumes.

## Decisions

- **Army data comes from the Army Forge API, not PDFs.** The API already serves 3.6.0 for every official
  book with per-book rule definitions and spell text; PDFs were only downloaded for the core rules and the
  Beginner's Guide (terrain). Saves the PDF transcription step the 3.5 work needed.
- **One feature branch for the whole upgrade, merged once.** A half-upgraded master (3.6 core rules with
  3.5 books, or vice versa) is unplayable; the branch is the unit of release.
- **Numbers reserved up front on master.** Thirty-one items filed at once on a long-lived branch would
  otherwise collide with every number another session files from `origin/master` (see Reconciliations 39,
  40 and the 406 collision three days ago). A docs-only reservation commit on master is cheaper than one
  renumber.
- **Faction rules are grouped by mechanism family, not by book.** The 3.5 work went book by book; the 3.6
  churn is ~6 families (plan section 2.2) that cut across all 88 books, so family items (#431-#433) come
  first and the per-system sweeps (#434/#435) mop up.

## Outcome

(open)
