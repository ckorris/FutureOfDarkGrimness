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

## Notes

- 2026-07-09 — filed.
