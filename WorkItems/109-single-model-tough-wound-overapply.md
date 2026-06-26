# 109 — Single-model Tough unit auto-killed by a sub-lethal hit

**Status**: done
**Related**: #023/#024 (wound-assignment ordering/validation in the same `AssignWoundsResults`), #028 (Deadly clump confinement in the same stage)
**Branch** (both repos): `149-base-shapes`

## Goal
A single multi-wound model (a Tough monster) shot for fewer wounds than it has must **survive**, taking exactly the wounds dealt. Reported bug (user, 2026-06-26): firing 10 Heavy Rifles at a Tough(12) Carnivo-Rex — 9 hits, 6 saved, **3 unsaved** — the log then read "Applying **12** wounds killed 1 model, killing the unit." The lone monster died to a 3-wound hit, with no Deadly/wound-multiplier in play.

## Notes
- 2026-06-26: **Fixed.** Root cause in `AssignWoundsStage.RunStage`: the "single living model → auto-resolve, no player choice" branch built `new AssignWoundsResults(defendingUnit, defenderRemainingWounds)` — the model's **full remaining health** (12) — instead of `totalWoundsDealt` (3). AutoFill then poured 12 into the model and `ApplyWoundsStage` killed it. The branch is only reached when `0 < totalWoundsDealt < defenderRemainingWounds` (the kill case is handled by the earlier `>=` branch), so it bit exactly single-model units with **more than one wound**; a 1-wound lone model never enters it (its `totalWoundsDealt` is either 0 or already `>= remaining`), which is why it went unnoticed. One-line fix: assign `totalWoundsDealt`. (We're past the `>=` branch, so `totalWoundsDealt < remaining` and AutoFill always fits.)
- Verified: engine build clean; **engine suite 840/0** incl. a new regression `WoundRuleIntegrationTests.SingleToughModel_SubLethalHit_AssignsOnlyWoundsDealt` (lone Tough(12), 3 failed saves → `TotalWoundsToAssign == 3`, model survives); headless smoke exit 0.

## Decisions
- **Minimal fix over deleting the branch.** The trailing `else` (player-choice) branch would also resolve a single model correctly (its `HasRemainingChoice` is false → AutoFill on `totalWoundsDealt`), so the single-model branch is strictly a redundant fast-path. Kept it and corrected the one wrong argument rather than deleting it, to keep the change surgical and obviously correct.

## Outcome
`AssignWoundsStage`'s single-living-model auto-resolve branch now assigns the wounds actually dealt, not the model's full remaining wounds, so a lone Tough model survives a sub-lethal hit. Submodule one-liner + a regression test; engine suite 840/0, build clean, headless smoke exit 0. Bumped into the superproject with the #108 engine work.
