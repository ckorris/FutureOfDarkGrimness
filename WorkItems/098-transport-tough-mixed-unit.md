# 098 — Transport capacity-by-Tough mixed-unit corner

**Status**: todo
**Related**: #035 (Transport), #006 (Hero — per-model Tough/hero), #093 (per-model special rules)

## Goal
Harden the Transport(X) capacity / ride-eligibility rules for a **mixed-Tough unit** — a single unit that contains models with different ride costs/caps (e.g. a Tough≤6 hero joined to Tough≤3 grunts, or a unit of mixed-Tough models). The rule: a normal model occupies 1 space; a Hero up to Tough(6) or a non-Hero up to Tough(3) occupies 3 spaces; models above their cap can't ride.

`TransportUtilities` already computes per-model space cost (`GetModelSpaceCost`) and per-model ride eligibility (`CanModelRide`), and `GetUnitSpaceCost` sums living models. The corner: `CanUnitEmbark`'s per-model hero detection for a *mixed* unit (which model is the hero, applying the hero cap to it and the non-hero cap to the rest), and confirming the summed 3-space accounting is right when a unit mixes 1-space and 3-space models. Add targeted unit tests for the mixed cases (hero+grunts within caps embark; a unit with one over-cap model can't; cost sums correctly).

"Done" = mixed-Tough units embark/are-rejected correctly and are covered by tests.

## Notes
- 2026-06-21: Opened from the #035 slice A deferral. The pure-Tough single-model and uniform cases are already unit-tested in `TransportUtilitiesTests`; this is the mixed-unit hero-detection corner that was explicitly deferred.

## Decisions

## Outcome
