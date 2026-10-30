# 439 — Spells 3.6: parser grammar, re-import, per-book parity verification

**Status**: todo
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

(none yet)

## Outcome

(open)

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
