# 377 — AoF spells: author all 240 army spells as data

**Status**: in-progress (started 2026-08-23)
**Related**: #378 (spells land inside the `.fdgbook` spells arrays it produces), #375/#376 (spells reference AoF rule names that must resolve). Reference doc: `/home/chris/Projects/GDF Armies/Age of Fantasy/Special Rules and Spells by Army.md` (local only, do not copy text into the repo).

## Goal

All 40 AoF books carry their 6 spells as working `SpellDefinition` data (name, threshold,
target spec, effect), verified in-engine. Done means: every spell is castable and produces
its printed effect, with the `generated-spell-armies`-style probe recipe (see
`reference_headless_testing` memory / the GDF spell verification runs) exercising each one
headless.

No new engine machinery is expected: the 2026-08-22 appraisal classified all 240 against
the spell-effect patterns the GDF books already use —

- dealHits-style damage: 120
- addRule-once buffs/debuffs ("gets X once (next time the effect would apply)"): 88
- markTarget ("friendly units get X against once"): 20
- moraleTestThen: 5
- countAsInTerrain (Difficult/Dangerous Terrain once): 5 — effect kind exists; confirm the
  spell path accepts it (GDF spells never used it; CastSpellStage pattern-matches effects,
  so this is the one seam worth probing early)
- move-modifier once (+/-X" on Advance/Rush/Charge): 2 — authorable as addRule of a
  move-modifier micro-rule (Musician precedent)

Casting values are uniformly (1)(1)(2)(2)(3)(3) across all 40 books. Several single-model
target spells carry the "resolved as if the target was a unit of [1]" clause — reuse the
GDF handling. Watch for spells granting rules that only #375/#376 define; sequence those
books after the rules land or accept the engine's skip-with-warning until then.

## Notes

- 2026-08-23 (session start): **Reality differs from the filing assumption — the 240 spells
  are already parsed and sitting in the local `fdgbooks-aofbaked/` books** (the #375 bake ran
  the importer's full spell parser: 120 dealHits / 95 addRule (incl. 7 synthesized move/terrain
  rules) / 20 markTarget / 5 moraleTestThen; 22 singleModel). The real work is coverage +
  correctness, not authoring from scratch. **Slice 1 finding: neither `--rule-coverage` nor
  the #168 army audit ever walked spell references.** Extending the census (spell WithRules at
  Weapon scope; grant names raw + argument-less, mirroring RuleEvaluator.CollectGrantedRules)
  found **21 dead spell refs in the SHIPPED GDF bundled books** (13 names — spells that
  silently do nothing today) and **34 in the AoF bake** = 14 Retreating Strike (#381's, known)
  + 20 across the same name families. Full name list + per-spell map in the session transcript;
  families: ungated numeric roll modifiers ("+1/-1 to morale test rolls", "+1/-1 to defense
  rolls", "-3 to casting rolls", "-1 to hit rolls when attacking"), combat-gated modifiers
  ("+1 to hit rolls in melee/when shooting", "AP(+1) in melee", "AP(1) when shooting"),
  weapon-rule qualifiers ("Bane/Shred when attacking"), "Slayer" (a rules-page name reachable
  only via spell marks), and "+6-inch range when shooting" (both as a self-grant and as a MARK -
  the mark form needs a range-check peek, see D3).

## Slice plan (2026-08-23)

1. [x] Census/audit/load-preflight for spell refs (engine: SpellRuleReferences helper,
   ArmyListSpellResolution grant pre-flight, ArmyRuleAudit walk + parity; app: --rule-coverage
   walks spells, dynamic breakdown). Engine suite 2992 green.
2. [ ] Core catalog defs (#093 section) for the 8 gated/named phrases (D2) + integration tests.
3. [ ] Range-mark peek seam (D3) + tests.
4. [ ] Importer: ungated "gets +/-N to <roll> rolls once" -> Effect.StatModifier (D1) + tests.
5. [ ] Rebake GDF bundled books; census -> 0 dead spell refs GDF, AoF dead = 14 (#381 only);
   app test pinning bundled-book spell coverage. AoF books rebaked locally.
6. [ ] Engine integration tests: spell-granted countAsInTerrain + movementBonus synthesized
   rules fire on the next move (the filed seam probe).
7. [ ] Local parity audit: baked spell JSON vs the reference doc (threshold/range/count/hits/
   AP/affinity/singleModel/granted name) across all 240; fix importer mis-parses found.
8. [ ] Headless probe: cast every AoF spell in-game (generated caster armies per book); tally
   here; close.

## Decisions

- 2026-08-23 **D1 — ungated numeric "gets +/-N to X rolls once" spells become
  Effect.StatModifier at import** (not phrase-named rule grants): StatModifier is the engine's
  purpose-built primitive for exactly this (Casting/Morale Debuff idiom, #197 P6 consumption
  sites at all four roll stages, spell-path verified in #034's generator pass). The clincher:
  "-3 to casting rolls" CANNOT be a granted rule - the cast roll folds GrantedRollModifiers
  tokens only, no hook fires there (Casting_OnSpellCastAttempt has zero consumers), so a
  phrase def would be emit-but-never-consumed. Books rebake (they are regenerated artifacts);
  pre-#377 armies keep the dead grant but slice 1's load pre-flight now WARNS instead of
  staying silent.
- 2026-08-23 **D2 — combat-gated/named phrases become CORE catalog defs** in the existing #093
  "combat-kind variants" section ("Bane in melee" etc. already live there, and its comment
  says these are "the named rules the 'X when shooting' spell grants resolve to"): marks carry
  rule NAMES, so these must exist as definitions; core (not supplement) fixes the shipped GDF
  books and every existing army retroactively with NO rebake, since grants resolve against
  the live registry. Names: "+1 to hit rolls in melee", "+1 to hit rolls when shooting",
  "AP(+1) in melee", "AP(1) when shooting", "Bane when attacking" / "Shred when attacking"
  (ungated Bane/Shred clones - attack-only already, the qualifier is redundant, #375 C9
  "Counter in Melee" precedent), "Slayer" (printed text identical GDF/AoF: >9" shooting OR
  charging, AP(+2) vs majority Tough(3)+ - Melee Slayer + Piercing Hunter shapes compose,
  targetMajorityHasTough exists), "+6\" range when shooting" (Shooting_OnRangeCheck
  rangeModifier, Royal Legion shape).
- 2026-08-23 **D3 — range-extension MARKS get a structural peek seam**: "friendly units get
  +6\" range when shooting AGAINST it once" (Eternal Guidance, Clearview Leaves) cannot work
  through mark claiming alone - marks are claimed at DetermineHitRollStage, AFTER range/target
  legality. Mirror ShootAfterRushRules (Quick Shot Mark's target-bound permission): the range
  check peeks the TARGET's marks for a rule with a rangeModifier entry and applies its delta
  against that target only; the mark is still claimed (and spent) at hit-roll time as usual.
  The addRule form (Battle Rune, self-buff) needs no seam - grant evaluation already runs at
  Shooting_OnRangeCheck.

## Outcome
