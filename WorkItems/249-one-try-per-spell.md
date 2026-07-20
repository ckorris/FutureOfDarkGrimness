# 249 — Caster: "only one try per spell" per activation is unenforced

**Status**: todo
**Related**: #234 (the "before attacking" gate — this was split out of it), #033 (Caster subsystem), #244 (self-boost)

## Goal
GF Core Rules v3.5.1, Caster(X): "At any point before attacking, spend as many tokens as the spell's
value to try casting **one or more spells (only one try per spell)**."

Casting several *different* spells in one activation is legal and the engine is right to allow it. But
"only one try per spell" is not enforced anywhere: `CastSpellStage` loops back to Choose Action after
each cast and keeps no record of which spells were attempted, so a caster with enough tokens can re-try
the *same* spell repeatedly in a single activation until it succeeds. That turns the 4+ roll from a real
risk into a token tax, which is a live balance hole — most directly on the failed-cast path, since
tokens are spent on the attempt whether or not it lands.

## Notes
- 2026-07-19: Filed while implementing #234. Found by reading `CastSpellStage.Enter` — it has no
  per-spell attempt state, and `UnitActionContext` tracks nothing spell-shaped (only `HasMoved` /
  `HasAttacked` / `IrreversibleActionTaken` / `PendingCustomAction`).
- Two plausible mechanisms, worth a fork before building:
  1. **Per-activation attempted-spell set on `UnitActionContext`** (cleared in `Reset`), consumed by
     `CastSpellStage.BuildSpellOffer` to mark already-tried spells non-castable with a reason. Mirrors
     how the other per-activation gates work; visible in the #244 spell picker as a disabled row.
  2. **`Cost.OncePerActivation` + `UsedMarker` tokens** (`Rules/Foundation/Cost.cs:43`,
     `Rules/Dispatch/RuleEvaluator.cs:716-741`) — the existing rule-level precedent, but it is keyed by
     rule name and spells are army data, not rules, so it likely needs a per-spell marker key.
  Option 1 looks like the better fit; confirm before building.
- Also confirm the intended scope of "one try": per activation (assumed) rather than per round.

## Decisions

## Outcome
