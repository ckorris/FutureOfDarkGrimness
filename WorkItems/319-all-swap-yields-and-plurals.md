# 319 — "Replace all" swallowed the specialist swap below it; "-es" plural targets never matched

**Status**: awaiting verification (engine + app suites green, headless smoke exit 0)
**Related**: #318 (the starved-Replace retry — same subsystem, opposite direction), #261 (quantity-prefixed
targets), #156 (Army Forge builder)

## Goal
Two defects found while auditing the #318 blast radius, both owner-triaged 2026-08-02:

1. **An "all" swap ate the pool a specialist swap needed.** Owner's framing: *"if you start with 5 pistols,
   and you trade 1 for something else, then buy a 'Replace all Pistols with Rifles' thing, the unit should
   get 4 rifles."* It did not — the all-swap took all 5 and the specialist upgrade vanished silently,
   unpaid. Done looks like: the all-swap leaves the copies its rivals have been bought for, and the Forge
   still offers those rivals once the all-swap is taken.
2. **A Replace target that matched nothing.** Dwarf Guilds' Guardians "Replace all Pistols and Bashes"
   never matched the `Bash` weapon, so the swap silently left all five Bashes on the unit, free.

## Notes

- 2026-08-02: Both landed. (1) `ListCompiler.ReservedForLaterSwaps` — a single-target Affects=All Replace
  subtracts, from its own application count, the applications already bought by counted (One/Any) Replace
  choices authored BELOW it that compete for the same weapon. No reordering: the reservation is local to the
  all-swap's own turn, so the other ~1900 Replace sections are untouched. Forge side, `ReplacePool` measures
  a section's availability on a compile with the yielding all-swaps dropped, so the specialist swap stays
  selectable (it was grayed out before, matching the old compiler); `AvailableExcludingSection` now routes
  through it, and the extra compile only happens when such a rival is actually selected. (2) `TargetMatches`
  gained an "-es" singularisation, allocation-free (it runs in the Forge's per-frame recompile).
  6 new tests fail with the two behaviours neutralised and pass with them; 4 more guard tests pass both ways
  (all-swap alone still takes everything; no reservation for a swap authored above; multi-target all-swaps
  unchanged; plural symmetry). Engine 2615/2615, app 958/958, headless smoke exit 0.

## Decisions

- **Reserve, don't reorder.** Deferring all-swaps behind counted ones in the choice ordering would have been
  the obvious fix, but it moves the all-swap past *every* other section, including Upgrade sections that can
  grant its target — a much wider blast radius than the bug. Subtracting the rivals' bought applications at
  the all-swap's own turn produces the same result for the target case and changes nothing else.
- **Only rivals authored BELOW the all-swap are reserved for.** One authored above has already taken its
  copies out of the pool by the time the all-swap runs; reserving again would hand it a second copy and
  leave a target unswapped. Pinned by `ReplaceAll_DoesNotReserveForASwapAuthoredAboveIt`.
- **Single-target all-swaps only — owner's call, 2026-08-02.** The corpus has 121 (all-swap, later counted
  swap) pairs across 32 of 47 books. 33 name a single weapon and are unambiguous: what is left is simply
  what "all" now means. The other 88 name two ("Replace all Heavy Pistols and CCWs"), where the aggregate
  weapon model cannot say which model's CCW survives once another model has traded its pistol away — a
  5-model unit can end up holding 6 weapon copies. Those keep today's behaviour and are deferred to their
  own item (see below). Explicitly pinned as deferred, not fixed, by
  `ReplaceAll_WithTwoTargets_DoesNotYield_PendingTheAggregateModelQuestion`.
- **The plural fix belongs in the matcher, not the data.** Verified 2026-08-02 against the live OPR API
  (`army-forge.onepagerules.com/api/army-books/fk1mkbp8apvltu0z`): `targets` is a list of display STRINGS
  and the section schema carries no weapon-id alternative (keys: affects, id, isHeroUpgrade, isLowPrio,
  label, model, options, parentPackageUid, select, targets, type, uid, variant) — even though weapons
  themselves do carry `id`/`weaponId`. So Army Forge matches on the name too and singularises "Bashes"
  properly; ours stripped one "s" and got "bashe". Patching the book data would have been undone by the next
  re-import. `EveryReplaceTarget_NamesSomethingItsUnitCanHold` now asserts zero dead targets corpus-wide.

## Deferred (filed, not silently dropped)

- **Multi-target all-swaps vs. specialist swaps — 88 corpus pairs.** Needs the aggregate weapon model
  question answered first: unit-level weapon quantities cannot express "this model traded its pistol but
  kept its CCW", so "replace all Heavy Pistols and CCWs" after a specialist swap has no unambiguous answer
  in the current representation. Today those specialist swaps are still swallowed. Deserves its own item
  when the model question is taken up.

## Outcome
Pending hand-verification in the running Army Forge (see the index line for what to check).
