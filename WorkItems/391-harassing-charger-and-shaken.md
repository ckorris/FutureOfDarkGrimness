# 391 — Harassing family: charger-side post-melee move + Shaken gate

**Status**: implemented + tested 2026-08-31 - awaiting GUI hand-verify.
**Related**: #381 (whose survey found both gaps and whose Retreating Strike rides the same seams),
#100 (the post-combat-move family), #206/#390 (forced-charge interactions unaffected - the post-melee
move is not an action). Engine submodule work. Number taken after `git fetch origin` (389/390 were
claimed upstream mid-session).

## Goal

Two shipped-behavior gaps in the Harassing / Hit & Run / Guerrilla family, both surfaced by #381's
engine survey and both endorsed for fixing by the owner ("please fix the harassing issues"):

1. **Charger-side post-melee move.** The rule text ("may move up to 3" after shooting or being in
   melee") is role-neutral, but `PostMeleeStage` offered the move ONLY to the charged unit. A unit
   that charges with Harassing never got its move - which also made #381's charger-side Retreating
   Strike unreachable. Both combatants now get the offer, each against its own once-per-round
   budget. Order: the ACTIVE player's unit (the charger) first, per the core simultaneous-trigger
   convention; the charged unit second.
2. **Shaken gate.** Shaken units must remain idle and cannot use Active Special Rules - the rulebook
   explicitly calls out repositioning rules like Harassing - but nothing gated the post-combat move
   on Shaken (relevant exactly when a unit becomes Shaken from losing the melee it would move after).
   Gated at `PostCombatMoveGate`, the family's one chokepoint, so the base rules, the Boosts, and any
   future supplement variant are all covered without touching defs.

## Notes

- 2026-08-31 implemented. `PostCombatMoveGate` refuses a Shaken unit (token ops still apply, budget
  kept); `PostMeleeStage` walks charger-then-charged (alive-gated, each with its own
  `PostMeleeActionContext` evaluation); `ICombatActionContext.PostCombatMover` became
  `PostCombatMovers` (a list - BOTH combatants can Harass in a Dark Elves mirror match) and
  `RetreatingStrikePostCombatStage` drains it one strike per entry via the reflect-stage
  OnBatchDone loop, bound back to itself in MeleeStage AND ShootStage. Hook doc updated
  (Melee_OnPostMelee is no longer "the charged unit only").
- Interaction pinned by tests: a unit Shaken by THIS melee's morale loss is blocked (morale resolves
  before the post-melee window), matching the community reading the #381 ruling adopted; the
  charger's move fires the same #381 strike arm automatically (it rides the same gate).

## Decisions

- Shaken gate lives in the ENGINE chokepoint, not on the family's defs: "Shaken = idle" is a
  game-wide fact, a def-side condition would need repeating across 5+ rules and their Boosts, and
  the Boost ops are emitted off `UnitHasRule` (a def-presence check a base-rule condition would not
  suppress).
- Charger first, charged second when both offer, per OPR's "active player resolves simultaneous
  effects first". Only matters in mirror matches.

## Outcome
