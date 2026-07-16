# 232 — Remove the saved-hits beat (especially its sound)

**Status**: todo
**Related**: #204 (save-beat pacing/grouping, done - this goes further), #056 (presentation beat stream), #053 (sound cues)

## Goal
Remove the presentation beat that plays when hits are SAVED (the no-damage outcome) - the user finds it adds noise (literally: especially the sound) without information. Scope: identify the beat emitted for successful saves (`SaveOverlay` / save-roll `DiceRolledBeat` family; see `SaveBeatOnWhiffTests` for the whiff behavior already tuned), and suppress the saved-outcome beat and/or its sound cue while keeping failed-save (damage) presentation intact. Confirm with the user whether the dice roll itself should still show and only the sound go, or the whole beat.

## Notes
- 2026-07-15: Filed from user playtest feedback.

## Decisions

## Outcome
