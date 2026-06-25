# 104 — Single-unit `Evaluate` doesn't consume one-shot (`NextTrigger`) grants

**Status**: todo (no consumer today — build when a rule needs it)
**Related**: #101 (keyword-buff bridge — spun off from its last remaining piece), #042 (rule dispatch), #034 (spell/rule content, the likely source of a first consumer)

## Goal
Make the single-unit `RuleEvaluator.Evaluate(unit, seat, context, weapon)` path consume `NextTrigger`
(one-shot / "next time") `RuleGrant` tokens when the granted rule actually fires there — the same
consume-on-occurrence semantic `EvaluateAll`/`CollectSurviving` already applies at the combat hooks (#101).
"Done" = a one-shot buff whose only firing hook is reached through `Evaluate` (a round-start / deployment /
activation hook) fires once and then clears, proven by an integration test.

## Why it's deferred (not a bug today)
`Evaluate` deliberately passes `grantsToConsume: null`, so it projects granted rules but never spends them.
That's currently **correct**, because of how its four production call sites split:

- **3 are read-only queries** that must NOT consume — `TryGetDefer` / `TryGetLaterRoundDefer`
  (`PreDeploymentSelectContext`): `DeploymentTurnContext`, `ChooseUnitToActivateStage`,
  `StartOfRoundExtraActionStage`. They check whether a unit carries a defer rule; consuming a grant during
  a mere check would wrongly spend a buff.
- **1 is a genuine apply path** — `StartOfRoundExtraActionStage.GrantSpellTokens` (`RoundStartContext`),
  which applies the resulting token ops.

And critically: **no rule in the corpus grants a `NextTrigger` buff that fires only at one of these
single-unit-`Evaluate` hooks.** So there is nothing to consume — building the plumbing now would be
speculative generality (per the project's "grow vocabulary on demand" principle).

## How to build it when a consumer appears (~5-line opt-in)
Don't make `Evaluate` blindly consume (it would break the 3 query sites). Instead:
1. Add `bool consumeGrants = false` to `Evaluate`; when true, allocate the `grantsToConsume` list and run
   the same post-walk consume pass `CollectSurviving` uses (remove each spent FirstTrigger grant token
   payload-precisely via `RemoveTokensWithPayload`).
2. Opt in **only at the genuine apply site(s)** — today `GrantSpellTokens` — `Evaluate(..., consumeGrants: true)`.
3. Leave the `TryGet*Defer` queries on the default (non-consuming).
4. Add a `GrantedRuleConsumeOnRoundStartTests`-style integration test.

The seam is commented in `RuleEvaluator.Evaluate` pointing here.

## Notes
- 2026-06-25: Opened. Spun off from #101's final "remaining" item at the user's request, so #101 (the
  keyword-buff/aura dispatch mechanism) can close while this no-consumer edge stays tracked rather than
  silently cut. A comment at the `Evaluate` seam references this item.

## Outcome
(pending)
