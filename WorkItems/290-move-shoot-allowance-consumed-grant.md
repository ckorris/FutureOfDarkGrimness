# 290 — Advance-and-shoot gate measured a granted move against the un-granted allowance

**Status**: in-progress
**Related**: #153 (one-shot movement grants), #042 (rule dispatch), #093 (per-model budgets)

## Goal
A unit that legally advanced under a ONE-SHOT granted movement rule was refused the Shoot action, because
`ChooseActionStage.GetCanShoot` re-derived the advance allowance from the unit's live rules AFTER
`ExecuteMoveStage` had already spent the grant. The gate must compare the move against the allowance that
was in force when the move was authorised.

Done when: a Slow unit carrying a one-shot Rapid Advance grant may advance its full 8" and still shoot,
while a Slow unit WITHOUT the grant is still correctly blocked after a 6" move.

## Notes
- 2026-07-26: reported from play. Robot Legions' "Inspiring Bots" (`addRule` / `Rapid Advance`, scope
  `NextTrigger`) cast on two Slow units. Movement offered and coloured a full 8" Advance; Shoot then
  wasn't offered afterwards.
- The reporter's guess was "a bug in how we evaluate if it can shoot when the advance distance is the
  same as charge". The coincidence is real and is what made it visible — Slow makes Advance 6-2=4 and
  Charge 12-4=8, and Rapid Advance's +4 takes Advance to 8 as well — but the equality is not the cause.
  The cause is the grant being consumed between the move and the gate.

## Decisions
- Fixed by RECORDING the allowance with the distance (`RegisterMoveFinished(distance, allowance)` ->
  `IUnitActionContext.MoveShootAllowance`), not by making the query non-destructive. The consumption is
  correct — a one-shot grant SHOULD be spent by the move it paid for — so the fix belongs on the reader,
  which was asking a subtly different question ("what could this unit advance now?") from the one it
  needed answered ("what was this move allowed to be?").
- The recorded value is `MaxModelAdvanceDistance` — the max across the unit scalar and every per-model
  budget — because the recorded DISTANCE is itself a max across models (`GetMaxMoveDistance`). Comparing
  a joined Fast hero's 8" against the unit's 6" scalar would call a legal advance a rush. This mirrors
  what `EffectiveMaxRushDistance` already does for the Pass gate.
- `RegisterMoveFinished`'s second parameter is REQUIRED, not defaulted: a default would let a future
  caller silently reintroduce exactly this bug.
- The distance check is now additionally gated on `HasMoved`, so a 0" allowance on a unit that never
  moved cannot gate anything.

## Outcome
Shipped 2026-07-26 (engine `fc9b316`). `IUnitActionContext.MoveShootAllowance` +
`RegisterMoveFinished(distance, allowance)`; `IMovementActionContext.MaxModelAdvanceDistance`;
`MovementStage.ReconcileChildContextBeforeLeaving` feeds the move context's value through,
`DisembarkStage` passes a live query (nothing on that path consumes a grant, so it is the value in
force); `GetCanShoot` reads the recorded number and only applies the check when `HasMoved`.

New `MoveShootAllowanceTests` (5): the reported scenario end to end through the real
`ExecuteMoveStage` + `MovementStage` reconcile hook; an explicit statement that a re-derived allowance is
now SMALLER than the one the move used (so re-deriving cannot quietly come back); the negative case
(Slow, no grant, 6" move -> no Shoot, with the reason in the menu); a not-moved case; and the per-model
max. **Verified the tests catch the bug**: temporarily restoring the re-derivation fails exactly the
positive test.

Test-harness finding worth keeping: `ChooseActionStage` does NOT prompt when the only surviving option
would be Pass, so a negative shoot-gate test needs some other valid action to keep the menu inspectable
(these give the unit a melee weapon and an enemy in charge range).

Engine suite 2204/2204 green; headless smoke exits 0. Awaiting GUI hand-verify with the real spell.
