# 218 — Army Forge "Replace All" charges per-model instead of once

**Status**: fixed for Affects.All, awaiting GUI hand-verify; Affects.Any convention still unverified
**Related**: #156 (Army Forge builder), #197's decision 5 in `WorkItems/156-army-forge-builder.md` (the cost-scaling design that produced this), `ListCompiler.cs:151,300-308`

## Goal
For an `UpgradeAffects.All` replace section, `ListCompiler` resolves `applications = availableMax` (the unit's model count) and then does `unit.PointCost += option.Cost * applications` — so "replace all X with Y" charges `option.Cost` once *per model*. The user's playtest report is that this reads as far too expensive versus how OPR pricing actually works for "all" replace sections (typically a single flat cost to upgrade the whole unit, not per-model). Needs a rules check against real OPR book data (are "all" options authored with a low per-model cost that's meant to be multiplied, or a section-level flat cost?) before changing `ListCompiler`'s cost math — this was a deliberate design decision (156's decision 5), not an oversight, so confirm the correct convention first.

## Notes
- 2026-07-19: **Convention check done, from real data - the user's report was right.** The Havoc Brothers
  share list (`iaP7jaKVjbUD`) takes a 10-pt "Replace all Heavy Rifles and CCWs" on two 5-model units.
  Army Forge's `listPoints` (1120) exceeds the base-cost sum (1100) by exactly **20** - i.e. 10 per unit,
  flat. Per-model would have been 100, and 1200 is precisely what our compiler computed. Unambiguous.
  Fixed in engine 6085388: `ListCompiler` charges `option.Cost` once when `Affects == All`. Cost and
  effect deliberately part ways - every eligible model still gets the swap, only the charge is levied
  once. The Havoc list now reconciles **1120 pts both ways** (was 1200 vs 1120), and plays a full
  headless game at 1120. 1721 engine green.
  `Compile_Gunners_ReplaceAll_ScalesCostByTargetCount` had pinned the overcharge in its own name -
  renamed to `..._AppliesToEveryModel_ButChargesOnce` and corrected (165 -> 135), plus new regression
  tests pinning BOTH conventions.
- **Corpus exposure of the fix**: 200 priced `Affects.All` options across the bundled books were
  overcharging. Any Forge-built list using one gets cheaper - expected, not a regression.
- **REMAINING (explicitly not verified):** `Affects.Any` per-application pricing. Structural evidence is
  good - Army Forge emits one `selectedUpgrades` entry PER APPLICATION for "any" sections (3 entries on a
  3-model Elemental Strikers unit) versus ONE per unit for "all" - so the wire format mirrors the pricing
  we assume. But it is not arithmetically confirmed: neither tested share list contains a PRICED "any"
  option (the High Elf ones are all #219-unpriced). **1185 priced `Any` options in the corpus ride on
  this assumption.** To settle it: a share link where someone took a priced "Replace any X" on 2+ models,
  then check whether `listPoints - base sum` equals the option cost x picks (per-application, our current
  behavior) or the option cost alone (flat).
- 2026-07-15: Filed from user playtest feedback ("Replace All options... should be global, pay once for all, much cheaper").

## Decisions
- 2026-07-19: A "Replace all" price is FLAT per unit. This reverses #156's decision 5 (cost scales with
  models touched) for `Affects.All` only; `One` and `Any` keep per-application pricing. The deciding
  evidence is Army Forge's own arithmetic on a real list, not a reading of the printed rulebook.

## Outcome
Affects.All fixed and confirmed end-to-end against the source list. Item stays open for the Affects.Any
verification above and a GUI hand-verify that Forge-built list prices drop as expected.
