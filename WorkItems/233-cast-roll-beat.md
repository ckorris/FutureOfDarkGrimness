# 233 — Add a beat for rolling to cast

**Status**: todo
**Related**: #033 (Caster subsystem), #056 (presentation beat stream), `DiceRolledBeat`, `CastSpellStage`

## Goal
Casting currently resolves its roll without a presentation beat - the spell just happens. Add a dice-roll beat for the cast attempt (mirroring shooting/save rolls: show the die, the threshold, success/failure) so the player sees WHY a spell succeeded or fizzled. Emit from the cast-roll resolution in `CastSpellStage` (engine-side beat emission, like the existing combat beats - engine change, ask before touching).

## Notes
- 2026-07-15: Filed from user playtest feedback.

## Decisions

## Outcome
