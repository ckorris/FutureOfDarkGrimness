# 278 — Playtest fixes 2026-07-25 ("Blue - Spill-Out" batch)

Four small playtest findings from the 2026-07-25 session, handled #202-style as one item.
Engine changes authorized by the owner for this batch.

## Facets

1. **Spill-out dice batched into one row.** When a destroyed transport spills occupants, the
   dangerous-terrain test rolled a decisive d6 + its own `DiceRolledBeat` per model — big spillouts
   crawled. Now one `Roll(6, livingModels)` batch + one `DiceRolledBeat.From` per occupant unit,
   mirroring `MovementExecutor.ApplyDangerousTerrainEffects` (which also makes the probabilistic
   roller meaningful here). Per-model hurt/death beats still play after the single dice row.
   `ApplySpilloutEffects` now returns `SpilloutRollResult` (roll + wounds + casualties) and takes
   the randomness type; realistic mode deals whole wounds to the first N testers (same convention
   as MovementExecutor), probabilistic spreads the expectation.

2. **No morale test when every hit is saved.** Reported: a unit under half strength that got shot,
   took hits, saved them all, still tested morale. Does NOT reproduce on current master: the #254
   predicate (`WoundsLeftUnitAtHalfStrength`) gates on wounds actually lost. Root cause of the
   sighting is almost certainly the PRE-#254 predicate (`CrossedIntoHalfStrength`), which mixed
   metrics — "was above half" by WOUNDS (`remainingWoundsBefore * 2 > MaxWounds`) but "is at half"
   by MODEL COUNT — so a unit with unequal per-model wounds (hero-joined, mixed Tough) could sit in
   a window where every targeting triggered a test even with zero wounds taken. Fixed as a side
   effect of #254 (2026-07-21). New pin: `AlreadyAtHalf_HitButEverySavePasses_NoTest` drives the
   REAL RollToSave -> AssignWounds -> ApplyWounds chain (3 hits, all saved) and asserts baseline
   wounds + no test. If the owner sees it again on a post-#254 build, grab the save.

3. **Harassing strike-back move — verified against the published rule, no change.** Owner suspected
   the post-melee 3" move should only fire "after you attack". Official Army Forge text (OPR v3.5.x,
   Dark Elf Raiders): *"Once per round, units where all models have this rule may move by up to 3\"
   after shooting or being in melee."* — "being in melee" covers being charged and striking back, so
   the current defender-side move is rules-legal. Known deliberate gaps recorded at
   `CoreRuleCatalog.cs` (post-combat-move family note): no once-per-round cap yet, and the charger
   never gets the move (engine interprets the melee half as defender-only). Left as-is pending an
   owner ruling; not part of this fix batch.

4. **Tier-2 (Toast) banner when a unit recovers from Shaken.** Both recovery paths were log-only:
   activation-end recovery (`ChooseActionStage`, #008) and rule-driven `ClearTokenOnRoll`
   (Steadfast-style round-start rolls, which presented their recovery die but no banner). Both now
   Announce an amber Toast; `ClearTokenOnRoll`'s player-facing strings also switched from the raw
   `TokenType` record (printed "TokenType { Id = Shaken }") to the catalog display name.

## Notes

- **2026-07-25** — filed; all four facets done same day. Engine commits `9e96401` (facet 1),
  `e1c1810` (facet 4), `c4b0f09` (facet 2 pin). Suite 2125/2125 green. Awaiting GUI hand-verify:
  spillout dice row (blow up a loaded transport), recovery Toast (activate a Shaken unit;
  Steadfast-style recovery). Facet 3 was a rules verification only — banner text quote + the two
  known deliberate gaps are recorded above for a future owner ruling.
