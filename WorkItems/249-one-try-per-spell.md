# 249 — Caster: "only one try per spell" per activation is unenforced

**Status**: implemented + tested 2026-07-19; awaiting GUI hand-verify
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
- **2026-07-19 - took option 1** (per-activation attempted-spell set), per the recommendation above and
  owner go-ahead. Option 2's `Cost.OncePerActivation` machinery is keyed by *rule* name, and spells are
  army data rather than rules, so it would have needed a synthetic per-spell marker key for no gain.
- **The try is recorded with the cost, before the roll** - so a failed cast burns the attempt, which is
  the whole point of the clause. Every cancel path in `CastSpellStage` returns before the commit point,
  so browsing the spell menu and backing out consumes nothing (preserves #248's pristine-activation rule).
- **Scoped per activation, not per round** - the rulebook sentence describes a single activation
  ("At any point before attacking ... try casting one or more spells"). Pinned by a test over `Reset`.

## Outcome
Implemented 2026-07-19 alongside #234.

- `IUnitActionContext`/`UnitActionContext`: `HasAttemptedSpell(name)` + `RegisterSpellAttempt(name)` over
  a name-keyed `HashSet<string>`, cleared in `Reset` with the other per-activation flags.
- `CastSpellStage`: records the attempt at the commit point; `BuildSpellOffer` takes the context and marks
  tried spells non-castable with "already tried this activation" (checked first, since unlike the
  token/target reasons it cannot change for the rest of the activation).
- `ChooseActionStage.GetCanCast`: filters to untried spells before the affordability/target checks, with a
  distinct "Every spell has been tried this activation." reason. **This half matters as much as the
  picker**: without it Cast would keep being offered once nothing castable remained, and picking it would
  enter a stage that immediately loops back - the same no-progress cycle the existing target gate guards.
- No app-side change needed: both AI resolvers (`AiChooseSpellResolver`, `TacticianChooseSpellResolver`)
  already filter on `Castable`, and the GUI/CLI pickers already render `UnavailableReason`.
- Verified: suite **1724/0** (was 1720, +4), full `dotnet build` clean, headless smoke exits 0 - including
  a caster-army run (`FutureOfDarkGrimness/armies/example-caster.fdgarmy` both sides) that reaches the
  spell picker.

**Known limitation (pre-existing, not introduced here):** the tried-set lives on `UnitActionContext`,
which is not store-backed, so a mid-activation save/resume loses it - exactly as it already loses
`HasMoved`/`HasAttacked`. Tracked by #057; not worth a separate item.

**Verify (GUI):** cast a spell and fail the roll, then reopen the spell picker - that spell is greyed out
reading "already tried this activation" while a second spell stays castable. With only one spell in the
list, Cast itself greys out reading "Every spell has been tried this activation." Re-activate the caster
next round and confirm the spell is available again.
