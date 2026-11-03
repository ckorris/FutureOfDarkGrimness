# 374 — Tactician over-commits under Declare First (no overkill avoidance)

**Status**: open, not started
**Related**: #371 (Declare First - where this was found and where half the fix already landed),
#191 A6 (the two bot profiles)

## Goal
Under Declare First a bot should stop piling weapons into a unit its earlier declarations have already
killed. Done = the Tactician discounts a target by the wounds already DECLARED against it this action,
not merely by the wounds already dealt, and a test pins that it spreads a volley the same way in both
shooting modes.

## Notes

- 2026-08-11: Found by code-read while auditing #371, not by a failing test. Split out of #371 on
  2026-08-12 when that item closed, so closing it would not bury this.
- Mechanically both AI profiles are FINE under the new mode: neither resolver reads the shooting mode,
  both answer one (weapon, target) request at a time and always reply `Selected`, never `Cancelled`, so
  the declaration loop terminates normally and the lost-shots path handles a declaration whose target
  died. Nothing hangs, nothing throws. This is purely a play-quality regression.
- The regression is in `TacticianRangedAttackResolver.Resolve`, whose score reads LIVE table state:
  `float remaining = Math.Max(1f, target.RemainingWounds)`, `living`, `CombatMath.ExpectedKillsFrom`.
  Under One At A Time those already reflect what earlier weapons did, so a nearly-dead target's
  `fractionKilled` saturates and `ShootingKillBonus` stops paying - which is exactly what pushes the next
  weapon onto a fresh unit. Under Declare First every request is answered before any dice, so every
  weapon scores the target as undamaged and the bot stacks its whole arsenal into one unit.
- DerpBot (`AiChooseRangedAttackResolver`) never read wounds at all, but it got the same feedback
  implicitly - a dead unit stops appearing in the next request's options - so it over-commits too. Worth
  deciding whether it should be taught anything or left as the deliberately simple bot.
- Net effect today: Declare First quietly favours the human, who can reason "that will die to the first
  volley" where the bot cannot.

## Approach

**Half of this already shipped with #371.** The blocker used to be that the request could not describe
the standing declarations (`PreviousTarget` names one unit, which is not enough for cumulative wounds).
`ChooseRangedAttackRequest.Declarations` now carries every aimed-and-unrolled shot - weapon, target unit
and copy count - so the remaining work is scoring plus tests:

1. In `TacticianRangedAttackResolver`, discount `remaining` for a candidate target by the expected wounds
   already declared against it this action (sum `CombatMath.ExpectedKillsFrom` over the matching
   `Declarations` entries). Empty in One At A Time, so that mode's behaviour is unchanged by construction.
2. Decide DerpBot's story - most likely "leave it, it is the simple bot" - and record which.
3. Pin it: a Declare First fixture where the first weapon is lethal, asserting the second weapon picks a
   different unit, plus a One At A Time control proving the discount is a no-op there.
4. Worth a pool run before/after, as with the other Tactician scoring changes.

## Outcome
_Not started._
