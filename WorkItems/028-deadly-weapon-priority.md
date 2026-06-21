# 028 — Deadly weapon priority (resolve first; wounds don't carry across models)

**Status**: in-progress — implemented on branch `028-deadly-weapon-priority` (both repos), engine green 633/0, headless smoke exit 0. Not yet merged to master.
**Related**: #042 (rule dispatch / Deadly wound-mult), #027 (weapon-scoped rules), #032 (Deadly impl umbrella), #024 (wound-split validation)
**Branch** (both repos): `028-deadly-weapon-priority` — submodule + superproject branched from master.

## Goal
The Deadly(X) rule has two halves. "Done" = both enforced:
1. **Wounds don't carry across models** — each Deadly clump (one failed save × X) lands entirely on one model; overkill is lost.
2. **Resolve first** — when a unit attacks with several weapons and some carry Deadly, the Deadly weapons must be used before the non-Deadly ones, so a clump removes whole models before normal wounds spread.

## Notes
- **2026-06-21 — scope split discovered.** Half #1 was *already implemented* before this item was picked up: `AssignWoundsStage.ConfineToClumps` (AssignWoundsStage.cs:184-204) confines each clump to one model with overkill lost, covered by `WoundRuleIntegrationTests` (Deadly vs single-wound = wasted, vs Tough = full clump, no carry-over). So this slice is only half #2, the **ordering**.
- **2026-06-21 — implemented half #2 (ordering).** Each weapon already resolves fully (hit→save→wound→apply→remove-dead) before the next weapon is chosen, so the entire mechanic is *gating the weapon picker* so Deadly weapons must be chosen first. Closed the two long-standing TODOs (`ChooseRangedAttackStage.cs:32`, `ChooseMeleeWeaponStage.cs:23`).
  - New shared query `Rules/Dispatch/WoundPriorityQueries.MustResolveFirst(attacker, weapon, evaluator)` — evaluates the weapon at the `Shooting_OnPreApplyWound` hook (non-logging, mirroring `SightRuleQueries`) and returns true when it nets a wound multiplier > 1. Capability-based, so it's not tied to the literal name "Deadly" — any future resolve-first wound rule is picked up automatically.
  - **Ranged** (`ChooseRangedAttackStage`): after building weapon options, `ApplyDeadlyFirstGating` marks every non-Deadly weapon's targets `UnselectableReason = "Must fire Deadly weapons first."` — but only when at least one Deadly weapon actually has a fireable target this action (an out-of-range Deadly weapon must not lock out the rest). Runs before the no-fireable-option check so gating composes with it.
  - **Melee** (`ChooseMeleeWeaponStage`): while an un-used Deadly weapon is in `AvailableWeapons`, the non-Deadly available weapons are offered as `InvalidOption`s ("Must attack with Deadly weapons first."). Melee has no range/LoS gating so it's unconditional.
  - **No app-side change needed:** verified all six resolvers (CLI/AI/GUI × ranged/melee) already honor `UnselectableReason` / `InvalidOptions`, so the AI picks the only selectable (Deadly) weapon, and once it's fired (leaving `AvailableWeapons`) the gate lifts for the rest.
  - Tests: `ChooseRangedAttackStageTests` +2 (Deadly fireable → non-Deadly gated; Deadly out of range → others *not* gated); new `DeadlyWeaponPriorityTests` ×4 (`MustResolveFirst` true/false; melee gates non-Deadly; melee with no Deadly leaves all selectable). Suite 627→633/0.

## Decisions
- **Gate the picker, don't reorder/auto-fire.** Each weapon already self-contains its full hit→wound→remove resolution, so forcing Deadly *selection* first is sufficient; no need to batch or pre-resolve. Player keeps target choice.
- **Gray-out-with-reason over filtering** (user call 2026-06-21): non-Deadly weapons stay visible but disabled with an explanatory reason, reusing the existing `UnselectableReason`/`InvalidOption` infra both stages already use for target-limit / already-used. More transparent than vanishing options.
- **Capability-based detection over name match.** Query folds the pre-apply-wound ops through `WoundModifierSink` and checks `NetMultiplier > 1`, rather than string-matching "Deadly". Defender is irrelevant to Deadly (unconditional `Always`, multiplier ignores target), so the attacker is passed as a neutral stand-in; documented that a future *defender-specific* wound-mult rule would need the real target threaded in (not available at ranged weapon-choice time).
- **Ranged gates conditionally, melee unconditionally.** Ranged only gates when a Deadly weapon can actually reach something (range/LoS), so a useless Deadly weapon doesn't soft-lock the picker; melee has no reach gating in the picker so any available Deadly weapon gates.

## Outcome
_(pending merge + GUI hand-verify)_
