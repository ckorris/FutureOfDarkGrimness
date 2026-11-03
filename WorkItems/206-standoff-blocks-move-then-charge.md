# 206 — Standoff rejects moving within 1" of a big rect base; forced-charge-next design broken

**Status:** open (filed 2026-07-09 from Chris's play report)
**Related:** #011 (enemy standoff distance), #033/#197-P5a (action gating changes suspected as the
regression window), #150 (rect-base geometry)

## Report

Chris could not move units right up against an enemy with a large rectangular base - the move was
rejected with "moving within 1 inch without charging". But the DESIGN (agreed when standoff first
landed) was: you MAY move inside the standoff band without charging; doing so removes Pass from
your next Choose Action so you are forced to charge next activation. Chris suspects the break
came in with a fix that "forced us to cast sometimes" (the Cast gating work - #033's
GetCanCast/castable filtering, or #197 P5a's activation-choice changes are the suspect windows).

## What to establish

1. Current intended semantics: find the original forced-charge-next design (standoff work, #011
   era) - where was "Pass gone when inside standoff" implemented, and does that code still exist?
2. Whether the rejection is UNIVERSAL now (any enemy) or specific to large rect bases - a
   rect-base standoff measured against the circumscribing circle would wrongly widen the 1" band
   on the flats (#150 remnant), which matches "somehow only against the big base".
3. Bisect window: the Cast-forcing change Chris remembers - check GetCanPass/GetCanCast history
   in ChooseActionStage and the move validator's standoff branch for a behavior change that made
   "ends within standoff" a hard reject instead of a charge-obligation.

## Outcome (2026-07-11) — RESOLVED

The intended design in the report was confirmed by Chris and implemented: you MAY move right up
against an enemy without charging; the consequence is a forced charge, enforced at action choice,
not a move-time block.

- Engine `6053061`:
  - `MovementUtilities.ValidateMovingThroughEnemyUnits` no longer rejects a non-charge move that
    ends inside the standoff band against a CONTACTABLE enemy. Pass-through and ending-stacked stay
    enforced; Aircraft (uncontactable, can't be charged) keep their own standoff. This clears the
    reported "can't press Done, must charge" block in the movement resolver (both the GUI Done gate
    and the engine `DefinePathStage` throw funnel through `ValidatePaths`, so one removal fixes both).
  - `ChooseActionStage.GetCanPass` is now PROXIMITY-based: Pass is gated when any enemy has a living
    model within `ENEMY_STANDOFF_DISTANCE_INCHES` (1", base-to-base, 3D) of the unit. The charge
    band (`MELEE_RANGE_INCHES_HORIZONTAL`, 2") is deliberately wider, so a unit at 1"-2" MAY Charge
    but is not forced (it may Pass). Allied units never force a charge (team-filtered like
    `GetCanCharge`). Distance moved no longer gates Pass (the old beyond-Rush rule is gone).

Design note vs the report's wording: the report said "forced to charge NEXT activation." This engine
loops back to Choose Action within the SAME activation after a move, and Charge is available there
once in range - so the obligation is enforced same-activation (move up -> Teleport or Charge, no
Pass), which matches Chris's re-described flow. The two-band model (forced at 1", chargeable at 2")
is what makes "teleport just clear of the standoff -> Pass returns, Charge still offered" work.

Tests: `ChooseActionPassDisableTests` rewritten to the proximity model (enemy at 0.5" gap -> no Pass;
1.5" gap -> Pass ok; allied -> Pass ok; moved-far-no-enemy -> Pass ok; already-attacked -> Pass ok).
`MoveThroughEnemyValidationTests` standoff-band cases flipped to accepted; pass-through/stacked/
aircraft unchanged. 1571 engine tests green, build clean, headless exits 0.

Enables the #197 Teleport slice (teleport clear of the standoff restores Pass).

## Notes

- 2026-07-09 — filed.
- 2026-07-11 — resolved (see Outcome).
