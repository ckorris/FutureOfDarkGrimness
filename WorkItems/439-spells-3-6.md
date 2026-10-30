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
