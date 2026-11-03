# 461 — Dead upgrade picks: a chosen Replace option with no target is silently dropped at 0 pts

**Status**: todo
**Related**: #440 (catalog rebuild), #261 (quantity-prefixed replace targets), #383 (per-model sections), #218 (Replace All pricing)

## Goal

A pick that compiles to nothing is never silent. Two facets:

1. **Validation.** `ListValidator` flags a chosen option whose section applies 0 times (`ListCompiler.Applications`
   clamps a Replace to the targets actually present, so a missing target means no gains and no cost). Today the
   compiled unit just lacks the weapon and the total reads short, with no issue line - the Forge, the reforge
   report and the lobby all show a clean list. Severity to decide: Error (the list claims an upgrade it does not
   have) or Warning (it plays fine, just not as written). Recommendation: Error for a pick that can never apply
   on the unit, Warning for one consumed by another pick in the same unit (facet 2b below).
2. **Corpus audit.** A dev flag (or a `*ShippedDataTests` case) walks every bundled book and, with the compiler's
   own target matcher (not a string compare - targets are plural, quantity-prefixed and sometimes rules), reports
   every Replace section whose targets (a) are absent from the base loadout and gained by no option of that unit
   (dead on every loadout - an importer/OPR-data defect), or (b) exist only until another section's Replace All
   strips them (conditionally dead - legal data, needs facet 1 to tell the user). Fix (a) in `OprBookImporter`
   if the OPR data names the target wrongly (compare against the Army Forge share JSON), else record it as a
   data quirk per book.

"Done" = facet 1 tested on both shapes + the audit's (a) list at zero or each entry explained.

## Notes

- 2026-11-01: found while hand-curating the shipped `armies/` lists (#440 follow-up, this session).
  - **Confirmed dead on every loadout:** Battle Brothers `Pathfinder Bikers`, section "Replace one Heavy Rifle
    and Hand Weapon" -> "Sgt. Heavy Pistol, Sgt. Hand Weapon" (5 pts). The base loadout is Assault Grenade
    Launcher + Hand Weapon; every Replace All option that adds a Heavy Rifle also turns Hand Weapon into Strike,
    and the one that keeps Hand Weapon has no Heavy Rifle. So the combined target is never satisfiable, the
    compiler applies it 0 times, charges 0, and the two "Replace Sgt. ..." sections behind it are unreachable
    too. The Blood/Prime/Knight/Custodian Brothers books clone this unit - check them first in the audit.
  - **Confirmed conditionally dead:** Human Defense Force `Combat Bikers`: "Replace one Battle Rifle and Strike"
    -> "Anti-Tank Lance" (15 pts) after the Replace All "Pistol, Heavy Lance" pick has already removed every
    Battle Rifle and Strike. The list compiled 15 pts short with no issue line; the curated list drops the pick.
  - A quick string-level scan of all 90 books over-reports (733 "dead" sections, most of them plural/quantity
    mismatches the real matcher handles), which is exactly why the audit must go through `ListCompiler`.

## Decisions

- (none yet)

## Outcome

(open)
