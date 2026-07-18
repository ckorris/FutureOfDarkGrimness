# 232 — Remove the saved-hits beat (especially its sound)

**Status**: todo
**Related**: #204 (save-beat pacing/grouping, done - this goes further), #056 (presentation beat stream), #053 (sound cues)

## Goal
Remove the presentation beat that plays when hits are SAVED (the no-damage outcome) - the user finds it adds noise (literally: especially the sound) without information. Scope: identify the beat emitted for successful saves (`SaveOverlay` / save-roll `DiceRolledBeat` family; see `SaveBeatOnWhiffTests` for the whiff behavior already tuned), and suppress the saved-outcome beat and/or its sound cue while keeping failed-save (damage) presentation intact. Confirm with the user whether the dice roll itself should still show and only the sound go, or the whole beat.

## Notes
- 2026-07-18: Implemented. `RollToSaveStage` no longer emits the `SaveBeat` (the per-threshold save
  dice-roll beats with their "N saved, M wounds" captions are untouched). The `SaveBeat` class, its
  wire serialization, `SaveOverlay`, and the "save" sound cue are left in place but dead - nothing
  emits the beat anymore (kept for wire/save compat; rip out later if it nags). Scope add from the
  same session: wound flinch pacing - `PresentationDurations.ModelWounded` 300ms -> 180ms (40%
  faster; flinches play one per wound so multi-wound volleys dragged). Death beats unchanged (500ms).
  Suite green (1687), headless smoke OK. Awaiting GUI hand-verify (shoot into armor: no blue pings,
  no ping sound, wounds snappier; failed saves still present wounds/deaths).
- 2026-07-15: Filed from user playtest feedback.

## Decisions
- 2026-07-18: User chose removing the WHOLE saved-hits beat (visual + sound), not just muting it, and
  added the wound-beat speedup (~40%) to this item's scope.

## Outcome
