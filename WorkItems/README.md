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
- **When closing**: write a brief Outcome, set Status to `done`, and mark `[x]` in `../WorkItemsList.md`. Move the line to the `## Done` section of the index.
- **Splits**: if an item turns out to be too big, leave its line in the index pointing at the new numbers it was split into. Don't delete.
- **Cross-references** to commits/PRs/other items go in the header `Related:` line.
