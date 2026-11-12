# 220 — Version control for Army Forge lists

**Status**: todo
**Related**: #156 (Army Forge builder — persistence design already embeds `BuilderList` + book snapshot in the `.fdgarmy`)

## Goal
User wants some form of version history for Army Forge lists — e.g. undo/redo across edits, or named snapshots/revisions of a list over time, so a bad edit or a "what did this look like last week" question is recoverable. Not yet scoped to a specific mechanism (in-app undo stack vs. saved revision snapshots vs. leaning on the user's own git/file backups) — surface the design fork (undo-stack vs. revision list vs. something else) before building.

## Notes
- 2026-07-15: Filed from user playtest feedback. Design approach not yet decided.

## Decisions

## Outcome
