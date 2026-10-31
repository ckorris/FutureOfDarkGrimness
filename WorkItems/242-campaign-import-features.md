# 242 — Import campaign/narrative features from Army Forge lists

**Status**: todo
**Related**: #241 (split from)

## Goal
Support the Army Forge list features that #241's importer deliberately skips: the `campaignMode` / `narrativeMode` list flags and per-unit campaign progression (`xp`, `traits[]`). Today the importer emits a warning per occurrence and drops them. Done = decide which of these have an in-game meaning for us (trait -> rule mapping? XP display only?), map those, and stop warning about what's now supported.

## Notes
- 2026-07-16: Filed from #241. Shapes seen in the TTS list JSON: top-level `campaignMode`/`narrativeMode` booleans; per-unit `xp` (int) and `traits` (array). Traits carry campaign upgrades that likely alter stats/rules — needs a campaign-mode share link to see the resolved shape.

## Decisions
(none yet)

## Outcome
(open)
