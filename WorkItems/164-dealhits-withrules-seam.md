# 164 — `DealHits.WithRules` resolver seam (Blast on pre-attack / Strafing hits)

**Status**: in-progress (core fix shipped 2026-07-19; awaiting GUI hand-verify)
**Related**: #197 (the faction-rule umbrella this unblocks), #100 #10 (dice-pool primitive), #034 (spells —
the path this borrows from), `SpecialRulesAudit.md`

## Goal

An effect that "deals N hits" must get the same weapon-rule treatment a fired volley does. Concretely:
Breath Attack is authored as `DealHits(1, with: Blast(3), AP(1))` and, until this item, dealt **one** hit —
the `WithRules` names were dropped on the floor. Done = the ability and Strafing paths run the same
hit-complete fold the spell path already runs, with an integration test per path and a mutation check.

## Notes

- 2026-07-19: **Shipped the core fix.** Engine only; no app-side change.
  - New shared `SyntheticHitResolution` (`MainUnitActionStage/`, next to `PerHitApSplitter` /
    `PostCombatMoveGate`) holding the fold lifted verbatim out of `ResolveSpellDamageStage`: rolls the base
    hits as real dice, evaluates `HitRollCompleteContext` with **both** seats (Actor = attacker + synthetic
    weapon, Subject = defender's living models), then folds `PerHitApSplitter` -> `HitInjectionSink` ->
    `HitMultiplierSink` (Blast, capped at the target's living-model count) -> `RollModifierSink` ->
    `ReduceArmorPenetration`. Takes an `isSpell` flag so `Condition.IsNotSpell` still gates Shielded out of
    the spell path and *in* for the ability path.
  - `ResolveSpellDamageStage` now delegates to it (behaviour unchanged — this is the extraction source).
  - `PreAttackStage` and `StrafingStage` build a synthetic weapon carrying the effect's AP **and** its
    resolved `WithRules`, then run the fold instead of synthesizing a bare `RollToHitResults`. The
    now-false `RuleDiagnostics.WarnOnce` about weapon rules not being applied is deleted.
  - **Second defect fixed in the same slice:** `StrafingStage` hardcoded `armorPenetration: 0` while
    reading the hit count off the op, so an authored `DealHits` AP was silently dropped. It now reads
    `dealHits.ArmorPenetration`. Core Strafing carries AP 0, so nothing shipped changes behaviour — this is
    a latent-gap fix, and it is what the new Strafing test pins.
  - `RuleEvaluator.RuleResolver` exposes the resolver #100 slice 1 threaded in;
    `ArmyListSpellResolution.ResolveWeaponRuleNames` is the generalized (public) form of the spell-side
    private resolver, so both paths parse `"Blast(3)"` through `SpecialRuleEntryParser` +
    `ArmyListRuleResolution.ResolveForScope(..., ERuleScope.Weapon, ...)` and share its warn-and-skip
    tolerance.
  - Tests: 4 new (3 in `PreAttackRuleIntegrationTests`, 1 in `StrafingRuleIntegrationTests`). Suite
    1738/0, full `dotnet build` clean, headless smoke exit 0 (tie, 4 rounds).
  - Mutation-checked both fixes independently: disabling the `WithRules` attachment reddens exactly
    `DealHitsAbility_WithBlast_MultipliesHitsThroughTheSharedFold`; reverting the Strafing AP to 0 reddens
    exactly `Stage_HonoursTheDealHitsArmorPenetration`. Nothing else moves.

## Decisions

- **Resolve at the stage, not at army load** (owner sign-off 2026-07-19). A spell has an army-load site and
  pre-resolves there; an ABILITY does not, because it may be conferred at runtime by an aura or grant, so
  there is no load-time attachment point to hang resolved rules on. Resolution therefore happens at
  dispatch via `RuleEvaluator.RuleResolver`. A null resolver (bare harness / pre-#095 resume) degrades to
  AP-only rather than throwing — pinned by `DealHitsAbility_WithBlastButNoResolver_StillDealsBaseHits`.
- **Two stale premises corrected while building** (the recurring #197 pattern):
  1. `RuntimeSpell`'s doc claimed "the resolver isn't reachable from a stage". That stopped being true when
     #100 slice 1 threaded the resolver into `RuleEvaluator`; the comment is now corrected in place rather
     than left to mislead the next reader (it is what made this look harder than it was).
  2. `PreAttackStage`'s doc claimed the same, citing `SpecialRulesAudit.md`.
- **Extract rather than duplicate.** The Blast living-model cap, the injection-before-multiply ordering, and
  the per-hit AP split are all subtle and were correct in exactly one place. Copying them into two more
  stages would have created three drifting implementations of one rule; the fold is now single-sourced.
- **`ERuleScope.Weapon` is the right gate** for `WithRules` names: Blast is weapon-scoped in the catalog, and
  reusing `ResolveForScope` means a mis-scoped name warns and skips exactly as it does at army load instead
  of silently attaching something nonsensical.
- **Test harness note worth keeping:** a wound-assignment request is only raised when there is an assignment
  *choice*. 3 hits into a 3-model unit wipes it and auto-assigns, so a test asserting on the request must
  leave the target with more models than incoming wounds. Cost me a debugging round; the new Strafing test
  documents it inline.

## Deferred / not in this slice

- **`TeleportStage` never calls `OperationExecutor.Execute`** after resolving its ability — it applies token
  ops only. Correct today *because* `Effect.Teleport` is a deliberate no-op marker and the stage does the
  placement itself, so Teleport works; but any executable effect routed through that stage would be
  silently dropped. One defensive line, outside this item's scope. Flagged to the owner 2026-07-19, not
  fixed.
- **#164's siblings in the audit follow-up list are untouched:** #173 (`RequiredToken`/`RequiredRule` never
  ported into `SpellTargeting`) and #174 (`SingleModel` + `MaxCount > 1` wound misattribution). Both were
  checked this session and are **dormant in shipped data** — all 23 authored single-model spells use
  `maxCount: 1`, so #174 cannot currently fire. Recorded here so the "why didn't you do them at the same
  time" question has an answer.

## Outcome

_(written when the item closes)_
