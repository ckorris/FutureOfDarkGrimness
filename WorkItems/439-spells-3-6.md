# 439 — Spells 3.6: parser grammar, re-import, per-book parity verification

**Status**: done 2026-10-31 (owner review: D1, D2, D3, D4, D5)
**Related**: #415, #377 (3.5 spell parity, the method to repeat), #034, #429 (LoS flag consumer), #431/#433 (rule names spells now grant), #438
**Branch**: `feature/opr-3.6`. Model/effort: Opus 5.5 / high.

## Goal

Every spell in the 88 books (6 per book) imports to a correct runtime effect. All 3.6 spell lists changed:
thresholds reordered (specialised spells level 1, generic ones higher), level-3 spells +6" range, LoS written
into the spell ("Pick one enemy unit within 18" in line of sight"), grants phrased "gets Shred on its weapons
when shooting", new rule names inside spells (Smash, Fracture, Enhanced Highborn, Spell Resistance...),
some targets changed from "up to three units" to "one unit" and vice versa, Dark/Dark Prime Brothers
replaced "Blessed Ammo" with "Blessed Shroud", Ratmen renamed two spells.

Evidence of today's parser against 3.6 text (scratch import coverage): it emits granted-rule names such as
"Shred on its weapons when shooting", "Unstoppable on their weapons when shooting", "+6" range on their
weapons when shooting", "AP(1) on their weapons when shooting", "Bane on their weapons when attacking",
"Rending on their weapons in melee", "Indirect on their weapons", "Surge on their weapons" - i.e. the
"on its/their weapons when shooting/in melee/when attacking" suffix is not recognised and becomes part of
the name.

"Done" = (1) the parser recognises the 3.6 phrasings: the LoS clause -> `RequiresLineOfSight` (#429); the
weapon-scoped grant suffix -> the existing "X when Shooting / X in Melee / X when Attacking" grant names with
weapon scope; "+N" range on its weapons" -> the Increased Shooting Range family; "once (next time the effect
would apply)" unchanged; damage spells "takes N hits with <rules>" with the new rule names; (2) every spell
re-imported into the baked books via `--import-spells`; (3) a parity pass like #377's: a scripted dump of
(book, spell, threshold, parsed target, parsed effect) reviewed in batches against the snapshot text, with
each batch's findings recorded here and bugs fixed before the next batch; (4) `--rule-coverage` shows no
spell-synthesised names unresolved.

## Acceptance

`OprBookImporterTests` spell cases for each new phrasing; the batch review ledger in this file covers all
88 books; a headless game casts at least one re-imported spell per system (seeded dice).

## Code anchors (survey 2026-10-29)

- `OprBookImporter.cs` Spells section (the synthesiser), `FdgRaylib/Program.cs --import-spells` (~line 291),
  `WorkItems/377-*.md` (batch method), `Rules/Dispatch/RuntimeSpell.cs`. See the appended survey rows below.

### Survey rows (code survey 2026-10-29)

Findings that shape the item: an unparsed spell is DROPPED with only a console warning (`OprBookImporter.cs:130`)
and nothing pins "6 spells per book" - add that test first. The required prefix regex anchors only the start,
so trailing clauses (including "in line of sight") are silently ignored today; LoS is hardcoded per shape.

- `ArmyBuilding/OprBookImporter.cs`: import loop :125-140 (:130 drop), `RestampSpells :154-197`, grammar
  comment :516-534, `TryParseSpell :597-721`, `SynthesizedGrantOrSkip :726-738`, synthesizers :742-800.
  Shapes: prefix :536-538 (`^pick (one|two|three|up to two|up to three) (enemy|friendly) (units?|models?)
  within (\d+)"`), Damage :540-542 (LoS true :638), Mark :563-565 (:648), Move :569-571, ApLoss :575-577,
  Terrain :581-583, MoraleTest :587-595, Grant :544-546 (raw phrase becomes the rule name), StatModifier :557-559.
- Phrase rules the grants must match by exact name: `CoreRuleCatalog.cs:918, :965-967, :979-981, :993-995,
  :1007-1009, :1021-1023, :1040-1042, :1061, :1077-1079` (see #434).
- `Rules/Definitions/SpellDefinition.cs:23` (no prose stored); `CastSpellStage/SpellText.cs:15` `Describe`
  (shown at `ArmyBuilderScreen.cs:905`, `CastSpellStage.cs:507`). `--import-spells` `Program.cs:289-335`
  (:311 `<book.Name>.json`).
- #377 method: `WorkItems/377-aof-spell-authoring.md:6-30` (goal), :99-129 (decisions D1-D3), :131-169
  (verification: census, pre-flight, off-repo parity audit 240/240, corpus cast sweeps).
- Tests: `FdgRaylib.Tests/BookSpellCoverageTests.cs:34`, `BookSpellCastProbeTests.cs:25-30`,
  `Tests/SpellCorpusProbeTests.cs:37-45` (`FDG_SPELL_PROBE_BOOKS`), `SpellPhraseCloneTests.cs:8-29`,
  `OprBookImporterTests.cs:386-500` (verbatim 3.5.3 spell text fixtures).

## Notes

- 2026-10-31: **Done - parser, re-bake, pins, parity pass, cast evidence, hand-offs.** Slices (engine `6af179ab`, super
  `52f9d65` pointer + bake gate, `dd2d229` supplement clones, `4f923d1` re-baked books, `c693395` corpus pin):
  (1) `OprBookImporter`: `CanonicalGrantName` folds the 3.6 weapon-scope suffix ("X on its/their weapons when shooting
  / in melee / when attacking / (bare)") onto "X when shooting" / "X in melee" / "X when attacking" / "X", and
  "+6\" range on its/their weapons when shooting" onto Increased Shooting Range, for grants AND marks; new shapes for
  the spell-token drain (`Effect.ConsumeToken(SpellTokens, N)`), the 3.6 Corrode Weapons ("loses AP(1) on its weapons
  when shooting", now also gated `IsNotSpell`), a mark with its verb dropped (Ancestral Guidance), and the morale move's
  "Units may only be moved once per round in this way" (`OncePerRoundKey` = the spell's name); an unparsed spell now
  warns in two classes - `TruncatedSpellWarning` (source defect) vs `UnrecognizedSpellWarning` (parser gap) - and
  `scripts/bake-books.sh` FAILS the bake on the second. (2) `CastSpellStage`: the synthetic spell weapon carries the
  spell's range (D3); a drain spell's targets keep back their own tokens from the cast-assist window
  (`DrainReservation`, `SpellPurse.Spend(reserveOwn:)`, D11); `SpellText` describes drains. (3) Catalog helper
  "+6\" range when shooting" deleted (D8); the 15 supplement phrase clones deleted and Unwieldy Debuff repointed
  (D9): `--validate-rules` 416 -> 401 definitions. (4) Re-bake (OFFLINE=1, 2026-10-30 snapshots): 45 books changed,
  527 spells (was 517), 287 LoS, `--rule-coverage` 30862 -> 30864 references / Dead 0, a second bake byte-identical
  88/88, ASCII grep clean. (5) `BookSpellCorpusPinTests` (app): six spells per book except the named source defect,
  88 books / 527 spells, 287 LoS all enemy-targeting, unique names, no weapon-scope suffix in any granted name, 17
  Smash/Pierce, 80 Enhanced one-shot grants over 44 names, 8 drains, the two move keys. Engine
  `Spells36RuleIntegrationTests` (drain, hinder lock, Pierce over half the spell range, separate move keys) and
  `OprBookImporterTests` 3.6 grammar cases. Corpus cast sweep `FDG_SPELL_PROBE_BOOKS=FdgRaylib/Assets/Books`
  `SpellCorpusProbeTests`: 527 spells from 88 books cast clean. **Cast evidence (seeded headless, `--book-to-army`
  armies, `--army <file> --seed 1`):** GDF Saurian Starhosts - "[LOG] Frog-Mage cast Toxin Mist: rolled 4, needed 3+
  (base 4+, relay +1); spent 1 token." / "[LOG] Toxin Mist marked Tehaxi - the next friendly to attack gets Bane when
  attacking." and later "[LOG] Saurian Veteran's Bane when attacking ignored Regeneration."; AoF Saurians - "[LOG]
  Frog-Mage cast Poison Mist: rolled 6, needed 4+; spent 2 tokens." / "[LOG] Poison Mist marked Frog-Mage - the next
  friendly to attack gets Shred when attacking." and later "[LOG] Frog-Mage's Shred when attacking added 1 extra
  wound." (both marks are names this item changed). Hand-offs written to #440, #441 and #434's follow-up list.
  Hand-off checklist: #438 (1) 11 skipped spells - 10 parse, Combat Possession D1; (2) re-baked + re-pinned; (3)
  allowlists still empty; (4) helpers D8. #433 - Enhanced grants pinned per corpus (80 / 44; its "45" was a
  miscount), NextTrigger semantics unchanged. #435 (1) canonical names emitted, its clones deleted; (2) move key D2;
  (3) 38 AoF Enhanced spells confirmed. #434 - canonical names emitted, its clones deleted. #429 - LoS kept, 287
  pinned. #431 - Smash/Pierce range D3, other names resolve. #422 - Evil Gaze / Asphyxiating Fog grant "Counter in
  Melee" (batches 4 and 8).
- 2026-10-31: **parity pass ledger** (#377's method; dump `scratchpad/439/parity_review.py` over the re-baked shipped
  books vs the 2026-10-30 snapshot text, 11 batches of 8 books in filename order, each batch's findings written here
  before the next batch was opened; the dump shows per spell the source text, the parsed threshold / count / affinity /
  unit-or-model / range / LoS / effect, and the granted rule's resolved description).
  - Batch 1 (Alien Hives, AoF Beastmen, Change Disciples, Chivalrous Kingdoms, Daemons of Change / Lust / Plague /
    War; 48 spells): 48/48 match. Thresholds, ranges, counts, affinities, single-model clauses and LoS flags all as
    printed; Shifting Form drains 2 tokens; Curse of the Huntress marks bare Surge; damage rule names (Fragment,
    Disintegrate, Lacerate, Obliterate, Decimate, Purge) all resolve to their 3.6 texts. No bugs.
  - Batch 2 (AoF Dark Elves, Deep-Sea Elves, Dragon Empire, Duchies of Vinci, Dwarves, Eternal Wardens, Ghostly Undead,
    Giant Tribes; 48 spells): 48/48 match. Ancestral Guidance (source dropped "gets") now a mark of Indirect; Clearview
    Leaves marks Increased Shooting Range; Shock Attack marks "Unstoppable when shooting"; Irresistible Grief "Bane when
    attacking"; Elemental Form grants "AP(+1) in melee"; Rune of Spite synthesizes -2"/-4". Thunder Clap is one of the
    17 Smash/Pierce spells (18" spell: the over-half facet now fires beyond 9", D3). No bugs.
  - Batch 3 (AoF Giant Tribes Change / Lust / Plague / War Disciples, Goblins, Halflings, Havoc Dwarves, Havoc Warriors;
    48 spells): 48/48 parse as printed. **Finding F1 (behaviour gap, deferred to #434's follow-up, not a parser bug):**
    three 3.6 spells take hits "with Hazardous. Roll as many dice as hits to see if \"on rolls of 1\" effects trigger"
    (GDF Ratmen Clans Power Surge, GDF Goblin Reclaimers Pow!, AoF Ratmen Lightning Blast - batches 8/9/6) - the text
    means the caster can wound itself on 1s, but the spell pipeline never applies `InflictSelfWounds`
    (SyntheticHitResolution drops it; ApplyWoundsStage's doc says spell damage "carries no such result"), so they get
    Hazardous's AP(4) with no risk. AoF Goblins' Biting Bat ("1 hit with Deadly(3) and Hazardous", NO roll-dice clause)
    is right today by the same accident. The roll-dice clause itself gets no flag (D4): the pipeline always rolls spell
    hits; 21 of the 22 spells whose weapon rule triggers on a 6 to hit carry the clause (the 22nd: batch 4, F2).
  - Batch 4 (AoF High Elves, Human Empire, Kingdom of Angels, Lust Disciples, Mummified Undead, Ogres, Orcs, Ossified
    Undead; 48 spells): 48/48 parse as printed. Magical Surge grants "Bane in melee", Drain Spirit "Unstoppable in
    melee", Cursed Arrows "Shred when shooting", Crack Spirits "Unwieldy in melee", Magic Fists marks "Rending in
    melee", Evil Gaze grants "Counter in Melee" (#422's hand-off, done), Battle Guts "Spell Resistance".
    **Finding F2 (OWNER REVIEW, D4):** Kingdom of Angels' Lightning Bolt reads "takes 6 hits with AP(1) and Shatter."
    with NO "Roll as many dice ..." clause, unlike every other Surge / Fracture / Shatter spell. Read literally, no dice
    are rolled and Shatter (on 6s to hit) can never trigger; the engine rolls spell hits always, so Shatter procs. Kept
    as the engine does (reads as an omission - Ossified Undead's Shattering Blast, the same rule, prints the clause).
  - Batch 5 (AoF Plague Disciples, Ratmen, Saurians, Shadow Stalkers, Sky-City Dwarves, Vampiric Undead, Volcanic
    Dwarves, War Disciples; 48 spells): 47/47 imported spells match. Combat Possession is the one not imported - its
    3.6.0 source text is truncated (D1, OWNER REVIEW). Aether-Cloud Rune drains 2 tokens; Seductive Invocation's move is
    keyed "Seductive Invocation" (D2); Poison Mist marks "Shred when attacking"; Cracking Bell-Chime is a -1 Defense
    StatModifier on up to two enemy units; Lightning Blast is F1's AoF case. No new bugs.
  - Batch 6 (AoF Wood Elves, Battle / Blessed Sisters / Blood / Blood Prime Brothers, Change Disciples, Custodian
    Brothers, DAO Union; 48 spells): 48/48 match. Blessed Ammo grants "Shred when shooting" (3.6 Blessed Ammo, not the
    Enhanced grant #433's note listed it among); Advanced / Blood Sight mark "Unstoppable when shooting"; Burn the
    Heretic drains 2 tokens; Coordinated Aggression marks "AP(1) when shooting"; Spirit Vines / Cerebral Trauma / Blood
    Trauma (Smash) and Blood Wound (Pierce) are 18"-24" spells whose over-half facet now reads the spell range. No bugs.
  - Batch 7 (Daemons of Change / Lust / Plague / War, Dark Brothers, Dark Elf Raiders, Dark Prime Brothers, Dwarf
    Guilds; 48 spells): 48/48 match. Dark / Dark Prime Brothers carry 3.6's Blessed Shroud (Enhanced Darkborn) where 3.5
    had Blessed Ammo, as the Goal says; Battle Rune grants Increased Shooting Range; Deceleration Rune synthesizes
    -2"/-4"; Raiding Drugs marks "+1 to hit rolls in melee". No bugs.
  - Batch 8 (Elven Jesters, Eternal Dynasty, Goblin Reclaimers, Havoc Brothers, High Elf Fleets, Human Defense Force,
    Human Inquisition, Infected Colonies; 48 spells): 48/48 parse as printed. Asphyxiating Fog grants "Counter in
    Melee" (#422 hand-off); Eternal Guidance marks Increased Shooting Range; Dark Assault grants "Shred in melee";
    Creator of Illusions "Unwieldy in melee"; Brain Infestation is a -1 hit StatModifier; Shock Speed synthesizes
    +2"/+4". Pow! is F1's GDF Goblin Reclaimers case. No new bugs.
  - Batch 9 (Jackals, Knight / Knight Prime Brothers, Lust Disciples, Machine Cults, Orc Marauders, Plague Disciples,
    Prime Brothers; 48 spells): 48/48 match. Corrode Weapons (skipped before #439) now synthesizes its AP(-1) shooting
    debuff, shooting-only and not on the target's own spells; Knight Sight marks "Unstoppable when shooting"; Head Bang
    marks "Rending in melee"; Power Field grants Shielded; Elder Protection "Spell Resistance". No bugs.
  - Batch 10 (Ratmen Clans, Rebel Guerrillas, Robot Legions, Saurian Starhosts, Soul-Snatcher Cults, Titan Lords,
    Titan Lords Change / Lust Disciples; 48 spells): 48/48 parse as printed. Triangulation Bots marks bare Indirect;
    Toxin Mist "Bane when attacking"; Deep Hypnosis keyed "Deep Hypnosis" (D2; Soul-Snatcher Cults also field Mind
    Control units - separate keys, so a unit moved by one can still be moved by the other); Gauss Bots (Shatter) carries
    the roll-dice clause; Power Surge is F1's GDF Ratmen case. Ratmen Clans' names are 3.6's. No new bugs.
  - Batch 11 (Titan Lords Plague / War Disciples, Voidspace Stalkers - new in 3.6 -, War Disciples, Watch / Watch
    Prime / Wolf / Wolf Prime Brothers; 48 spells): 48/48 match. Watch Brothers' "Blessed Ammo" is 3.6's Enhanced
    Watchborn grant (its OPR text reads "If this model has Versatile Attack ..." - the source's own wording, #433's
    domain, not a spell defect); Synaptic Stims synthesizes +2"/+4". No bugs.
  - **Pass summary:** 88 books / 528 snapshot spells reviewed; 527 imported and all 527 match their printed text
    (threshold, target count, affinity, unit/model, range, LoS, effect and the granted rule's meaning); 1 not imported
    (Combat Possession, truncated OPR source, D1). Mechanical cross-check of the final dump: 0 threshold / range / count
    / affinity / unit-or-model / LoS-vs-clause mismatches. Parser bugs found in the pass: none beyond the ones the
    parser slice fixed before the re-bake (the 11 skipped spells, the weapon-scope suffix, the move key), so no re-dump
    cycle was needed; the before/after dump diff of the re-bake showed exactly those changes and nothing else.
    Behaviour findings: F1 (Hazardous self-wound on spell hits, -> #434 follow-up), F2 (Lightning Bolt's missing
    clause, D4 OWNER REVIEW).
- 2026-10-30 (from #438): the shipped books are the 3.6.0 bake (`scripts/bake-books.sh`, from
  `~/Projects/GDF Armies/opr-json-snapshots-3.6.0-refetch-2026-10-30/` and the AoF twin - use these, not the 10-29
  folders; seven books changed overnight, #438 D1). (1) The 11 spells the importer skips today: GDF Blessed Sisters
  "Burn the Heretic"; "Shifting Form" x6 (GDF Change Disciples, Daemons of Change, Titan Lords Change Disciples; AoF
  Change Disciples, Daemons of Change, Giant Tribes Change Disciples); GDF Machine Cults "Corrode Weapons"; AoF Dragon
  Empire "Ancestral Guidance", Shadow Stalkers "Combat Possession", Sky-City Dwarves "Aether-Cloud Rune". (2) After the
  parser lands, re-run `scripts/bake-books.sh` (a full re-import re-stamps every spell; its `--import-spells` pass is a
  no-op on a fresh import, so a parser change only shows through the import itself) and re-pin what moves.
  (3) `BookSpellCoverageTests` / `BookSpellCastProbeTests` allowlists are EMPTY now - any newly parsed grant that does
  not resolve fails loudly. (4) Yours to decide (#438 D7): the catalog spell-phrase helpers "AP(+1) in melee", "AP(1)
  when shooting", "Bane when attacking", "Shred when attacking", "+6\" range when shooting" - no 3.6 book reaches them
  today, but `SpellPhraseCloneTests` pins them and Corrode Weapons ("loses AP(1) when shooting") is exactly one; delete
  any your parser does not emit (shipped Dead must stay 0).
- 2026-10-30 (from #433): the 45 spells that grant an Enhanced name once (29 AoF names / the GDF Boon family and
  friends: Lust / Change / Plague / War Boon, Litanies of War, Blessed Ammo, Righteous Fury, Banishing Sigil, Blessed
  Shroud, Burst of Rage, ..., Vigorous Return, Great Hunt) now resolve at import - none of the 90 import logs prints
  "Skipping unimplemented special rule 'Enhanced ..." any more, and the Vampiric Undead smoke loaded Vigorous Return
  ("get Enhanced Cursed Undead once") clean. Two semantics to carry into the per-book parity check: (1) a NextTrigger
  grant is spent at the FIRST hook+seat occurrence the Enhanced rule listens on, whether or not its `has X` gate passed
  (the #101 "entering a situation you can't benefit from wastes the buff" rule) - a Lust Boon on a unit without Lustbound
  is a wasted cast, correctly; (2) `Condition.UnitHasRule` now reads grant tokens, so a spell that grants BOTH X and
  Enhanced X (none does today) would compose. The supplement wrappers' grant targets are pinned
  (`Enhanced36ShippedDataTests.EveryWrapper_GrantsAnEnhancedRule_DefinedInTheSameUniverse_ThatReadsNoArgument`); the
  spells' grant targets are yours to pin per book.
- 2026-10-30 (from #435): (1) the AoF spells' 3.6 phrases the parser emits today now resolve from the AoF file (#435 D8):
  "AP(+1) on its weapons in melee" (Giant Tribes / Human Empire Elemental Form), "Bane on its weapons in melee" (High Elves
  Magical Surge), "Unstoppable on its weapons in melee" (Ossified Undead Drain Spirit) - clones of the catalog's "... in
  melee" rules - and "Surge on their weapons" (Chivalrous Kingdoms Curse of the Huntress, a mark; Surge + `isNotSpell`).
  Emitting the canonical names instead is equally fine. (2) Vampiric Undead's Seductive Invocation reads "... If failed you
  may move it by up to 6" in a straight line in any direction. Units may only be moved once per round in this way." but the
  parsed spell's `triggeredMove` has `oncePerRoundKey: null` - the parser should emit a key (whether the spell shares Mind
  Control's "Mind Control" key, i.e. one limit for the rule and the spell, is a reading call for you). (3) 38 AoF spells (29 names)
  grant an "Enhanced X" once (#433).
- 2026-10-30 (from #434): **names the parser may emit - both forms resolve today.** The GDF supplement now defines the
  3.6 phrases the current parser produces as exact clones of their canonical rules (#434 D5): "Unstoppable on their
  weapons when shooting" = "Unstoppable when shooting", "Shred on its weapons when shooting" = "Shred when shooting",
  "AP(1) on their weapons when shooting" = "AP(1) when shooting", "Indirect on their weapons" = "Indirect when shooting",
  "Rending on their weapons in melee" = "Rending in melee", "+6" range on its weapons when shooting" / "+6" range on their
  weapons when shooting" = "+6" range when shooting", "Bane on their weapons when attacking" = "Bane when attacking",
  "Shred on its weapons in melee" = "Shred in melee", "Shred on their weapons when attacking" = "Shred when attacking",
  "Unwieldy on its weapons in melee" (the 3.6 Unwieldy Debuff's own grant; "Unwieldy in melee" is the 3.5 name). If the
  parser learns the suffix, emit the CANONICAL names (they are the catalog rules the auras and buffs grant); the clones
  can then go (#436). Still undefined (AoF spells, #435): "AP(+1) on its weapons in melee" (= "AP(+1) in melee"), "Bane on
  its weapons in melee" (= "Bane in melee"), "Unstoppable on its weapons in melee" (= "Unstoppable in melee"), "Surge on
  their weapons" (= "Surge"). Note also (#434 D2): the "when shooting" / "when attacking" grants now skip spell hits
  (`isNotSpell`), so a buffed caster's damage spells no longer pick them up.
- 2026-10-30 (from #429): **the LoS clause -> flag is done in the importer; you only re-import.** Rule implemented:
  `OprBookImporter.SpellTextRequiresLineOfSight(text)` = `\bin line of sight\b`, case-insensitive, anywhere in the
  effect text, and `TryParseSpell` feeds it to EVERY shape's `TargetSelector.RequireLineOfSight` (damage, mark, grant,
  stat modifier, morale test, the synthesized grants) - no more "damage = LoS, everything else = no LoS". The
  `SpellShape` prefix still matches with the clause after the range. Counts over the 3.6.0 snapshots: GDF 288 spells
  (153 with the clause, 135 without), AoF 240 (134 / 106), **total 528 (287 / 241)**; every clause is in the opening
  target clause and every one is on an ENEMY-targeting spell (196 "Pick one enemy unit", 45 "one enemy model", 45 "up
  to two enemy units", 1 "up to three enemy units"). Your parity pass should therefore expect exactly 287 flagged
  spells. Until the re-import, the shipped 3.5 books keep their shape-based flags (427 true / 565 false). Tests:
  `OprBookImporterTests.Import_SpellLineOfSight_FollowsTheSpellsOwnClause_NotItsEffectShape` and
  `SpellTextRequiresLineOfSight_IsTheClauseTest`; `Import_ParsesSpells_DamageBuffAndSingleModel_SkippingUnparseable`
  now expects its clause-less 3.5 damage spell to need no LoS. The engine, picker, CLI and AI already read the flag.
- 2026-10-29 (from #431, D8 there): seventeen 3.6 spells name Smash or Pierce ("takes 6 hits with AP(1) and Smash",
  ...). `CastSpellStage` builds its synthetic spell weapon with range 0, and `targetOverHalfWeaponRange` is false for a
  range-0 weapon, so the "over half of this weapon's range" facet never fires for spell hits (Pierce's ignore-positive
  facet does). If the owner reads the spell's range as "this weapon's range", give the synthetic weapon the spell's
  range here. Other #431 rules spells name (Fracture, Fragment, Puncture, Obliterate, Decimate, Destructive,
  Disintegrate, Demolish, Purge, Reap, Scourge) need no range and work as `withRules` once the names resolve.
- 2026-10-29 (from #422): AoF Orcs "Evil Gaze" and GDF Elven Jesters "Asphyxiating Fog" grant "Counter-Attack" in the
  bundled 3.5 books (dropped by 3.6, now an allowlisted dead grant); their 3.6 text is "Pick one friendly unit
  within 12", which gets Counter in Melee once (next time the effect would apply)." - `addRule "Counter in Melee"`
  NextTrigger, and "Counter in Melee" is defined in `GdfRuleSupplement.json`.
- 2026-10-29: filed with the plan.

## Decisions

- 2026-10-31 **D1 - Combat Possession (AoF Shadow Stalkers) is not imported. OWNER REVIEW.** Its 3.6.0 source reads, in
  full, "Pick up to two friendly units within 12\", which" (the 2026-10-29 snapshot and the 10-30 re-fetch agree, and
  its `generation.effect` block has no id and no parameters - OPR's own data is truncated). Alternatives: (a) the 3.5
  text (+2"/+4" movement, threshold 3) - rejected, plan rule 6 forbids 3.5 fallbacks and the 3.6 threshold is 2, so
  the effect changed; (b) a castable no-effect placeholder - rejected, a spell that does nothing is exactly the silent
  failure this item exists to remove; (c) skip, warn as a SOURCE defect, and pin it. Chose (c): the importer's
  `TruncatedSpellWarning` names it, the bake does not fail on it, and `BookSpellCorpusPinTests.KnownSourceDefects`
  pins AoF Shadow Stalkers at 5 spells with the reason - when OPR fixes the text a re-fetch + re-bake makes that entry
  stale and the test fails, prompting the update. Total corpus: 527 of 528.
- 2026-10-31 **D2 - "Units may only be moved once per round in this way" keys the move by the SPELL'S OWN NAME.
  OWNER REVIEW.** Deep Hypnosis (GDF Soul-Snatcher Cults) and Seductive Invocation (AoF Vampiric Undead) print the
  same sentence as the Mind Control rule (#434, key "Mind Control"), and both books also field Mind Control units.
  Alternatives: (a) share "Mind Control" - one limit across the rule and the spell (one shove per enemy unit per round
  from either source); (b) per-ability keys - each text's "in this way" is its own effect. Chose (b): the literal
  per-card reading, with no hidden coupling between a spell and a differently named rule; a unit moved by Mind Control
  can still be moved by the spell that round (pinned by
  `Spells36RuleIntegrationTests.OncePerRoundSpellMove_AUnitMovedByMindControl_CanStillBeMovedByTheSpell`). Flipping to
  (a) is one line in `TryParseSpell` (key = "Mind Control") plus a re-bake.
- 2026-10-31 **D3 - the synthetic spell weapon's range is the spell's range (#431 hand-off). OWNER REVIEW.** 17 3.6
  spells name Smash or Pierce, whose distinctive facet is "when attacking units over half of this weapon's range
  away". With the old range-0 weapon that facet could never fire for a spell (only Pierce's ignore-positive-modifiers
  part did). Alternatives: (a) keep range 0 - Smash on a spell is dead text and Pierce half-works; (b) the spell's
  range stands for "this weapon's range" - an 18" Smash spell re-rolls Defense 6s beyond 9", a 24" one beyond 12".
  Chose (b): OPR attached these rules to spells, so they are meant to do something, and the spell's reach is the only
  range a spell has. `CastSpellStage` builds the weapon with `chosen.Target.RangeInches`; pinned by
  `PierceSpell_OverHalfTheSpellsRange_GetsAp` (mutation-checked: reverting to 0 fails the 12" case). Nothing else reads
  the synthetic weapon's range (`IsMelee()` / `IsRanged()` are not consulted on the spell path).
- 2026-10-31 **D4 - "Roll as many dice as hits to see if \"on rolls of 6\" effects trigger" gets no flag. OWNER
  REVIEW (F2).** The spell pipeline always rolls spell hits as dice for the on-6 fold, and 21 of the 22 spells whose
  weapon rule triggers on a 6 print the clause. The 22nd is AoF Kingdom of Angels' Lightning Bolt: "Pick one enemy
  unit within 18\" in line of sight, which takes 6 hits with AP(1) and Shatter." - read literally no dice are rolled,
  so Shatter could never trigger. Alternatives: (a) a `DealHits` flag set from the clause, with a no-roll path for
  unflagged spells - Lightning Bolt's Shatter becomes dead text; (b) no flag - Lightning Bolt's Shatter procs like
  Ossified Undead's Shattering Blast (same rule, clause printed). Chose (b): reads as an omission, and (a) adds schema
  for one spell. The "on rolls of 1" variant (Hazardous) is finding F1, deferred.
- 2026-10-31 **D5 - the 11 spells the importer skipped before #439, one by one. OWNER REVIEW.**
  - GDF Blessed Sisters "Burn the Heretic", "Shifting Form" x6 (GDF Change Disciples, Daemons of Change, Titan Lords
    Change Disciples; AoF Change Disciples, Daemons of Change, Giant Tribes Change Disciples), AoF Sky-City Dwarves
    "Aether-Cloud Rune" - all eight read "Pick one enemy unit within 18\" in line of sight, which loses 2 spell tokens.
    Note that the target may not spend those spell tokens to prevent this effect." -> `Effect.ConsumeToken(SpellTokens,
    2)` on the target (Spell Drain's primitive; losing what it has if it holds fewer), and the note -> D11.
  - GDF Machine Cults "Corrode Weapons" - "Pick up to two enemy units within 18\" in line of sight, which loses AP(1)
    on its weapons when shooting once (next time the effect would apply)." -> the existing synthesized "Corrode
    Weapons Effect" (attacker-side `ReduceArmorPenetration(1)`), its condition now `Not(IsMelee) AND IsNotSpell` (D10).
  - AoF Dragon Empire "Ancestral Guidance" - "Pick up to two enemy units within 18\" in line of sight, which friendly
    units Indirect on their weapons against once (next time the effect would apply)." - the source dropped "get(s)";
    the mark is the only reading -> `MarkTarget("Indirect")` (the verb is optional in the mark shape now).
  - AoF Shadow Stalkers "Combat Possession" - "Pick up to two friendly units within 12\", which" -> not imported, D1.
- 2026-10-31 **D6 - the weapon-scope suffix folds onto the canonical names, bare "on their weapons" onto the bare
  rule.** "Indirect on their weapons" -> Indirect (the #434 clone was entry-for-entry Indirect); "Surge on their
  weapons" -> Surge (the #435 clone added `IsNotSpell`, but a mark is claimed only at a real attack's hit roll -
  `DetermineHitRollStage.ClaimTargetMarks` - never by spell hits, so the gate had nothing to exclude; the one edge,
  a claimed grant lingering until the claimer's next attack, is handed to #434's follow-up). Case is left as printed
  ("Bane when Shooting"); the resolver is case-insensitive.
- 2026-10-31 **D7 - "+N\" range on its/their weapons when shooting" -> Increased Shooting Range for N = 6** (the
  supplement's Increased Shooting Range Buff / Mark already grant it for the identical text; the #377 D3 range-mark
  peek reads its `RangeModifier`). Any other N keeps the phrase name "+N\" range when shooting", which resolves nowhere,
  so `BookSpellCoverageTests` fails loudly instead of granting +6". The corpus has only +6 (Battle Rune, Eternal
  Guidance, Clearview Leaves).
- 2026-10-31 **D8 - the five catalog spell-phrase helpers (#438 D7).** With canonical names emitted: "AP(+1) in melee"
  (Elemental Form x2), "AP(1) when shooting" (Coordinated Aggression), "Bane when attacking" (Toxin Mist, Irresistible
  Grief) and "Shred when attacking" (The Founder's Curse, Poison Mist) are reached by baked spells - kept. "+6\" range
  when shooting" is reached by nothing (D7) - deleted with its `SpellPhraseCloneTests` row; the two engine tests that
  used it as a grant name now grant Increased Shooting Range. A legacy army naming it now drops that grant at load ->
  #440's legacy-load check.
- 2026-10-31 **D9 - the 15 phrase clones are deleted** (GDF 11: the ten #434 D5 spell clones + "Unwieldy on its weapons
  in melee"; AoF 4: the #435 D8 clones). No re-baked book names any of them; the GDF Unwieldy Debuff now grants
  "Unwieldy in melee" (same entries) so its clone could go too. Shipped-data tests now pin their absence.
  `--validate-rules` 416 -> 401.
- 2026-10-31 **D10 - Corrode Weapons Effect also gates `IsNotSpell`** ("on its weapons when shooting": the debuffed
  unit's own spell hits are not its weapons - #434 D2's convention for the "when shooting" grants).
- 2026-10-31 **D11 - the drain note as a cast-assist lock.** "The target may not spend those spell tokens to prevent
  this effect": the only window where a target can spend tokens against a cast is the Caster rule's "+1/-1 per token"
  assist before the roll. Each picked target keeps back min(N, its OWN tokens) - offered `Available - reserved`, and
  `SpellPurse.Spend(reserveOwn:)` draws lent accumulator pools before the reserved own tokens (lent pools are not its
  tokens). Non-target casters are unaffected. No resolver change: CLI / GUI / AI just see the smaller count.
- 2026-10-31 **D12 - the expected invariants, checked.** 287 LoS spells, all enemy-targeting (as #429 said); 17
  Smash/Pierce (as #431 said); the Enhanced one-shot grants are **80 spells over 44 distinct names** (GDF 42 / 29, AoF
  38 / 29 - #435's 38 AoF spells and 29 names are right; #433's "45 spells" was a miscount). All pinned.
- 2026-10-31 **D13 - a parser gap fails the bake, a truncated source does not.** `scripts/bake-books.sh` collects every
  "skipped: unrecognized effect text" line from the import step and exits 1 after step 1; the truncated-source warning
  is a different phrase. This plus `BookSpellCorpusPinTests` is the "an unparsed spell must fail" pin.

## Outcome

Done 2026-10-31. The importer reads every 3.6.0 spell phrasing in the 88 books: 527 of 528 spells import (the 528th,
Combat Possession, is truncated in OPR's own data - D1), all 527 verified against their printed text in an 11-batch
parity pass (0 mismatches), and the corpus is pinned so an unparsed spell fails a test and the bake. Grants and marks
carry the catalog's canonical names; the 15 phrase clones and one catalog helper are gone (validate 401 definitions,
shipped coverage 30864 references / Dead 0); the 10 formerly-skipped spells parse (8 token drains with the
no-hinder note enforced, Corrode Weapons, Ancestral Guidance); the morale-move spells carry their once-per-round key;
Smash / Pierce's over-half facet reads the spell's range. Engine 4442 passed / 1 skipped, app 3995 passed, corpus cast
sweep 527/527, re-bake byte-identical 88/88, headless smokes exit 0 with spells cast in both systems. Owner review:
D1, D2, D3, D4, D5. Deferred: F1 (Hazardous self-wound on spell hits), the stale claimed-mark edge, the
"when shooting" one-shot grants a spell's hits spend, the AI's drain targeting -> #434's follow-up list; legacy armies
naming deleted names -> #440; Forge / glossary spell display (drains, a 5-spell book, canonical names vs printed
phrasing) -> #441.

## Rules for the agent (plan section 1, restated)

- Branch `feature/opr-3.6` in BOTH repos; check `git branch --show-current` in each before committing.
  Never commit, merge or push `master`. Submodule-first commit cadence; subjects start with `NNN:`.
- 3.6 rule text: `~/Projects/GDF Armies/Rules 3.6.0/` (core PDFs' text extracts, common-rules JSON) and the
  3.6.0 book snapshots. Never commit anything from that folder. No 3.5 fallbacks or version flags.
- Vertical slice: engine change + CLI resolver + GUI resolver/overlay + AI resolver legality + an
  integration test mirroring the nearest `*RuleIntegrationTests` + the `docs/` line if a number changed.
- Verify per CLAUDE.md (engine suite green, full build, headless smoke exit 0) and
  `FdgRaylib --rule-coverage FdgRaylib/Assets/Books` not regressing. Never commit red.
- Keep this file's Notes/Decisions current and flip the row in `docs/opr-3.6-upgrade-plan.md` section 5.
  A design fork stops for Chris's sign-off with options + a recommendation written here.
