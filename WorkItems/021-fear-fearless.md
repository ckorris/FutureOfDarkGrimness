# 021 — Fear & Fearless (morale-impacting special rules)

**Status**: in-progress
**Related**: continuation of the morale work. Depends on #090 (decisive rolls — a correct morale roll for Fearless to ride on). Built on #042 rule-dispatch architecture. On branch `021-morale-rules` (both repos).

## Goal
Wire the two dormant morale-impacting special rules whose dispatch is already proven by `SpecialRuleTests` but which no stage consumes:
- **Fear(X)** (Slice A): a unit counts as dealing +X extra wounds for the who-won-melee check only (no real wounds), which can flip the loser — and thus who tests morale.
- **Fearless** (Slice B): when a unit fails a morale test, it rolls a fresh die and passes on a 4+ (regardless of Quality). Plus the generic **morale roll modifier** plumbing (`Morale_OnPreMoraleTest`) that other rules (e.g. a future Courage) ride.

"Done" = both rules in `CoreRuleCatalog`, the two stages (`DetermineMeleeWinnerStage`, `RollForMoraleStage`) fire their hooks and consume the operations, with tests. **Out of scope**: Courage / auras (needs proximity-based rule sourcing); the `Morale_OnShakenApplied/Cleared` hooks (no rule needs them yet).

## Decisions
- **Fear sums per side via two Actor-seat evaluations.** Fear's default seat is Actor and its bonus adds to *its own* wounds-dealt, so `DetermineMeleeWinnerStage` evaluates `Melee_OnMeleeResolution` once per unit (each as Actor) and folds each side's `ExtraMeleeWoundCount` (already resolved to an int by the dispatcher) into that side's total. Two single-participant calls attribute the bonus cleanly; the shared `MeleeResolutionContext(attacker, defender)` is reused for both.
- **Fearless reuses the decisive-roll path** (#090): the fresh second-chance die is a single decisive roll, 4+ = pass. (Slice B.)

## Notes
- 2026-06-14: **Slice A (Fear) complete.** Added `Fear(X)` to `CoreRuleCatalog` (passive at `Melee_OnMeleeResolution`, `ExtraMeleeWoundCount(Arg(0))`) and to the `All` registration list. Wired `DetermineMeleeWinnerStage` to fire the hook per side and fold the bonus before the winner comparison, replacing the long-standing "fear/fearless can't apply" TODO. New `FearRuleIntegrationTests` (4): Fear flips tie→win, pulls loss→tie, X comes from the argument, and deals no real wounds. Updated `SpecialRuleRegistryTests` — Fear moved off the unimplemented-phantom list and onto the numeric-rules list (it reads an arg). Engine suite 513/0; app build clean; headless smoke exits 0 (melee resolution unaffected when no unit has Fear). Commits: engine `<pending>` / bump `<pending>`.

## Outcome
_(open — Slice B pending)_
