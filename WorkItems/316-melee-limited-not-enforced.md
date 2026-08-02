# 316 — Limited is not enforced in melee at all

**Status**: todo
**Related**: #032 (Limited: marker rule + per-model spent token, shooting only), #315 (the shooting
opt-out work that surfaced this), #028 (Deadly-first gating in melee, the nearest existing gate)

## Goal
A Limited melee weapon may only be used once per game, and a player may decline to use it — the same
two properties #032 and #315 gave the shooting path.

## Notes

- 2026-08-02: **Filed while implementing #315** (deliberately not fixed there — separate slice, user
  sign-off on splitting it). `ChooseMeleeWeaponStage` never mentions `LimitedRules`: it does not call
  `IsSpent` when building its option list and never calls `MarkFired` after a weapon is chosen. So a
  Limited melee weapon:
  - is offered in every melee, every round, for the whole game (never marked spent, never gated), and
  - cannot be declined — the melee weapon choice is a bare `StringSelectionRequest` with no cancel and
    no hold-fire entry, so the stage's `validOptions.First(...)` lookup requires one of the offered
    labels back.

  Grep confirming the gap: `LimitedRules` appears in `ChooseRangedAttackStage` only.

  Shape of the fix (not yet designed): mirror #032's two seams in `ChooseMeleeWeaponStage` — an
  `IsSpent` check that moves the weapon into `invalidOptions` with an "Already used (Limited)" reason,
  and a `MarkFired` after `SetAttackWeapon`. The decline half is the harder half: melee's request type
  carries no hold-fire form, so it needs either #315's `DeclineWeapon` reached through a new reply shape
  or a "no weapon" option on the string request. Worth checking first whether any shipped book actually
  puts Limited on a melee weapon — if not, the enforcement half alone may be the whole slice.

## Decisions
(none yet)

## Outcome
(open)
