# 404 — Ambush reserve reads as "Activated" in the in-game army list

**Status**: done
**Related**: #329 (`UnitActivation`, the shared definition), #399 (`UnitCardHeaderLayout`, which
reserves the tag's room), #202 (Ambush reserve is unit state, not an origin position)

## Goal

Open the army list (L) mid-game and a unit held in Ambush reserve is tagged red **Activated** - the
opposite of the truth: it is the one unit on the roster that has done nothing. It should read as held
in reserve instead, in its own colour.

## Notes

- 2026-09-12: Filed and fixed. Numbering from `origin/master` at `f63996a` after `git fetch`: detail
  files max **403**, archive max **401**, no `40x` branch on any remote. No collision.

## Decisions

- **2026-09-12 — the root cause is a sign error in a shared definition, not a display bug.**
  `UnitActivation.HasActivated` (#329) asks whether a unit is MISSING from the round's unactivated
  pool. `SingleRoundContext.SetUnactivatedUnits` admits only units that are on the battlefield or
  embarked, deliberately excluding reserve ("they don't activate until they're placed") - so a reserve
  unit is missing from the pool for the exact OPPOSITE reason an activated one is, and the single test
  could not tell the two apart. Fixed in `HasActivated` rather than at the two draw sites, so the
  canvas labels and tooltips that share the definition (#329's whole point) cannot disagree with it.

- **2026-09-12 — "In Reserve", blue, in the same slot as "Activated".** Not a third column and not a
  token chip: the `InReserve` token is deliberately `ETokenProminence.Invisible`, so it is not in the
  chip row, and the state tag slot is where a player already looks. Blue (`ImGuiTheme.HeaderAccent`)
  rather than red or amber - it is a neutral "not here yet", not a warning. Both the compact table row
  and the card header go through one `StateTag` helper, so they cannot drift; the card's #399 layout
  already measured whatever string the tag holds, so a longer label needed no new layout work.

## GUI hand-verify checklist

1. Play an army with an Ambush unit, hold it back at deployment, open the army list (L) in round 1:
   the unit reads blue "In Reserve", not red "Activated".
2. Both views: the compact table row and the expanded unit card.
3. When it arrives in a later round, the tag clears; once it has acted that round it reads "Activated".
4. A long-named reserve unit's tag does not land on top of the name (#399's layout rule).

## Outcome

**Closed 2026-09-12, hand-verified by the owner.** Fixed in `UnitActivation.HasActivated` (a unit in
reserve is never "activated") rather than at the draw sites, so the canvas labels and tooltips that
share the #329 definition cannot disagree with the army list; `IsInReserve` added alongside. The army
list's two tag sites share one `StateTag` and show a blue "In Reserve" in the slot "Activated" uses -
a neutral "not here yet", and not a token chip, since `InReserve` is deliberately invisible. 6 unit
tests pin the pool / reserve / arrival readings.
