# 234 — Casting allowed after charging + melee - check the rules

**Status**: implemented + tested 2026-07-19; awaiting GUI hand-verify
**Related**: #033 (Caster), #173 (spell targeting checks), `docs/EngineNotes.md` known stubs ("never assume a rule is enforced because a stage exists")

## Goal
User observed a unit could Charge, resolve melee, and then still Cast in the same activation - and doubts that's legal. Step 1: check the GF rulebook's Caster rule for when during an activation a spell may be cast (and whether the action taken restricts it). Step 2: if it's illegal, gate the Cast option in the action/ability flow accordingly (engine change - ask before touching); if it's actually legal, record that here and close.

## Notes
- 2026-07-19: **Implemented.** Rules check settled it (see Decisions) - the observed behavior was
  illegal, and the gate is exactly `HasAttacked`, which `ShootStage.cs:77` and `MeleeStage.cs:116` are
  the only setters of. So one check covers both the charge-then-melee case this item was filed for and
  the shooting case the owner added.
  - `ChooseActionStage.GetCanCast`: new first gate returning "Has already attacked." (wording matched to
    the existing Charge gate). Cast greys out with a reason rather than vanishing, per the built-in-action
    idiom. Corrected the stale #033 comment at the gate block, which asserted Cast was offered
    "regardless of whether the unit has moved/attacked".
  - `CastSpellStage.Enter`: mirror guard - the stage is reachable directly, so it logs and loops back
    rather than trusting the menu. No tokens spent on that path.
  - Tests in `CasterRuleIntegrationTests.cs` (3, mirroring `TeleportRuleIntegrationTests.cs:96-111`'s
    negative pattern; reuses the `internal` `RecordingActionRequester` from `TransportDisembarkTests.cs`,
    same namespace): Cast absent after attacking; Cast **still present** after moving (pins the gate to
    attacking, not to spending the activation); direct `CastSpellStage` entry after attacking spends 0
    tokens and returns.
  - Verified: suite **1720/0** (was 1717), full `dotnet build` clean, headless smoke exits 0.
- 2026-07-15: Filed from user playtest feedback.

## Decisions
- **2026-07-19 - the rule, verbatim** (GF Core Rules v3.5.1, owner-supplied PDF): "Caster(X): Gets X
  spell tokens at the start of each round, but can't hold more than 6 tokens at once. **At any point
  before attacking**, spend as many tokens as the spell's value to try casting one or more spells (only
  one try per spell). Roll one die, on 4+ resolve the effect on a target in line of sight."
  So: attacking (shooting *or* melee) closes the casting window; **moving does not** - casting brackets
  the move. The observed charge -> melee -> cast sequence was illegal. Confirmed against the rulebook,
  not an owner ruling.
- **Scope split**: the same sentence licenses casting *multiple* spells per activation but only "one try
  per spell". The engine tracks no per-spell attempt state, so a caster can currently re-try the same
  spell while tokens last. Out of scope here, filed as **#249** rather than folded in silently.

## Outcome
Gated; awaiting GUI hand-verify. **Verify:** activate a caster, Charge and resolve melee, then reopen
the action menu - Cast is greyed out reading "Has already attacked." Repeat with Shoot instead of
Charge (same result). Then activate a caster, Advance, and confirm Cast is **still** offered and works.
