# 263 — Off-table (Ambush reserve) units chargeable at the origin

**Status**: done (engine); awaiting GUI hand-verify
**Related**: #202 (made reserve explicit unit state), #206 (forced-charge standoff gate), #029 (aircraft off-table token)

## Goal
A unit that is not on the battlefield (in reserve, embarked, or flown off the edge) can never be
charged, force a charge, or be chosen as a melee defender. The melee/charge family respects
`GetIsOnBattlefield()` like every other targeting path already does, enforced at the shared
chokepoint so future melee callers inherit the check, plus a loud diagnostic backstop if any
attack path ever wounds an off-table unit again.

## Notes
- 2026-07-23: Implemented + committed (engine `faa8ab5`). 8 new tests in
  `OffTableTargetingTests` (reserve/embarked unreachable both directions, standoff not forced,
  wound-backstop warns, on-table controls incl. no-warn-on-ordinary-kill); suite 1917/1917 green,
  full build clean, headless smoke exit 0. `VerticalMeleeRangeTests` re-anchored from the origin
  to x=10 (a model centred at (0,0) is the never-placed marker, so units parked there now
  correctly read as off-battlefield - the old fixtures relied on the pre-#202 coordinate
  convention). GUI hand-verify still pending: hold a unit in Ambush, deploy an enemy near the
  table corner, confirm round-1 Charge is not offered/forced against the reserve unit.
- 2026-07-23: Reported from a live game: HEF Shifters held in Ambush were charged and nearly
  wiped in round 1 by Knight Brothers units deployed near the table's bottom-left corner - the
  reserve models' stored positions sit at the origin, and the melee path read that as real
  geometry. Root cause: the melee/charge family never consults `GetIsOnBattlefield()`:
  - `ChooseActionStage.GetCanCharge` - offered Charge because the reserve unit "was in range".
  - `ChooseActionStage.AnyEnemyWithinStandoff` (#206) - worse, FORCED the charge (Pass gated
    off) for units within 1" of the origin.
  - `ChooseMeleeDefenderStage` - listed the reserve unit as a valid defender.
  Shoot/spell/movement/activation paths all filter correctly; audited every `UnitBindings`
  enumeration in the engine and melee was the only hole.
- 2026-07-23: Design fork surfaced and resolved with Chris: considered making `Position`
  nullable so off-table is unrepresentable. Rejected for now - see Decisions.

## Decisions
- **Fix at the chokepoint, not the call sites.** `MeleeRangeUtilities.AreUnitsInMeleeRange`
  documents itself as the single definition of melee engagement; the battlefield gate goes there
  (both ends), fixing charge availability + defender eligibility + any future caller at once.
  `AnyEnemyWithinStandoff` measures raw distance and needs its own filter.
- **Backstop at the wound aggregation point.** `UnitData.OnModelWoundsDealt` warns via
  `RuleDiagnostics.WarnOnce` if positive wounds land while the unit carries an off-table token
  (`InReserve` / `EmbarkedIn` / `OffTableFromForcedMove`). Token check, NOT
  `GetIsOnBattlefield()` - a unit whose last model just died reads as off-battlefield by
  position and would warn on every ordinary kill. Warning not assert: a live game should
  degrade, not crash. Verified orderings: spillout disembarks before dangerous-terrain wounds,
  ambush arrival places before clearing reserve - no false positives.
- **Nullable `Position` rejected (for now).** ~450 read/binding sites across engine + app +
  tests, save/wire format change with no migration hook (#178), and C#'s `Nullable<struct>`
  leaks (`GetValueOrDefault()` returns exactly (0,0,0), reintroducing the bug with compiler
  blessing). Decisive: the unit-level gate must survive anyway (tokens carry per-kind arrival
  semantics), so the discipline requirement would not disappear. If stronger structure is wanted
  later, the better shape is access control: stages lose direct `army.UnitBindings` enumeration
  behind a `TargetingUtilities` API that bakes the filter in.

## Outcome
(open)
