# 289 — Decisive rolls render as real dice in probabilistic mode

**Status**: in-progress
**Related**: #090 (decisive rolls), #233/#245 (dice beat + panel), `IDiceRoller.RollDecisive`

## Goal
`RollDecisive` exists precisely so a binary outcome (morale, cast, objective count, token shed, Storm
pool) resolves to ONE concrete face even under the probabilistic roller. But every one of those sites
emits its `DiceRolledBeat` with `GameContext.Settings.RandomnessType`, so in probabilistic mode the
front-end draws the expected-value success BAR instead of the die that was actually rolled — the player
sees no dice for a roll that genuinely happened.

`DiceRolledBeat.Mode` describes the SHAPE of the histogram the front-end has to render, not the game's
randomness setting. Decisive emissions must declare `Realistic` so the concrete faces are drawn as dice.

Done when: in probabilistic mode a morale test / cast roll / objective roll / Storm pool / token-shed
roll shows tumbling dice settling on the rolled face, while genuinely fractional rolls (to-hit, saves,
dangerous terrain) keep the probability bar. Engine tests assert the mode on each decisive beat.

## Notes
- 2026-07-26: filed from a play session ("some things are still a dice roll, via a special call to
  IDiceRoller ... it should in fact show a dice roll").
- Decisive emission sites: `MoraleUtilities` (initial + Fearless re-roll), `CastSpellStage` (cast roll),
  `RollForObjectiveCountStage` (D3), `StormStage` (pool), `GameOperationServices`
  (`ClearTokenOnRoll` / `GrantTokenOnRoll`).

## Decisions
- Fixed at the emission sites via a `DiceRolledBeat.FromDecisive` factory rather than by having the
  front-end sniff integrality: the beat's own doc comment already warns that probabilistic counts can
  land on whole numbers by coincidence, so integrality is not a safe signal.

## Outcome
Shipped 2026-07-26 (engine `5a9e34a`). `DiceRolledBeat.FromDecisive` added and applied at all six
decisive emission sites (`MoraleUtilities` x2, `CastSpellStage`, `RollForObjectiveCountStage`,
`StormStage`, `GameOperationServices` x2 — seven call sites); `Mode`'s doc comment now states it is the
histogram's shape, not the setting. New `DecisiveDiceBeatModeTests` (4 tests) runs a Probabilistic game
with a `ProbabilisticDiceRoller` and asserts the morale + token-shed beats declare Realistic with whole
face counts, plus a scope guard that a fractional pool still declares Probabilistic. Engine suite 2190/2190
green; headless smoke exits 0. Awaiting GUI hand-verify (start a probabilistic game, take a morale test).
