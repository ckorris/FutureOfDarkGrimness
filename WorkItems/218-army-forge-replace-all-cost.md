# 218 — Army Forge "Replace All" charges per-model instead of once

**Status**: todo
**Related**: #156 (Army Forge builder), #197's decision 5 in `WorkItems/156-army-forge-builder.md` (the cost-scaling design that produced this), `ListCompiler.cs:151,300-308`

## Goal
For an `UpgradeAffects.All` replace section, `ListCompiler` resolves `applications = availableMax` (the unit's model count) and then does `unit.PointCost += option.Cost * applications` — so "replace all X with Y" charges `option.Cost` once *per model*. The user's playtest report is that this reads as far too expensive versus how OPR pricing actually works for "all" replace sections (typically a single flat cost to upgrade the whole unit, not per-model). Needs a rules check against real OPR book data (are "all" options authored with a low per-model cost that's meant to be multiplied, or a section-level flat cost?) before changing `ListCompiler`'s cost math — this was a deliberate design decision (156's decision 5), not an oversight, so confirm the correct convention first.

## Notes
- 2026-07-15: Filed from user playtest feedback ("Replace All options... should be global, pay once for all, much cheaper").

## Decisions

## Outcome
