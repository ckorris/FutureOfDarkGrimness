# 368 - Shooting weapon rows do not say how many copies are firing

## Goal

The shoot panel listed a weapon by name alone, so a volley of five rifles and a single rifle read
identically. The melee weapon menu has printed the datasheet convention ("3x Blade - A2, AP0") since
#298, and the copy count is one of the first things a player compares weapons on.

## Notes

### 2026-08-08 - implemented

- `WeaponOption.CopiesRemaining` already carried the number (the pool this firing draws from), set from
  `availableWeapons[weapon]` in `ChooseRangedAttackStage.BuildWeaponOptions`. It was documented as
  "only meaningful to display when `AimedIndividuallyRule` is set"; it is correct for every weapon, so
  both front ends now display it.
- GUI weapon row: `"{CopiesRemaining}x {Weapon.Name}"`, with the ONCE PER GAME / SPENT badge offset
  recomputed off the new label. GUI Details header: `GetWeaponNameAndStats(CopiesRemaining)`.
- CLI: `GetWeaponNameAndStats(CopiesRemaining)`, the same overload the melee menu already uses.
- A Takedown/Sniper weapon spends one copy per pass, so its count counts down as it fires. That is
  consistent with the existing "N LEFT - AIMED 1 AT A TIME" badge rather than in tension with it.

## Outcome

Implemented + tested (new `Enter_OrdinaryWeapon_ReportsHowManyCopiesTheUnitIsFiring` pins
`CopiesRemaining` for a non-Takedown weapon, which nothing covered before). CLI-verified: a 10-model
Infantry Squad's row reads "2x Grenade Launcher - 24", A1, AP0, Blast(3)". Awaiting GUI hand-verify.
