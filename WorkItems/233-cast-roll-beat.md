# 233 — Add a beat for rolling to cast

**Status**: todo
**Related**: #033 (Caster subsystem), #056 (presentation beat stream), `DiceRolledBeat`, `CastSpellStage`

## Goal
Casting currently resolves its roll without a presentation beat - the spell just happens. Add a dice-roll beat for the cast attempt (mirroring shooting/save rolls: show the die, the threshold, success/failure) so the player sees WHY a spell succeeded or fizzled. Emit from the cast-roll resolution in `CastSpellStage` (engine-side beat emission, like the existing combat beats - engine change, ask before touching).

## Notes
- 2026-07-18: **Built** (with #244, same commit pair). `CastSpellStage` presents
  `DiceRolledBeat.From(castRoll, threshold, ..., "Roll to Cast", "Cast!"/"Failed")` right after the
  roll and before the result banner - the beat carries the SHIFTED threshold (boost + assists), so the
  die animation shows the real target face. No app-side change needed (the existing dice overlay +
  sound cue render any `DiceRolledBeat`). Pinned by `CastSpellStage_CastRoll_EmitsDiceRolledBeat`
  (asserts one beat, boost-shifted 3+ threshold, "Cast!" summary). Suite 1693/0.
  **Awaiting GUI hand-verification** (see the die tumble on a cast in a real game).
- 2026-07-18: Started, built together with #244 (caster self-boost) - both touch the same cast-roll
  site in `CastSpellStage`. Decision (user sign-off): dice beat AND the existing blue/red result
  banner both stay - the beat shows the die + threshold + a short "Cast!/Failed" summary, the banner
  keeps the full math (base, self boost, assists, tokens spent). Engine changes authorized.
- 2026-07-15: Filed from user playtest feedback.

## Decisions

## Outcome
