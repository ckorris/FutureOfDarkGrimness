# 010 — Custom actions branch in ChooseActionStage

**Status**: done
**Related**: #042 (rule framework — activated abilities), #033/#034 (Caster subsystem + spell content — the deferred destination), #051 (charge-state threading, similar context plumbing)

## Goal
Replace the hardcoded `hasCustomActionsAvailable = false` in `ChooseActionStage` with a real seam: a special rule that carries an `ActivatedAbility` triggered at `EHookID.Activation_OnActionChoice` surfaces as a selectable action in the Choose Action list. Selecting it resolves the ability generically and loops back to the action chooser.

**This is the mechanism only — NOT Caster.** Caster is the motivating example (activating a Caster unit should make "Cast" an option), but the full Caster/spell subsystem (spell-token economy per round, 4+ casting roll, ±1 friendly assist within 18", spell content) is #033/#034. #010 delivers *the ability for special rules to add actions to the choose-action stage* and proves it with a trivial test-corpus rule.

## Decisions (forks resolved with the user, 2026-06-21)
- **Cast/custom actions are layered, not action-consuming.** Choosing a custom action does NOT set `HasMoved`/`HasAttacked`; it loops back to Choose Action so the unit can still Move/Shoot. Matches GDF/OPR — casting isn't an action there, it's a free overlay during activation. Re-offering is gated by the ability's own `Cost` (`OncePerActivation` marker / spell-token consumption), already enforced by `RuleEvaluator.IsAffordable`.
- **One action per offer, labelled by `offer.RuleName`.** An `ActivatedAbility` has no display name; `RuleName` is the natural generic label (no schema change). Two rules → two actions. Grouping a single rule's many spells under one "Cast" entry is a Caster-internal concern → #033.
- **Generic resolution via the existing offer/resolve path.** `ChooseActionStage` fires `Activation_OnActionChoice`, `GatherOffers`; on selection `CustomActionStage` runs `ResolveAbility` against the bearer and applies the operation queue via `OperationApplier.ApplyTokenOperations` (cost markers + token-granting effects). Mirrors how `StrafingStage`/`DeterminePlayerTurnStage` consume offers stage-side.
- **Resolve against the bearer (self-target), no target-selection UI in #010.** Keeps the seam minimal and headless-testable. Target-selecting custom actions (resolving the ability's `TargetSelector` through a player request) are deferred until a real rule needs them.

## Deferred (recorded — not silently cut)
- **Target-selecting custom actions** — `TargetSelector` resolution through a player request. No live rule needs it yet.
- **Child-pipeline effects** (damage/save spells, like Strafing's synthetic-hit pipeline) — per-effect; belongs with spell content (#034).
- **Executable-operation effects** (movement etc. needing `IOperationServices`) — handled per-effect by the consuming stage today (Strafing/Reactivate read their own op inline); the generic seam applies token operations only.
- **Whole Caster/spell subsystem** (tokens-per-round, casting roll, assist) → #033/#034.
- **`EActionType.Custom`** — the enum's own comment anticipates a new value; only needed if a rule must condition on "a custom action happened" via `Condition.ActionTypeIs`. Deferred until something needs it.

## Plan
1. `ActionChoiceContext` (`Rules/Dispatch/Contexts/`) — `IHookContext` + `IHasActingUnit`, `Hook => Activation_OnActionChoice`. Mirrors `NextActivatorRequestedContext`.
2. `UnitActionContext` — carry the chosen `AbilityOffer` (`PendingCustomAction` + set/clear) so the chooser hands the selection to `CustomActionStage`.
3. `ChooseActionStage` — gather offers; `hasCustomActionsAvailable = offers.Count > 0`; add one option per offer (label `RuleName`, collision-guarded + logged); route the chosen one to a new `ToCustomAction` binding.
4. `CustomActionStage` (child of `MainUnitActionStage`) — resolve the pending offer against the bearer, apply token ops, log, loop back via `OnFinished`. Does not touch the move/attack flags (layered).
5. `MainUnitActionStage.PopulateTransitions` — add the child + bind `chooseAction.ToCustomAction → custom` and `custom.OnFinished → chooseAction`.

Tests: `CustomActionRuleIntegrationTests` — (a) eval: `GatherOffers(ActionChoiceContext)` yields the offer; (b) stage: `ChooseActionStage` surfaces + routes; (c) stage: `CustomActionStage` pays cost + applies effect + leaves move/attack flags untouched; (d) once-per-activation re-offer gate.

## Outcome
**DONE 2026-06-21** (engine `d56167b`, bump pending in the same superproject commit as this ledger). The seam is live: `ChooseActionStage` fires `Activation_OnActionChoice`, gathers offers, and surfaces one option per rule (collision-guarded + logged), routing the chosen one to the new `CustomActionStage`, which resolves it against the bearer, pays the cost via `OperationApplier`, and loops back without setting `HasMoved`/`HasAttacked` (layered). New `ActionChoiceContext`; `UnitActionContext` carries the chosen `AbilityOffer`. Custom actions surface through the **existing** `StringSelectionRequest` resolvers (CLI + GUI) — no new resolver needed. Proven by `CustomActionRuleIntegrationTests` (4 cases, red→green); full engine suite **615/0**, app build clean, headless smoke exit 0. The first live consumer (a real Caster/spell rule in `CoreRuleCatalog`) is **#033/#034** — until one lands there's no user-visible custom action in the app, so this item's verification is at the test level by design (the existing built-in test army carries no action-choice rule). Deferred corners recorded above (target-selecting custom actions, child-pipeline/executable-op effects, `EActionType.Custom`).

## Notes
- 2026-06-21: **Done.** TDD: wrote `CustomActionRuleIntegrationTests` first (2 stage tests genuinely red — "Cast" not an option / effect token not granted — while the 2 eval-layer tests passed, proving the `GatherOffers` foundation), then implemented surfacing + `CustomActionStage` + `MainUnitActionStage` wiring to green. Engine committed `d56167b`.
- 2026-06-21: Item opened. Forks resolved with the user (above). Branch `010-custom-actions` cut in both repos. Survey confirmed the machinery already exists — `Activation_OnActionChoice` hook value present but never fired / no context class; `GatherOffers`/`ResolveAbility`/`OperationApplier` proven by three live consumers (`StrafingStage`, `DeterminePlayerTurnStage`, `DeployUnitStage`). Caster not in `CoreRuleCatalog` (intentionally — out of scope). Writing failing tests first.
