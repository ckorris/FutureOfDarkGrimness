# 199 — AutoFill faults on tiny fractional wounds (probabilistic mode)

**Status:** open (filed 2026-07-09 during #191 A0)
**Where:** `FutureOfDarkGrimness/StateMachine/ResultsStructs/AssignWoundsResults.cs:185` (`AutoFill`)

## Symptom

A whole game in probabilistic mode dies mid-round with:

```
Game error: AutoFill could not assign all wounds. Required: 0.055555493, assigned: 0.
```

The state machine faults, the game ends `outcome=Fault rounds=0`.

## Deterministic repro (10/10 since #198)

Engine test harness: `TacticianScaffoldTests.PlayFreshGame`-style fresh game, **rich armies**
(`TestArmies.MakeRichArmy` x2), `ERandomnessType.Probabilistic`, `AutoPlaceObjectivesDebug = false`,
**seed 31415**. Found when A0's determinism pin ran on that seed; confirmed profile-independent
(faults identically under solo-rules and Tactician — the scaffold is a pure delegate). The pin test
moved to seed 424242 and left a comment pointing here.

## Reading (unverified)

A roll-derived expected-wound residue of ~0.0556 (= 1/18) reaches AutoFill but no model is eligible
to take it, so `assigned: 0`. Likely a float-precision / eligibility-threshold mismatch in the
fractional-wound path — the same family as the validation-margin gotchas in `docs/ResolverGuide.md`,
and adjacent to the house invariant that roll-derived values stay float (never int-locked). Whether
the bug is in AutoFill's eligibility filter, an upstream rule effect (rich armies carry
Blast/Counter/Impact), or the required-vs-assigned comparison lacking an epsilon: not yet
investigated.

## Notes

- 2026-07-09 — filed. Not a #159 sibling (that's movement cohesion; this is wound assignment), but
  found the same way: a seeded whole-game run that now reproduces exactly (#198's payoff).
