# Work Items

Per-item working memory. Each file corresponds to a numbered entry in `../WorkItemsList.md` and is created when work on that item begins (not preemptively).

## File naming

`NNN-short-slug.md` — e.g. `017-melee-in-range-checks.md`.

- `NNN` is the number from the index, zero-padded to 3 digits.
- Slug is a short kebab-case identifier that survives renames.
- Numbers are permanent — never reused, even if an item is deleted.

## Template

```markdown
# NNN — Title

**Status**: todo / in-progress / blocked / done
**Related**: #other-item, PR #N, commit SHA (optional)

## Goal
One paragraph: what "done" looks like for this item. Specific enough that someone picking it up later knows when to stop.

## Notes
Running log. Newest entries on top, each prefixed with the date.

- YYYY-MM-DD: ...

## Decisions
Surprises, dead ends, and *why* the implementation went the way it did. Survives even when the running notes go stale. Future readers care about this section more than Notes.

## Outcome
Written when the item closes. One paragraph: what shipped, what was deferred, links to follow-up items if any.
```

## Conventions

- **Append, don't overwrite.** Add new dated entries to Notes; don't rewrite history.
- **Notes rot; decisions don't.** Keep them separated.
- **Keep the index lean.** An entry in `../WorkItemsList.md` is at most ~3 lines: number, title, one-sentence scope/status, link. Running notes, commit hashes, root-cause narratives, and test tallies go in the detail file — if an index line grows past that, move the overflow here the same day.
- **When closing**: write a brief Outcome, set Status to `done`, mark the index line `[x]`, and move it to `Archive.md` (in this directory). Completed items never stay in the index.
- **Splits**: if an item turns out to be too big, leave its line in the index pointing at the new numbers it was split into. Don't delete.
- **Cross-references** to commits/PRs/other items go in the header `Related:` line.
- **Number collisions** (parallel sessions claiming the same number): see `Reconciliations.md` for the log and the standing precedent (the unmerged local item yields and takes a fresh number).

## Pre-push hook

A per-clone hook blocks pushing duplicate work-item numbers across the index and the archive.
It is not version-controlled; install it in a new clone as `.git/hooks/pre-push` (chmod +x):

```sh
#!/bin/sh
# pre-push: refuse to push if work-item numbers are duplicated across
# WorkItemsList.md and WorkItems/Archive.md. Override: git push --no-verify
root=$(git rev-parse --show-toplevel 2>/dev/null) || exit 0
index="$root/WorkItemsList.md"
archive="$root/WorkItems/Archive.md"
[ -f "$index" ] || exit 0
files="$index"
[ -f "$archive" ] && files="$files $archive"
dupes=$(cat $files | grep -oE '^- \[.\] [0-9]+' | grep -oE '[0-9]+' | sort | uniq -d)
if [ -n "$dupes" ]; then
  echo "" >&2
  echo "X pre-push blocked: duplicate work-item numbers in index/archive:" >&2
  for n in $dupes; do echo "    #$n" >&2; done
  echo "  Renumber the collision (numbers are never reused), or override with: git push --no-verify" >&2
  echo "" >&2
  exit 1
fi
exit 0
```
