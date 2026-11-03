# 200 — Choose Action offers Shoot with no fireable target -> infinite loop under AI

**Status:** open (filed 2026-07-09 while validating the #191 benchmark pool)
**Where:** `ChooseActionStage.GetCanShoot` (offers) vs `ChooseRangedAttackStage.BuildWeaponOptions` (rejects)

## Symptom

A solo-rules (or any deterministic) AI game livelocks: `Entered Choose Action.` -> `Shoot stage
entered.` -> `No weapon has a valid target - returning to Choose Action.` forever. The FdgLab
watchdog kills the game at 120s (Fault) - and before the watchdog can fire, the loop's unbounded
stage-transition continuation chain overflows the stack and kills the whole process at default
stack sizes (that facet is #203).

## Deterministic repro

`fdglab smoke --seed 42 --a "FdgLab/armies/Orks 2k - Horde Mixed.fdgarmy" --b <same>` - the Orc
Bikers activation at log line ~2085 starts the loop. Needs `DOTNET_DefaultStackSize=0x4000000` to
survive long enough to see the watchdog fault instead of a process crash.

## Root cause

`GetCanShoot` checks embarked / off-table / has-attacked / advance-and-shoot cap / has-ranged-
weapons - but never whether any weapon has a FIREABLE target (range + LoS). The real filter lives
in `ChooseRangedAttackStage`, which bounces back to Choose Action when empty. A human shrugs and
picks something else; the solo AI's fixed priority picks Shoot again. **The engine already solved
this exact class for Cast:** `ChooseActionStage.GetCanCast` + castable filtering exist, per
CastSpellStage's own comment, "what keeps a no-target cast from looping forever under a
deterministic resolver". Shoot never got the same gate.

## Proposed fix (engine core -> Chris sign-off per plan D2)

Extend `GetCanShoot` with the same pattern as `GetCanCast`: at least one (weapon, enemy) pair
fireable, sharing/refactoring the target scan `ChooseRangedAttackStage` already performs. Grays
the GUI's Shoot button with a reason for free. Pin with an integration test: unit with ranged
weapons, no LoS to any enemy -> Shoot not offered -> AI activation completes.

## Notes

- 2026-07-09 — filed. Found on the pool's first validation pass: 7/8 armies play clean 2k mirrors
  in 1.4-2.5s; the Orks horde-mixed list is blocked on this.

## Outcome

**Fixed 2026-07-09** (engine `a8b593b`). Two changes: (1) the gating pipeline order - Limited-spent
now gates BEFORE Deadly-first, so a spent Limited+Deadly weapon can no longer lock out the unit's
other weapons (the Orc Bikers' empty Rocket-Mod demanded to be "fired first" forever); (2)
`HasAnyFireableTarget` now runs the stage's exact pipeline (`ApplyTargetGating` + the same fireable
check), so the Shoot action gate and the shoot stage can never disagree again. Diagnosed by
temporarily instrumenting the bounce branch and reading the live divergence. Verified: two
regression pins in ChooseRangedAttackStageTests; the Orks 2k mirror plays a full 4-round game
(3.5s, default stacks); all 8 pool mirrors green; suite 1511/1511; builtin bench hashes unchanged
(no Limited weapons there - pool-army trajectories legitimately changed where wrongly-locked-out
units now shoot).
