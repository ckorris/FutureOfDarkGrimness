# 232 — Remove the saved-hits beat (especially its sound)

**Status**: todo
**Related**: #204 (save-beat pacing/grouping, done - this goes further), #056 (presentation beat stream), #053 (sound cues)

## Goal
Remove the presentation beat that plays when hits are SAVED (the no-damage outcome) - the user finds it adds noise (literally: especially the sound) without information. Scope: identify the beat emitted for successful saves (`SaveOverlay` / save-roll `DiceRolledBeat` family; see `SaveBeatOnWhiffTests` for the whiff behavior already tuned), and suppress the saved-outcome beat and/or its sound cue while keeping failed-save (damage) presentation intact. Confirm with the user whether the dice roll itself should still show and only the sound go, or the whole beat.

## Notes
- 2026-07-18 (v2, casualty cascade): hand-verify feedback - the flat 180ms wound trim read no faster.
  New behavior: within one volley, every death/flinch beat except the LAST carries `Overlap = true`
  (`Held`, `HoldLeadIn = PresentationDurations.CasualtyStagger` 150ms), so the engine paces 150ms
  between casualties while each animation still plays its FULL length; the last casualty is a normal
  beat and runs out on its own ("BE-BE-BE-BE-BEEW"). 5 kills: 2.5s -> ~1.1s. `ApplyWoundsStage` scans
  the precomputed assignments for the last Wounds > 0 entry (trailing untouched models don't count).
  App: `PresentationPlayer` gained a cascade track (like the #238 attack track) - held casualty beats
  free the active slot and animate concurrently; each still fires its sound cue at start (Raylib
  PlaySound retriggers = the rapid-fire report; same behavior as #238 volley cues). `ModelWounded`
  duration reverted 180ms -> 300ms - the cascade supersedes the flat trim (5 wounds ~0.9s, and the
  final flinch reads fully). Overlap rides the wire (serialization tests extended); stage tests in
  `CasualtyCascadeTests`, player tests in `FdgRaylib.Tests/CasualtyCascadePlayerTests`. Engine 1689 +
  app 378 green, headless smoke OK. Awaiting GUI hand-verify: kill a 5-model unit in one volley -
  deaths ripple ~150ms apart, last one lingers; sounds rapid-fire; morale dice wait for the cascade.
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
