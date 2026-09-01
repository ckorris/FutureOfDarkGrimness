# 380 — GDF Melee Slayer fires in all melee; book text says only when charging

**Status**: DONE 2026-08-31.
**Related**: #375 (found by its AoF variant review), #196 F1 (where the def was authored), #042.

## Goal

The shipped GDF `Melee Slayer` definition (`GdfRuleSupplement.json`) gates on `isMelee`, so it
fires on strike-back and non-charge melee too. The GDF book text - and 16 of 17 AoF books -
reads "When this model charges, its weapons get AP(+2) if most models in the target have
Tough(3) or higher": an `isCharging` gate. The shipped def over-grants.

Fix: change the condition to `and(isCharging, targetMajorityHasTough(3))`, adjust the
description, rebake the GDF books that reference it, verify with the #196 loop (validate,
fire-lint - the name is allowlisted for the Tough-majority lint gap, which is unaffected -
engine suite, rebake, coverage).

Note: the AoF side already ships the charge-gated shape (`AofRuleSupplement.json`, #375 C9),
and the melee-wide variant survives only as the Havoc Warriors Lust Disciples per-book
override - authored from that book's own divergent text, not from this bug.

## Notes

- 2026-08-22: Filed from #375's within-AoF variant review (owner chose "file a separate item"
  over fixing in the #375 branch). See `WorkItems/375-aof-rule-data-authoring.md` C9 notes.

## Decisions

## Outcome

2026-08-31: exactly the prescribed fix. `GdfRuleSupplement.json`'s Melee Slayer condition
isMelee -> isCharging + description reworded to the book text; `--validate-rules` OK (251 defs);
`--apply-rules` re-embedded into the 11 GDF books that reference it (each diff exactly the two
changed lines; AoF books untouched - their supplement always shipped the charge gate and wins the
merge, and the Havoc Warriors Lust Disciples melee-wide override is that book's own divergent
text). New `MeleeSlayerShippedDataTests` pins the charge+Tough(3) gate in BOTH supplements (so
they can't drift apart) and in an embedded book copy (so a supplement edit without the rebake
fails loudly). App suite 1560 green, census clean. No engine change.
