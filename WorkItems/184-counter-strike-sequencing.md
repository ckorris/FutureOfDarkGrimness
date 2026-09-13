# 184 — Counter strike sequencing: per-weapon RAW vs whole-unit role swap

**Status**: open (deferred by design, filed from the #183 sign-off discussion 2026-07-08)
**Related**: #183 (Subject-seat attribution — where this edge surfaced), #030 (Counter implementation), #027 (weapon-scoped dispatch), #006 (hero join)

## The deviation

Core-rule **Counter** is a weapon rule: "strikes first with this weapon when charged" (plus the
impact-dice reduction). The faithful (RAW) melee sequence when only SOME of a charged unit's weapons
carry Counter is a three-phase interleave:

1. Defender swings its **Counter weapons only**,
2. Charger swings **all** its weapons (casualties from phase 1 already removed),
3. Surviving defenders strike back with the **remaining** (non-Counter) weapons.

The engine instead resolves any `StrikeFirst` op with a whole-unit role swap
(`DetermineStrikeOrderStage` -> `SwapCombatRoles`): the defender swings with **all** weapons first,
then the charger. Two consequences vs RAW:

- Non-Counter weapons in a mixed unit swing early (a tempo gain they shouldn't have), and defender
  models that RAW would lose to the charger's phase-2 swings before their phase-3 strike-back still
  get to swing.
- **Hero edge (the #183 trigger):** a joined hero whose personal melee weapon carries Counter drags
  the entire host unit's strikes ahead of the charger.
- **Impact facet:** `Effect.ReduceImpactDicePerModel` reduces the charger's Impact dice per living
  model of the whole defending unit; with a hero-only Counter weapon that over-reduces (RAW scales by
  models actually carrying Counter).

For homogeneous units (every melee weapon has Counter, or none) the swap is exact — which is the
overwhelmingly common case, and why #030 shipped it this way.

## Scope when picked up

Split melee resolution so `StrikeFirst` can carry a weapon subset: phase the defender's swing into
counter-weapons-before-charger and rest-after, with casualty removal between phases, and scale the
impact reduction by counter-carrying models. Touches the melee stage flow (strike order, swing
batching, strike-back offer), so it wants its own design pass — do not bolt onto #183.

## Notes
- 2026-07-08: Filed. #183 deliberately does NOT change this behavior (weapon-scoped rules have no
  all-models notion; the unit-scoped gate work there leaves Counter exactly as #030 built it).
