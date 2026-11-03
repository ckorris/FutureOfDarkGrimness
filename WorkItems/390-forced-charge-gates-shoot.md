# 390 — Forced charge (#206) must gate Shoot, not just Pass

**Status**: implemented + tested 2026-08-30; awaiting GUI hand-verify (grayed Shoot button with
reason text when standing in the standoff band with a charge available)
**Related**: #206 (the proximity obligation), #334 (predicate extraction + preview), #337
(base-to-base geometry pins), #197 (Instinctive - the other menu-binding attack compulsion)

## Goal

A unit inside the 1" standoff band that can charge is not offered Shoot: shooting sets
`HasAttacked`, which closes the charge gate AND satisfies `GetCanPass`'s engaged short-circuit, so
a shot both dodges and dissolves the forced charge. Actions that do not close the charge window
(Cast, Move/reposition, ability actions) stay available. Chris (2026-08-30): "Shooting then causes
you to be unable to charge, so you fail that requirement... shooting should be [grayed out]. You
have to charge."

## Mechanism (pre-fix)

The #206 obligation was enforced ONLY in `ChooseActionStage.GetCanPass` (via
`ForcedChargeUtilities.AnyEnemyWithinStandoff`). `GetCanShoot` never consulted it, so the post-move
menu offered Shoot; picking it set `HasAttacked`, after which Pass unlocked (the engaged
short-circuit at the top of `GetCanPass`) - the obligation evaporated without a charge.

## Fix (engine `ChooseActionStage`)

- New public static `ShootWouldForfeitObligatedCharge(gameContext, context, canCharge)` - same
  predicate, same geometry as the Pass gate (`AnyEnemyWithinStandoff`), bound on `canCharge` so a
  unit that cannot charge at all (Immobile, only Aircraft in range, nothing to swing - #355) keeps
  its shot rather than being punished for an obligation it cannot discharge.
- Menu assembly grays Shoot with reason "Within 1\" of an enemy - must charge; shooting would
  forfeit the charge." Placed after the #100 RestrictActions block (so canCharge reflects
  restrictions) and before the #197 Instinctive compel (which keeps working; with both binding,
  the menu correctly narrows to Charge).
- No livelock: Charge is by construction a valid option whenever the gate fires, and
  `TacticianPlanner.ChooseAction` / the solo picker only ever return offered options.
- No app-side work: valid/invalid options with reasons already render in GUI + CLI.

## Notes

- 2026-08-30: implemented, 5 new pins in `ChooseActionPassDisableTests` (the #206 fixture) mirroring
  the Pass-gate geometry: in-band+canCharge -> withheld; 1"-2" band -> free; canCharge=false ->
  free; ally in band -> free; #337 large-base base-to-base pin. Suite 3075/3075; full build +
  headless smoke exit 0. Solo/Tactician benchmark hashes may legitimately move (units in the band
  now charge instead of shooting) - re-pin at the next #191 bench run, not gated here: this is a
  rules fix, not an AI-policy change.

## Decisions

- Gate binds on `canCharge` (Chris's "you have to charge" presumes a charge exists; a unit that
  cannot charge keeps its shot).
- Gate evaluated fresh each menu visit like the Pass gate - a unit that STARTS in the band is bound
  the same as one that moved in; repositioning out then shooting stays legal.

## Outcome

(open - awaiting GUI hand-verify)
