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

- **Grounded Speed** (added 2026-08-22 from #375 C4) — terrain-conditional movement bonus:
  `mostModelsWithinInchesOfTerrain` requires `IHasTerrain`, which `MoveActionDeclaredContext`
  does not provide. Small slice: give the movement-declare context terrain access, then the
  rule itself is plain data (already drafted and reverted in #375 C4 - see its ledger).

- **Vale Oath Boost (+ Aura)** (added 2026-08-22 from #375 C5) — Shaken recovery at 3+
  instead of 4+. `clearTokenOnRoll` resolves as `InvokeClearTokenOnRoll`, an imperative
  executable that rolls once PER FIRING ENTRY, so base (4+) plus a boosted entry (3+) gives
  two recovery rolls (P 0.833) instead of one at 3+ (0.667). Needs either a threshold-shift
  parameter folded before the roll or a best-threshold-wins fold like WoundIgnoreSink.

Borderline (try as data in #375 first; move here only if the vocabulary refuses):
Shadowborn/Wild Veil Boost min-clamps on range/charge debuffs. RESOLVED as data in #375: Ethereal (rides Effect.Teleport's stage routing +
Slow-style negative movementBonus; C4), Great Sergeant (two addExtraHit hook entries, 5 and
6; C3).

## Notes

- 2026-08-22 (#375 C5): Vale Oath Boost (+ Aura) moved here (double-roll composition, above).
- 2026-08-22 (#375 C4): Grounded Speed moved here (context capability gap, above); Ethereal
  and Great Sergeant fell out of the borderline list as data.
- 2026-08-22: Filed from the appraisal residue. Dice invariant applies throughout
  (histograms, never int-locked roll-derived values).

## Decisions

## Outcome
