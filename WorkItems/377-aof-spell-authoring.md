# 377 — AoF spells: author all 240 army spells as data

**Status**: done (GUI hand-verified 2026-08-23; archived)
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

## Slice plan (2026-08-23) — ALL DONE same session

1. [x] Census/audit/load-preflight for spell refs (engine: SpellRuleReferences helper,
   ArmyListSpellResolution grant pre-flight, ArmyRuleAudit walk + parity; app: --rule-coverage
   walks spells, dynamic breakdown). Engine `082fcc8`, superproject `fe51b50`.
2. [x] Core catalog defs (#093 section) for the 8 gated/named phrases (D2) + integration tests
   (AP gates + Slayer arms at the save seam in ThrustRuleIntegrationTests, gated phrase rule
   claimed from a mark in HitRollRuleIntegrationTests incl. the spent-for-nothing shooting
   claim, SpellPhraseCloneTests parity pins). RuleFireLint gained Tough(9)-majority defender
   variants; the 4 app allowlist entries (Shatter/Tear/Melee+Ranged Slayer) retired as stale.
   GDF dead 21 -> 13. Engine `75d7241`, superproject `0af811e`.
3. [x] Range-mark peek seam (D3): EffectiveRange structurally peeks target marks for
   Shooting_OnRangeCheck RangeModifier entries (ShootAfterRushRules pattern, peek never
   spends); DetermineHitRollStage runs one live range evaluation per SHOOTING attack so a
   one-shot range grant (Battle Rune) is spent by the shot that used it (melee never spends).
   Tests both ways. Engine `4d5323f`, superproject `04dcc3e`.
4. [x] Importer StatModifierShape (D1): ungated "+/-N to hit/defense/morale test/casting rolls
   [when attacking]" -> Effect.StatModifier(NextTrigger); gated phrases stay grants. Importer
   test covers all four roll kinds + the gated fall-through. Engine `ce9c189`.
5. [x] Books: NOT a regen — the bundled books carry post-bake live-API passes (#219 prices,
   #383 shapes) that a full re-import would wipe (found: rebake drifts a Support Grunts cost
   25 -> 30 vs the live-refreshed data). New `OprBookImporter.RestampSpells` + app
   `--import-spells <snapshotDir> <book|dir>` re-stamps ONLY spells (synthesized defs swapped
   in place, position preserved). 13 GDF books changed semantically (addRule-phrase ->
   statModifier) + HEF picks up #376's onFailure serialization field. **GDF census: 14,088
   refs, 0 dead. AoF rebake (full regen - those books carry no post-passes): 7,991 refs, dead
   = exactly #381's 14 Retreating Strike.** New BookSpellCoverageTests pins every bundled
   book's spell refs through the shared load ladder (empty allowlist, stale-entry guard).
   Engine `0882546`, superproject `377a089`.
6. [x] Seam probe: spell-granted countAsInTerrain (importer's exact "Desert Storm Effect"
   shape, granted NextTrigger as a RuleGrant token) caps all projected budgets WITHOUT
   spending, and ExecuteMoveStage spends it - "once" means once. PASSED with no engine change:
   the machinery was already correct; the filed seam concern is closed. (movementBonus grants
   were already pinned by the existing GrantOnce Quick tests.) Engine `2042623`.
7. [x] Parity audit (`appraisal-2026-08-22/spell-parity-audit.py`, local): independent
   re-parse of all 240 printed spell texts vs the baked JSON - threshold/range/maxCount/
   affinity/singleModel/effect kind + numbers + synthesized def parameters. **240/240 match,
   0 mismatches.** Two accepted API-vs-PDF drifts recorded: (a) "AP(2) each" wording (import
   handles it); (b) PDF flavor rule names Slash/Butcher/Break are API Surge/Surge/Crack - the
   PDF adds Ignores-Cover/Regeneration riders the API text dropped; books follow the API
   (canonical machine source, same doctrine as the #375 census).
8. [x] Corpus cast sweep (`SpellCorpusProbeTests`, engine; FDG_SPELL_PROBE_BOOKS env var,
   skips unset): every spell CAST through the real CastSpellStage - first-castable pick,
   targets, wounds, forced moves - asserting completion, exact threshold spend, zero
   RuleDiagnostics. **AoF 240/240 across 40 books; GDF bundled 282/282 across 47.** Plus an
   end-to-end headless game with a compiled Beastmen army (exit 0, zero rule warnings).
   Engine `f2ba812`, superproject `83f0397`.

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

**Implemented and machine-verified 2026-08-23, one session** (8 slices, engine `082fcc8`..`f2ba812`,
superproject `fe51b50`..`83f0397`). The filing assumption was wrong in a useful way: #375's bake had
already parsed all 240 AoF spells into the local books - the real work was COVERAGE, which had a
census-shaped hole. Spell rule references were invisible to `--rule-coverage`, the #168 audit, and
army load alike; opening that seam found 21 dead spell refs in the SHIPPED GDF books (spells casting
as silent no-ops since #156) and 20 more in the AoF bake, fixed via three mechanisms chosen by where
each name's consumption lives: Effect.StatModifier at import for ungated numeric phrases (the cast
roll folds tokens only - a rule-shaped "-3 to casting rolls" can never fire), 8 core-catalog phrase
rules for gated/named grants (core so already-baked books and saved armies resolve them with no
rebake), and a range-mark peek + one-shot range-grant spend seam for the "+6\" range against it"
marks that mark-claiming alone could never deliver.

End state: **GDF bundled books 14,088 references / 0 dead; AoF local books 7,991 / dead = exactly
#381's 14 Retreating Strike refs. Parity audit 240/240 against the printed texts. Corpus cast sweep
240/240 AoF + 282/282 GDF through the real CastSpellStage with zero diagnostics.** Durable tooling:
spell-aware census + load pre-flight + audit parity, `--import-spells` targeted re-stamp,
BookSpellCoverageTests (bundled books, auto-extends when #378 bundles AoF), SpellCorpusProbeTests
(any books dir), the parity audit script, and RuleFireLint's Tough-majority contexts (4 allowlist
entries retired). Engine 3016 green, app 1367 green, headless smokes exit 0.

**#378 pickups**: bundle-time spell verification = run BookSpellCoverageTests (auto) + the two local
sweeps against the bundled set; AoF books are full regens (no post-bake passes yet) - if #378 ever
runs the price/shape refreshers on them, switch their spell updates to `--import-spells` too.

**Hand-verified 2026-08-23** in the running app via the scenario below - all four checks passed:
cast-debuff token landed ("-3 to casting rolls" on the Adept, log-confirmed), "Marked: Shred when
attacking" + the shot into the marked Blob, the Battle Rune range grant, and the Eternal Guidance
range mark making the 27" Distant Lurkers targetable. (Cast-roll dice ran cold - three legitimate
failed 4+ casts across runs - and the first scenario cut placed the Lurkers outside the Magus's 18"
spell range; fixed in the committed scenario.) The original check text: `--scenario Scenarios/377-spell-verify.json` (or load
`377-SpellVerify.fdgsave`) - a Caster with the four shipped spell shapes vs AI targets. Checks:
(1) "Burn the Heretic" on the Hostile Adept -> cast-modifier token lands; if the AI Adept casts,
its roll breakdown shows the granted -3 (needs a 6). (2) "The Founder's Curse" on the Hostile Blob
-> "Marked: Shred when attacking"; shooting the Blob with Verifier Rifles shows Shred's extra
wound on block-1s and clears the mark. (3) "Battle Rune" on the Rifles -> grant badge; spent by
their first shot. (4) "Eternal Guidance" marking the Distant Lurkers (27" out, rifles are 24") ->
the Scouts can now target them. AoF spells become GUI-reachable only when #378 bundles the books.
