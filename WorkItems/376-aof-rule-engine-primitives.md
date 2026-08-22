# 376 — AoF rules pt.2: new engine primitives

**Status**: todo
**Related**: #375 (data half; feeds this item its list), mirrors #197. Engine submodule work — submodule-first commit cadence, full engine suite green. Reference doc: `/home/chris/Projects/GDF Armies/Age of Fantasy/Special Rules and Spells by Army.md` (local only, do not copy text into the repo).

## Goal

The AoF rule mechanics that cannot be authored with the existing effect/condition
vocabulary get real engine primitives, with integration tests mirroring the nearest
existing `*RuleIntegrationTests`, one vertical slice at a time. Done means: combined with
#375, every rule name the AoF books reference resolves to a definition that actually fires,
and anything deliberately approximated is recorded here as an owner-ruled facet (the #197
discipline).

Known candidates from the 2026-08-22 appraisal (~4 of 306 names; expect the #375 census to
adjust the boundary in both directions):

- **Bloodthirsty Fighter** — attacker gains +1 attack per enemy unmodified block roll of 1
  in melee. New seam: defender's defense dice feeding the attacker's attack count (Shred
  reads block 1s, Predator Fighter adds attacks — neither crosses this way).
- **Retreating Strike** — once per round, wounds dealt when the unit ends a post-melee move
  within 3" of an enemy. Extends the triggered-move family with a move-end proximity
  trigger.
- **Ravage Aura** — grants **Ravage(+1)**: an argumented, additive aura grant. The grant
  path is name-only today (LAT-1 fix made unargumented grants safe, not argument-carrying).
- **Reckless Piercing** — on activation, optional gamble: one die, 2+ round-long AP buff,
  1 round-long enemy AP buff against you. May fall out of the Unpredictable branch
  machinery; adjudicate before building.

Borderline (try as data in #375 first; move here only if the vocabulary refuses): Ethereal
(pre-attack 6" reposition + move penalties), Vale Oath Boost (Shaken recovery threshold
3+), Shadowborn/Wild Veil Boost min-clamps on range/charge debuffs, Great Sergeant (extra
hit on 5-6 rather than 6).

## Notes

- 2026-08-22: Filed from the appraisal residue. Dice invariant applies throughout
  (histograms, never int-locked roll-derived values).

## Decisions

## Outcome
