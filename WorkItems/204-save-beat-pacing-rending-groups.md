# 204 — Save-roll beats for Rending and non-Rending hit groups play too close together

**Status:** open (filed 2026-07-09 at Chris's request)
**Where:** presentation pacing around `RollToSaveStage` (one `DiceRolledBeat` per AP group) /
the beat clock; related umbrella: #056 (presentation beat stream polish)

## Symptom

When an attack contains both Rending (natural-6, per-hit AP) and normal hits, the save flow rolls
each group separately (per #032's per-hit AP split) and emits a `DiceRolledBeat` per group - but
the two beats play nearly on top of each other, so the viewer can't read them as two distinct
save rolls (different thresholds, different dice) before the second one lands.

## Direction

Give consecutive save-roll beats of one attack a minimum spacing (or hold the first group's dice
until the second group's roll presents alongside it, mirroring how the held save-dice already
stay up through wound assignment). Purely presentation - no rules/timing change to the engine
math; verify by eye in the GUI with a Rending weapon vs a mixed volley.

## Notes

- 2026-07-09 — filed verbatim from Chris's report during #200/#203 work.
- 2026-07-09 (second report, same day): re-raised - "the two beats for the dice rolls play too
  close together and it's not clear what happened." The readability harm is confirmed felt in
  real play, not just cosmetic.
