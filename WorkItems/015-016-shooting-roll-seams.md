# 015 / 016 — Attack-count & hit→save effect seams in the shooting/melee roll pipeline

**Status**: DONE — merged to master 2026-06-21 (engine `36e3631`/`1b16dc6` in master pin `5efe827`; suite 627/0; headless smoke exit 0)
**Related**: #042 (rule dispatch), #032 (weapon rules), #030/#051 (combat-modifier rules), #028 (Deadly)
**Branch** (both repos): `015-016-shooting-roll-seams` — submodule branched from `14890d5`, superproject from `master`.
**Commits**: engine `36e3631` (#015), `1b16dc6` (#016); superproject bump — see git log.

## 2026-06-21 — merged to master / closed
Confirmed both facet commits are ancestors of submodule master and of the superproject's pinned submodule pointer (`5efe827`): `36e3631` (#015) and `1b16dc6` (#016) both report `IN pinned`. The `origin/015-016-shooting-roll-seams` ref is stale (predates the master merges) — disregard it. Re-verified on master: code present (`DetermineHitRollStage` + `DetermineHitRollResults.AttackCount`; `SuccessfulHitInfo.SaveModifier`), engine suite **627/0**, headless smoke **exit 0** with `Rolled … out of N total attacks` confirming `AttackCount` flows live. Index lines flipped from `[~]` (stale "uncommitted") to `[x]` under ## Done. No "Awaiting verification" hold — both are seams with integration-test coverage and no live consumer yet, so there is no new in-app behavior to hand-verify.

## 2026-06-21 — finished close-out
Headless smoke passed (exit 0, full 4-round game to tie; `Rolled … out of 1 total attacks` confirms `AttackCount` flows). Engine committed submodule-first as two facet commits (above), then superproject bump. Untracked `FutureOfDarkGrimness/ExampleArmies/` deliberately left uncommitted (unrelated). Merge to master is a separate step, not yet done.

## Goal
- **#015**: give attack-count its own modifier-fold point, computed where the to-hit roll is determined (not inline in the roll stage), so attack-count rules have a clamp-safe home.
- **#016**: provide a way for effects carried on specific hits to affect the save (wound) rolls, per hit group rather than one unit-wide scalar.

## Where I stopped (2026-06-19)
All edits done; **engine suite 569/0 green**, full `dotnet build` **succeeded (0 errors)**.
**NOT yet done**: headless smoke, commits, index update. Resume there.

### Remaining steps to finish
1. Headless smoke: `printf "2\n2\n" | dotnet run --project FdgRaylib/FdgRaylib.csproj -- --headless` → expect exit 0 (touches a playable path: shooting/melee resolution).
2. Commit **submodule first** (two commits: #015 rename+relocate, #016 per-hit save modifier), then bump superproject pointer + this ledger in a second superproject commit. Do NOT commit the untracked `FutureOfDarkGrimness/ExampleArmies/` (unrelated).
3. Flip the #015 / #016 lines in `WorkItemsList.md` to `[~]` with a pointer here (started below).

## What shipped on the branch

### #015 — attack count relocated
- Renamed stage `DetermineHitRollNeededStage` → **`DetermineHitRollStage`** and result `DetermineHitRollNeededResults` → **`DetermineHitRollResults`** (it now determines *both* the attack-dice count and the to-hit threshold, so "Needed" was lying). `git mv` preserved history (file + folder).
- `DetermineHitRollResults` gains `float AttackCount`; the stage computes `WeaponType.Attacks * WeaponCount` beside the threshold, with a comment marking it as the attack-count modifier-fold point (mirror of the hit-roll modifier fold).
- `RollToHitStage` now queries the result **before** rolling and rolls `AttackCount` instead of recomputing the product.
- Shared by both `FireStage` and `SwingMeleeWeaponStage` (melee + ranged both benefit).
- **Deferred (recorded):** no rule *produces* an attack-count modifier yet — only the seam exists. Build the producer when a rule (e.g. Rapid-Fire-style) needs it.

### #016 — per-hit save effect
- `SuccessfulHitInfo` gains `int SaveModifier` (default 0); `DetermineSaveRollsNeededStage` applies it per hit group (`saveNeeded = baseDefenseWithAP - hits.SaveModifier`), stacking on the unit-wide `RollToHitResults.SaveModifier`. Sign convention matches (negative raises threshold).
- **Deferred (recorded):** migrating Rending off its blunt unit-wide scalar onto this per-hit path is a separate slice (its test already flags per-hit AP as deferred).

### Tests
- `HitRollRuleIntegrationTests.AttackCount_IsWeaponAttacksTimesWeaponCount` (2×3 → 6).
- New `PerHitSaveEffectIntegrationTests` (per-hit raises threshold / no-op default / stacks with unit-wide).
- 5 seed sites updated to `new DetermineHitRollResults(4, attackCount: 1)` (Blast/Extra/Rending/WeaponScoped/Thrust); type/stage identifiers renamed across 7 test files + a `CoreRuleCatalog` comment (sed pass).

## Decisions
- **No new stage for #015.** The pipeline already has a pre-roll "determine" stage; attack count shares that timing and is consumed one line later — relocation, not a third stage. (See the earlier discussion: stages are for queryable results / distinct rule-timings / decision points, not modifier math.)
- **#016 was never a stage question** — it's data plumbing on `SuccessfulHitInfo` between two existing stages.

## Follow-up to capture (user request, 2026-06-19)
**Make "melee-only" / "shooting-only" a first-class condition** in the #042 data-driven `Condition × Effect` system. Today combat-kind gating is ad hoc per rule: `IsMelee` is threaded through `HitRollModifierContext`/`HitRollCompleteContext`, and rules hand-gate on it (Indirect = `Not(IsMelee)`, Thrust = melee+charging, Furious's melee gate, #051's charging gate). We want a declarable Condition so a rule (or these new #015/#016 effects) can say "applies in shooting only" / "melee only" as data, not wiring. Relevant to #015 (attack-count mods are often shooting-only, e.g. Rapid Fire) and #016 (per-hit save effects may be combat-kind-specific). Likely a small Condition addition in `Rules/Dispatch` (CoreRuleCatalog / condition evaluator) reading the combat-kind already on the contexts. **Tracked as #093** (folded into [WorkItems/093 — per-model special rules](093-per-model-special-rules.md) on 2026-06-21 after a number collision; see its "Folded in: combat-kind condition" section).
