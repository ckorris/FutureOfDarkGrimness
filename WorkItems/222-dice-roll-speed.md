# 222 — Tie dice rolls take too long

**Status**: todo
**Related**: #056 (presentation beat stream), `DiceRolledBeat.cs`, `RollOffBeat.cs`, `DiceOverlay.cs`

## Goal
Speed up the dice-roll animation/pacing specifically for tie-break rolls (`RollOffBeat`) — user reports it takes too long. Check whether this is `NominalDuration` tuning on the beat itself or `DiceOverlay` animation timing, and whether the fix should be scoped to roll-offs only or dice rolls generally.

## Notes
- 2026-07-15: Filed from user playtest feedback.

## Decisions

## Outcome
