# 254 — Wound-driven morale must test every wounding activation at half or less, not only on the crossing blow

> _Renumbered from #252 on 2026-07-21 (reconciliation 17) — collided with origin/master's #252 (field-texture cover proximity). The engine commit message was amended to #254 before push._

**Status**: DONE 2026-07-21 — implemented, suite green, archived same day (engine-logic only; no new presentation surface to hand-verify).
**Related**: #009/#091 (built the wound-driven trigger + `MoraleUtilities`), #202 (once-per-defender ranged morale pass).

## Goal
Live-play bug report (2026-07-21): a Great Monolith (Tough 18, Robot Legions) at 7-8 remaining wounds was shot down to 5 and took no morale test — the log showed `Resolving ranged morale.` and nothing after. Root cause: `MoraleUtilities.CrossedIntoHalfStrength` only fired the test on the blow that *crossed* the unit into half strength, so a unit already at half or less never tested again. Per the rule (GF v3.5.1), a unit tests at the end of ANY activation whose wounds leave it at half or less of its size/Tough — there is no morale case anywhere that keys on crossing the boundary (owner-confirmed). Melee is unaffected (the loser always tests; Rout-at-half reads current state).

## Notes
- 2026-07-21: Fix, engine (submodule): `CrossedIntoHalfStrength` -> `WoundsLeftUnitAtHalfStrength` — "took wounds this action (remaining dropped below the first-targeted baseline) AND now at half or less". The `RemainingWoundsAtStart` baseline keeps its two original jobs (at-most-one test per shoot action; measured against pre-volley wounds) and gains a third: a targeted-but-unwounded unit at half doesn't test. Sole production caller is `ResolveRangedMoraleStage` (dangerous terrain was de-wired earlier as a rules bug); its two log lines reworded from "was reduced to half strength" to "is at half strength or less". Tests: `AlreadyAtHalfBeforeShooting_NoTest` flipped to `AlreadyAtHalfBeforeShooting_WoundedAgain_TestsAgain` (it had encoded the bug) + new `AlreadyAtHalf_TargetedButUnwounded_NoTest`; Fearless wound-driven tests unaffected. Engine 1762/0, full build clean, headless smoke exit 0.

## Outcome
2026-07-21: Done. A unit at half strength or less now takes a ranged morale test in every activation where shooting wounds it; taking no wounds still means no test, and multi-weapon actions still test at most once. Behavior pinned by `ResolveRangedMoraleStageTests` (8).
