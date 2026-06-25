# 098 — Transport capacity-by-Tough mixed-unit corner

**Status**: done 2026-06-25 — already-correct; closed by adding the deferred mixed-case tests. Engine suite 811/0, headless smoke exit 0. No production code change.
**Related**: #035 (Transport), #006 (Hero — per-model Tough/hero), #093 (per-model special rules)
**Branch** (both repos): `098-transport-tough-mixed-unit` — submodule + superproject branched from master.

## Goal
Harden the Transport(X) capacity / ride-eligibility rules for a **mixed-Tough unit** — a single unit that contains models with different ride costs/caps (e.g. a Tough≤6 hero joined to Tough≤3 grunts, or a unit of mixed-Tough models). The rule: a normal model occupies 1 space; a Hero up to Tough(6) or a non-Hero up to Tough(3) occupies 3 spaces; models above their cap can't ride.

`TransportUtilities` already computes per-model space cost (`GetModelSpaceCost`) and per-model ride eligibility (`CanModelRide`), and `GetUnitSpaceCost` sums living models. The corner: `CanUnitEmbark`'s per-model hero detection for a *mixed* unit (which model is the hero, applying the hero cap to it and the non-hero cap to the rest), and confirming the summed 3-space accounting is right when a unit mixes 1-space and 3-space models. Add targeted unit tests for the mixed cases (hero+grunts within caps embark; a unit with one over-cap model can't; cost sums correctly).

"Done" = mixed-Tough units embark/are-rejected correctly and are covered by tests.

## Notes
- 2026-06-21: Opened from the #035 slice A deferral. The pure-Tough single-model and uniform cases are already unit-tested in `TransportUtilitiesTests`; this is the mixed-unit hero-detection corner that was explicitly deferred.
- **2026-06-25 — investigated; implementation already correct.** Audited `CanUnitEmbark` → `IsHeroModel` → `CanModelRide` / `GetUnitSpaceCost` for the mixed case. `IsHeroModel` resolves the hero of a *joined* unit by `HeroAttachment.HeroModelId` (so a host of grunts + one joined hero is handled per-model: hero→cap 6, grunts→cap 3), and falls back to the single-model+Hero-rule branch for a lone hero. Space cost sums `GetModelSpaceCost` per living model, so a mixed 1-space/3-space unit totals correctly. No defect found — the deferred work was purely the missing coverage.
- **2026-06-25 — added 6 mixed-case tests** to `TransportUtilitiesTests` (new `MakeHeroJoinedUnit` / `MakeSoloHeroUnit` helpers built via the real `UnitData.AttachHero` / Hero-rule paths): joined hero(T6)+grunts(T3) embarks (proves the hero gets cap-6 via detection, not cap-3); hero over cap (T7) rejected; one grunt over cap (T4) rejected though the hero is fine; a non-Hero Tough(6) in a plain multi-model unit rejected (can't borrow the hero cap); a solo Hero(T6) unit embarks; mixed space cost sums (hero 3 + two standard 1 = 5). Suite 805→811/0; headless smoke exit 0. No production code touched.

## Decisions
- **Closed as a coverage gap, not a fix.** The mixed-Tough logic was already correct; following the codebase's "no speculative code" stance (cf. #104), nothing was hardened defensively against states that can't occur (e.g. a multi-model unit carrying the Hero rule with no `HeroAttachment`). The value delivered is the regression-guarding tests the original slice deferred.

## Outcome
Done 2026-06-25. `TransportUtilities`' mixed-Tough embark logic was verified correct and is now covered by 6 targeted tests exercising `CanUnitEmbark`'s per-model hero detection and summed space accounting on genuinely mixed units (joined hero + grunts, solo hero, over-cap rejections). No production code change — this closed the #035 slice-A test deferral. Suite 811/0.
