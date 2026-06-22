# 094 — Friendly-Caster ±1 cast assist

**Status**: todo
**Related**: #033 (Caster framework — spun off from its last tracked slice), #034 (spell content)

## Goal
When a Caster declares a spell + target(s), other friendly Caster units within 18" may spend their own
spell tokens to modify the cast roll by ±1 per token, before it resolves. "Done" = casting a spell offers
each eligible friendly Caster's controller the chance to contribute, those tokens are spent, and the net
modifier shifts the 4+ result — proven by an integration test and visible in a real game.

This was the last tracked slice inside #033; broken out to its own item (2026-06-21, at the user's
request) because it's an optional, networking-sensitive, multi-unit decision loop that's independent of
the (now complete) core casting framework.

## What exists to build on
- `CastSpellStage` (`.../MainUnitActionStage/CastSpellStage/CastSpellStage.cs`) resolves the cast with a
  single `GameContext.DiceRoller.RollDecisive()` 4+ check. There's an explicit insertion point comment
  there: *"±1 friendly-Caster assist — a #033 follow-up — would adjust here."* The assist slots in
  **after target selection, before the roll**.
- `EHookID.Casting_OnSpellAssistOffered` is **defined but unwired** (reserved in the Caster hook block) —
  this is its consumer.
- Token economy is done: friendly Casters carry `TokenType.SpellTokens`; spending is `RemoveTokens`.
- Eligibility helpers exist: `SpellTargeting` already finds units by affinity + range; the same
  team/affinity + distance machinery (`DistanceUtilities`, team lookup) can find friendly Casters in 18".
- Player decisions ride the existing request/resolver infra (e.g. a `YesNoRequest` per assister, or a
  count request); CLI + GUI + AI resolvers already exist for those shapes.

## Design forks to resolve before building
- **±1 direction / who may assist.** OPR allows Caster units within 18" to spend tokens for +1 (help) or
  −1 (hinder) each. Decide: friendly-only +1 (simplest, matches the #033 one-liner) vs. also letting enemy
  Casters spend tokens for −1 (fuller rule, more decision loops, opens an open-information question over
  the network). Recommend starting friendly-only +1 and recording the enemy −1 as a further follow-up.
- **Decision shape.** Per-assister: a YesNo("spend a token to add +1?") loop, or a single "how many
  tokens?" count request per assister. Count request is fewer prompts; YesNo reuses the simplest resolver.
- **How the modifier applies to a decisive roll.** The cast is `RollDecisive()` (4+). Apply the net
  assist as a threshold shift (need `4 - assist`+) or as a post-roll result adjustment — must stay correct
  under the probabilistic roller (don't int-lock; see [[project_dice_probabilistic_invariant]]).
- **Ordering vs. the cast cost.** The caster still spends the spell's threshold to attempt; assisters
  spend *additional* tokens. Confirm assist tokens are spent regardless of pass/fail (like the cast cost).

## Deferred (carry forward, don't silently cut)
- Enemy Casters opposing with −1 (if friendly-only +1 ships first).
- AI policy for whether/how much an AI Caster assists (the AI resolver must produce a legal, sensible
  answer — default could be "don't assist" to keep it conservative).

## Notes
- 2026-06-21: Item opened, spun off from #033's deferred "±1 assist" slice. #033's framework (slices 0–4
  + spell-authoring UI) is on branch `033-caster`.

## Outcome
(pending)
