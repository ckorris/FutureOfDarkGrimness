# 101 — Keyword-buff bridge: make granted rules fire during dispatch

**Status**: DONE (2026-06-25) — bridge + Aura + resume-path resolver complete; the one no-consumer edge (single-unit-`Evaluate` NextTrigger consume) spun off to #104; conferred-rule content = #034
**Related**: #033 (Caster framework; the buff archetype), #034 (spell content), #042 (rule dispatch / token system)

> **Renumbered 2026-06-22.** Opened as #095, but #095 was already assigned to other work on a parallel
> instance; per the never-reuse rule this item yields and takes #101. The commit `ada5536` and the pushed
> `033-caster` branch reference "#095" — they predate the renumber.

## Goal
Make `Effect.AddRule` (and `Effect.Aura`) grants actually take effect. Today a granted rule is stored as
a `RuleGrant` token but **never read during dispatch**, so a unit that "gets Furious once" carries an
inert token and Furious never fires; `FirstTrigger` ("once / next time") is likewise never consumed.
"Done" = a unit carrying a `RuleGrant` token behaves as if it has that rule at the relevant hooks, and a
`FirstTrigger` grant is consumed on first use — proven by an integration test (cast a "gets Furious once"
buff, the buffed unit's next melee gets the extra-hit-on-6, then the token clears). This is what makes the
~114 keyword-grant spells (the bulk of the corpus's buff/debuff half) functional rather than authorable-
but-inert.

Spun off from #033's primitive work (2026-06-21, user's choice): the two damage/stat primitives shipped;
the keyword-buff bridge is the separate, larger piece.

## The gap (verified 2026-06-21)
- `RuleEvaluator.CollectTagged` reads only `unit.RuleDefinitions` / `weapon.RuleDefinitions` /
  `model.RuleDefinitions` — never `unit.Tokens`. So `RuleGrant` tokens are inert.
- `TokenClearService.ClearsAtHook` has no `FirstTrigger` case — `FirstTrigger` tokens are never decremented.
- `Effect.AddRule.Apply` correctly *grants* the `RuleGrant(RuleName, Lifetime)` token; nothing consumes it.

## Design sketch (resolve forks before building)
- **Project granted rules into evaluation.** When the evaluator collects a unit's rules at a hook, also
  evaluate the rules named by its `RuleGrant` tokens. Needs **rule-name → definition resolution at
  dispatch time** — which isn't currently reachable from the evaluator (the resolver is local to
  `FDGServer.CreateArmies`). Fork: (a) make the resolver reachable (store it on `GameContext`/inject into
  `RuleEvaluator`); or (b) pre-resolve the granted rule to a `ResolvedRule` at grant time and store it on
  the token/payload (avoids a dispatch-time resolver, but `Effect.AddRule.Apply` has no resolver either —
  would need one threaded in). Recommend deciding this first; (a) is the more general fix and also helps
  any future "resolve by name at runtime" need.
- **FirstTrigger consumption.** "Next time the effect would apply" must decrement when the granted rule
  actually fires (not merely when the hook fires) — otherwise a buff that doesn't apply this hook is
  wasted. Define precisely when a `FirstTrigger` grant is "used".
- **Shared foundation with the stat-modifier primitive (#033 primitive 2).** That primitive already built
  half of this: granted *numeric* modifiers (self-describing `StatModifier` tokens) read + consumed at the
  roll stages via `GrantedRollModifiers`. The keyword bridge is the same idea for *named rules* (which
  additionally need name resolution). Consider unifying both onto one "granted effects live during
  dispatch + FirstTrigger" mechanism rather than two parallel paths — and revisit the token-merge edge
  (same-type/owner grants merge in the container, discarding payloads), which a dedicated granted-effect
  store would fix cleanly.
- **Aura** (`Effect.Aura`) grants unit-wide; today its per-model expansion is also deferred — fold in here
  or keep separate.

## Concrete seams (carried from #033 primitive work — 2026-06-22)
Pointers so this can resume without re-deriving them:
- **Injection point**: `Rules/Dispatch/RuleEvaluator.cs` — `CollectTagged(unit, seat, weapon, models, …)` →
  `CollectFromRules(rules, unit, carryingWeapon, seat, …)` per source. Project granted rules here: read the
  unit's `RuleGrant` tokens (`unit.Tokens.GetAllTokens(TokenType.RuleGrant)`, payload
  `TokenPayload.RuleGrant(RuleName, Lifetime)`), resolve each name → `ResolvedRule`, and run them through
  `CollectFromRules`. The seat for a granted rule = the bearer's seat in the event.
- **The stat-modifier half already built** (the pattern to mirror/unify): `Rules/Dispatch/GrantedRollModifiers.cs`
  `ConsumeNet(IUnit, ERollKind)` reads per-roll-kind token types (`TokenType.HitRollModifier`/`SaveRollModifier`/
  `MoraleRollModifier`, Foundation), sums `TokenPayload.StatModifier(Delta)`, and **removes FirstTrigger tokens
  at the read site**. It's consumed in `DetermineHitRollStage` (attacker/Hit), `DetermineSaveRollsNeededStage`
  (defender/Save), `MoraleUtilities.TakeMoraleTest` (Morale) — i.e. FirstTrigger is decremented where the
  modifier is *used*, not in the evaluator. The keyword bridge can follow this "consume on use" model, or
  unify both onto one granted-effect store.
- **Resolver access** (the fork-(a)/(b) crux): `GameContext` exposes `RuleEvaluator` but **no resolver**. The
  resolver is `CoreRuleCatalog.CreateResolver()` + `ArmyListRuleResolution.RegisterEmbeddedDefinitions`, built in
  `FDGServer.CreateArmies` and discarded. Precedent for fork (b): #033 Slice 1 pre-resolved spell `WithRules`
  names → `ResolvedRule` at army load (`SaveLoad/ArmyListSpellResolution.ResolveSpells`, stored on `ArmyData`).
  `Effect.AddRule.Apply` runs at cast time with no resolver, so pre-resolving the granted rule needs the
  resolver threaded into the grant path — vs fork (a), exposing the resolver on `GameContext`/`RuleEvaluator`.
- **Lifetimes**: `Effect.ClearTriggerFor(ELifetime)` (in `Rules/Definitions/Effect.cs`) maps NextTrigger→
  FirstTrigger, ThisActivation→ActivationEnd, ThisRound→RoundEnd, etc. `TokenClearService` sweeps the
  duration triggers; only `FirstTrigger` needs read-site consumption (the gap).
- **Test harness**: `Tests/CasterRuleIntegrationTests.cs` + `FixedFaceDiceRoller` (honors rollCount, unlike
  `FixedDiceRoller`) + `TriggeredMoveTestContext(store, requester, diceRoller?)`. The buff *grant* is already
  tested (the RuleGrant token lands); #101 adds the "…and it fires, then clears" assertion.

## Notes
- 2026-06-25: **Aura projection + resume-path resolver DONE.** Two engine commits on `033-caster`:
  - `9781b01` — **apply aura grants at unit creation.** `UnitCreationRules.Apply` now runs
    `OperationApplier.ApplyTokenOperations` over the `Lifecycle_OnUnitCreated` queue, so `Effect.Aura`'s
    `GrantTokenToUnit` actually lands — it was produced then dropped (only the `MaxWoundsSink` was folded),
    leaving auras inert in live games. The granted rule then projects unit-wide via the existing read-back
    (`CollectGrantedRules`). Unit-scoped, no radius (per the user: GDF auras apply only to the bearer's own
    unit; there is no set-distance passive buff). `AuraRuleIntegrationTests` drives the REAL creation pass
    (no manual token add — the distinction from `GrantedRuleReadbackTests`, which seeds the token by hand).
  - `852a99e` — **rebuild the resolver on resume.** Discovered while scoping: granted-rule tokens (auras /
    buffs) DO round-trip in the save — the token container is `[JsonProperty]`; only the public accessor is
    `[JsonIgnore]` — so the original "tokens don't persist, re-grant on resume" plan was wrong. The real gap
    was that the resume `FDGServer` ctor left `_ruleResolver` null (`CreateArmies`, which builds it, runs
    only for a new game), so the surviving tokens were inert. Extracted `BuildRuleResolver`, called on both
    paths. **No re-grant pass** — re-applying creation rules would double the grants (and reset Tough
    wounds, the reason resume skips that pass). Two new `RuleRehydrationOnResumeTests`: RuleGrant payload
    survives + projects with the resolver; inert without it (the gap closed). Suite 766/0, build clean,
    headless exit 0.
  - **Spun off:** the niche single-unit `Evaluate` NextTrigger consume (the activation/deployment hooks
    `EvaluateAll` doesn't run the consume pass on — no corpus buff targets them today) is now **#104**, with
    a comment at the `Evaluate` seam pointing there. Conferred-rule *implementations* remain = #034. With
    both moved out, #101's dispatch mechanism is closed.
- 2026-06-22: **Reconciled with master's #100 — adopted its bridge, kept this branch's fixes.** While
  this branch built #101, origin/master's **#100** independently shipped the same granted-rule read-back
  + FirstTrigger consume + the same `TokenContainer` payload-merge fix (engine `4fb6159`/`b97fa7a`).
  Merging master in: this branch's redundant bridge commits (the `8bdd4a6`/`1f0717f` below) were **dropped
  in favour of master's #100 bridge**, and three fixes were folded onto it (engine `9375321`):
  (1) FirstTrigger grants consume **directly in the evaluator** (master's `ConsumeRuleGrant` op silently
  no-op'd at the hit/save/melee hooks — they run sinks, not `OperationApplier` — exactly where most "once"
  buffs apply); (2) consume is **occurrence-based** (spent when the rule's hook+seat fires, regardless of
  condition/survival — the user's chosen semantic); (3) `TokenClearService` made payload-precise. The
  dropped-commit notes below are kept as the record of the parallel build. Item stays open for Aura +
  resume-path; suite 761/0, full solution builds, headless exits 0.
- 2026-06-22: **AddRule dispatch bridge DONE** (resolver fork = option (a), per the user). Two engine
  commits (SUPERSEDED — see the reconciliation note above; these commits are not in the merged history):
  - `8bdd4a6` — **token payload is part of entry identity.** `TokenContainer.AddToken` merged by
    type+owner only, silently keeping the first token's payload, so two different granted rules from one
    owner collapsed and lost a name. Payload is now in the merge key; added `RemoveTokensWithPayload`
    (interface + impl); `TokenClearService` clears expired tokens payload-precisely. No-payload tokens are
    unchanged (Equals(null,null)). This also retires the stat-modifier merge edge noted in #033 Slice B.
  - `1f0717f` — **project + consume granted rules in `RuleEvaluator`.** Optional `IRuleResolver` injected;
    `CollectTagged` projects each `RuleGrant` token by resolving its name → `ResolvedRule` and firing it
    like an innate rule (condition-gated, deduped vs innate copies). **R2 consumption**: a `NextTrigger`
    grant is removed when its rule's hook+seat next fires on a real `EvaluateAll`, regardless of condition
    or outcome (wasting a one-shot buff by forcing the situation is a valid tactic — user's call). Read-only
    `EvaluateAllNamed`/single-unit `Evaluate` project but never consume; duration grants left to
    `TokenClearService`. Resolver threaded `FDGServer` (fresh-game) → `GameContext` → `RuleEvaluator`.
    Tests in `GrantedRuleProjectionTests` (probe rule = Reliable). Suite 648/0; app builds; headless 0.
  - **Scope landed:** any rule that exists in the resolver (core catalog + #059 army-embedded) now fires
    when granted. So buff spells naming an already-implemented rule (Furious, Stealth, …) work now.
  - **Remaining (recorded, not cut):**
    1. **Aura** (`Effect.Aura`, unit-wide grant) — not yet projected; its per-model expansion is still
       deferred. Same projection seam should serve it.
    2. **Resume path** — `FDGServer` resume ctor builds no resolver (and master's #095 rule-rehydration
       isn't in this branch yet), so granted-rule projection is **inert on resumed games**. Reconcile when
       `033-caster` merges master.
    3. **Single-unit `Evaluate` hooks** don't consume `NextTrigger` grants (niche activation/deployment
       hooks; no corpus buff targets them). A grant whose ONLY hook is such a path would fire un-consumed.
    4. **Conferred-rule content** — the ~114 corpus spells name rules many of which aren't implemented
       (Evasive, Quick Shot, faction Boosts, …); those are #034. This item makes the *mechanism* work; a
       granted-but-unimplemented name resolves to nothing and is skipped (army-load's skip-and-warn).
- 2026-06-21: Opened. The #033 survey classified ~114 spells as `AddRule`-expressible (the buff/debuff
  half); they're authorable today but inert until this lands. The conferred rules many of them name
  (Evasive, Quick Shot, faction "Boost" rules, …) are themselves unimplemented — that's #034 content, a
  separate axis from this dispatch bridge.

## Outcome
(mechanism complete) The keyword-buff/aura dispatch mechanism works end-to-end: `Effect.AddRule` and
`Effect.Aura` grants fire — auras land at unit creation and project unit-wide (unit-scoped, no radius, per
GDF); one-shot grants consume correctly — over a payload-precise token container, and the grants survive a
save/load resume (the tokens round-trip; the resume path now rebuilds the resolver that reads them back).
Auras are unit-scoped with no radius, per GDF. The one no-consumer edge — the single-unit `Evaluate`
NextTrigger consume — is spun off to #104 (build when a rule needs it); conferred-rule *implementations*
are #034.
