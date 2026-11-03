# 318 — Melee hold-back is a Limited-only exception, not a general opt-out

**Status**: done (closed 2026-08-02 - hand-verified in the running app by the owner)
**Related**: #320 (melee Limited enforcement + hold-back, which this narrows), #321 (how the hold-back is
presented), #319 (shooting's Hold fire, which is NOT affected — see Decisions)

## Goal
Melee hold-back matches the melee rule: a weapon may only be declined when a rule says it may.

> Models within 2" horizontally and 4" vertically of enemies must strike with all melee weapons.

## Notes

- 2026-08-02: **Implemented. Engine 2590/0, app 894/0, full build clean, CLI hand-run.** Owner-supplied
  rule text: #320 shipped hold-back on EVERY melee weapon, which contradicts it — there is no general
  opting out of a swing. `ChooseMeleeWeaponStage` now offers the hold-back only for weapons carrying
  Limited (`LimitedRules.LimitedRuleName != null`); an ordinary weapon gets no hold-back row at all, not
  even a greyed one (a greyed row would imply the choice exists and is merely unavailable right now).
  The only-weapon refusal is unchanged in behaviour and re-worded to cite the rule: "Must strike with all
  melee weapons - this is the only one left to strike with."

  The melee menu now reads:

      [1] 1x One-Shot Bomb - A3, AP2, Limited
            Limited - This weapon may only be used once per game.
          [s1] Hold back
                Keeps its Limited once-per-game use for a later melee.
      [2] 3x Blade - A2, AP0

  New test `MeleeChoose_OrdinaryWeapons_AreNotOfferedAHoldBackAtAll` (Limited + two plain weapons);
  the refusal-reason and companion-pairing assertions updated to match.

## Decisions

- **Limited is the exception because its own text implies one.** "May only be used once per game" is a
  statement about WHETHER to use it — read as compulsory, the first melee of the game always spends it
  and "once per game" collapses into "in your first fight", which is plainly not what the rule is for.
  Nothing else in the melee sequence carries that implication, so nothing else gets the choice.
- **Not a greyed row for ordinary weapons.** Showing "Hold back" disabled on every blade would teach the
  player that declining is normally possible; it isn't. Absence is the honest presentation.
- **Shooting keeps its broader Hold fire (#319), deliberately.** The rule quoted above is about MELEE —
  it is what compels models in contact to strike. Shooting has no equivalent "must fire everything"
  clause, and a unit may decline to shoot at all, so declining an individual weapon there needs no
  rule-granted exception. If the shooting side turns out to have its own must-fire clause, that is a
  separate correction and this file is the precedent for how to scope it.
- **The only-weapon guard survives on its own footing.** It is no longer "at least one weapon must attack
  after charging" as a general principle but the same rule again: even a Limited weapon must swing when
  holding it back would mean striking with nothing.

## Outcome
Melee hold-back is offered only for weapons carrying Limited; an ordinary weapon gets no opt-out row at
all, matching "models within 2in horizontally and 4in vertically of enemies must strike with all melee
weapons". The only-weapon refusal survives on the same rule. Shooting's Hold fire (#319) was left
deliberately broader - that rule governs melee, and a unit may decline to shoot entirely.
