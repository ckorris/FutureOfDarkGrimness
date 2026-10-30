# 429 — Casting 3.6: LoS per spell, +1 casting modifiers, once per activation, Spell Drain

**Status**: done 2026-10-30 (engine `a4edfc53`, `524e3976`, `b831993a`, `62b46eee`, `381c4241`, `c6c8c693`, `a09c7599`;
the superproject commit `429: ...` bumps the pointer with the CLI/GUI pickers, the Spell Drain supplement entry, docs and
this ledger). Owner review asked on D2 and D6. Awaiting GUI hand-verify (list below).
**Related**: #415, #034 (spell content), #234/#249/#244 (cast gating, one try per spell, self-boost), #377 (spell parity), #427 (Spell Amplifier entry), #439 (spell re-import + parser)
**Branch**: `feature/opr-3.6`. Model/effort: Opus 5.5 / high.

## Goal

Core Rules v3.6.0, Caster(X): "Gets X spell tokens at the start of each round, but can't have more than 6 at
once. Once per activation, before attacking, spend as many tokens as the spell's value to try casting one or
more spells (only one try per spell). Pick a target and roll one die, and on 4+ resolve the spell's effect.
Models within 18" of the Caster may spend any number of spell tokens at the same time before rolling to give
the Caster +1/-1 to the roll per token." Changes vs 3.5.1: (a) the "target in line of sight" requirement left
the Caster rule and now lives in each spell's text ("Pick one enemy unit within 18" in line of sight") - some
spells have no LoS clause and may target out of sight; (b) boosters need only be within 18" of the Caster,
no LoS; (c) "Once per activation" phrasing (the engine's #249 gate stays). Patch notes: Caster Staff
upgrades give +1 to casting via Spell Amplifier (#427), so spells "go off on 3+, or on 2+ with one spell
token" - that is just the modifier arithmetic. New book rule Spell Drain: "Once per activation, before
attacking, pick one enemy model within 18" in line of sight, which must take a Morale Test. If failed, it
loses 2 spell tokens."

"Done" = `SpellTargeting` reads a per-spell `RequiresLineOfSight` flag (set by the #439 parser from the "in
line of sight" clause; default false in 3.6, i.e. no LoS unless the text says so); the caster-level LoS gate
is deleted; booster eligibility drops its LoS check; Spell Amplifier and any other `castRollBonus` fold into
the cast roll; Spell Drain works (token removal on the target model's unit); `ChooseSpellRequest` and the
GUI spell picker show the LoS requirement per spell.

## What changes

1. Engine: `RuntimeSpell` / spell target model gains `RequiresLineOfSight`; `SpellTargeting` + `CastSpellStage`
   use it; booster scan in `CastSpellStage` (18" only); cast roll modifier sink reads Spell Amplifier (#427)
   plus the existing Spell Conduit `castRollBonus`; `Spell Drain` as an activated ability in the supplement
   (`Effect.ConsumeToken` on the target - exists since the audit, LAT-7).
2. Resolvers: the spell picker lists "LoS" per spell and greys spells whose only legal targets are out of sight;
   CLI shows "(LoS)" in the list; booster prompt no longer filters by LoS.
3. Tests: a spell without the clause targets an out-of-sight unit; one with the clause cannot; booster at 18"
   behind a wall boosts; Spell Amplifier +1 turns a 3 into a success; Spell Drain removes 2 tokens.

## Acceptance

Tests green; `CasterRuleIntegrationTests` updated; hand-verify (Chris): cast a no-LoS spell through a wall.

## Code anchors (survey 2026-10-29)

- `CastSpellStage/CastSpellStage.cs` (4+ roll, booster scan), `Rules/Dispatch/RuntimeSpell.cs`, `SpellTargeting`,
  `ChooseSpellRequest` + GUI picker (#244), `OprBookImporter` spells section (for the flag's data path, #439).
  See the appended survey rows below for exact lines.

### Survey rows (code survey 2026-10-29)

Findings that shape the item: LoS is ALREADY a per-spell flag (`TargetSelector.RequireLineOfSight`) enforced
in `SpellTargeting.WithinRangeAndSight`; the Caster catalog entry has no LoS logic. The importer sets the
flag by spell SHAPE (damage spells true, every other shape false), so the 3.6 work is: set it from the "in
line of sight" clause instead (#439's parser), confirm boosters are not LoS-filtered, and add the +1 modifiers.

- Caster `CoreRuleCatalog.cs:1277-1295`. Flag `Rules/Foundation/TargetSelector.cs:59-61`; enforcement
  `CastSpellStage/SpellTargeting.cs:108-133` (`WithinRangeAndSight`; from `GetEligibleTargets` :26/:45); callers
  `CastSpellStage.cs:484/:733/:792`, `ChooseActionStage.cs:111/:789`; text `SpellText.cs:148`; AI
  `Ai/Tactician/SpellValuation.cs:166-184`.
- Importer flag by shape: `OprBookImporter.cs:638` (damage true), :648/:690/:710/:716/:736 (false). Shipped
  books: 427 true / 565 false.
- Cast roll `CastSpellStage.cs:56` (4), :207 net modifier, :221-223; `DiceUtilities.cs:25` clamp [2,6]; assist
  range `GameWideConstants.cs:72` (18"); token cap :65 (6).
- Tests: `CasterRuleIntegrationTests.cs`, `OprBookImporterTests.cs:427`, `SpellConduitRuleIntegrationTests.cs`,
  `ArmyRuleDataResumeTests.cs`.

## Notes

- 2026-10-30: **Landed**, one engine commit per numbered part of the brief (hashes in Status). Tests: engine 3742 ->
  3771 passed (+29, 1 skipped as before); app 3066 -> 3082 (+16: 11 picker resolver tests, 4 Spell Drain shipped-data
  tests, and the data-driven `EverySupplementRuleFires(Gdf:Spell Drain)` case); `--validate-rules` OK 293 definitions
  (was 292; + Spell Drain); `--rule-coverage` unchanged at `Dead: 36 (36 no-definition across 2 names)` (no shipped
  3.5 book names Spell Drain yet); headless smoke exit 0 with a `Game result:` line; `--scenario
  Scenarios/377-spell-verify.json` headless (pick Cast) shows the new CLI "Casting roll: 4+ before boosts." line. Every
  new test was broken on purpose and failed (importer clause -> shape; `SpellTargeting` LoS forced on / off; a LoS
  filter added to the booster scan; the `- grantedPeek` term, `+ rules` term and `PeekNet` -> `ConsumeNet`;
  `ConsumeTokensFromUnit` count 0 and the morale branch inverted; the LoS reason and tag dropped; the CLI tag and GUI
  note dropped; `SpellValuation` LoS forced on / off; the Caster description reverted).
  - **1 LoS per spell.** The engine already enforced a per-spell flag; the importer now sets it from the text (D1), for
    every shape. Engine default confirmed: a spell whose flag is false (no clause) targets a unit behind a Blocking
    wall (`Cast36_ASpellWithoutALineOfSightClause_TargetsAUnitBehindAWall`), the same spell with the flag cannot, and
    without the wall it can (control). **Counts over the 3.6.0 snapshots** (`spells` arrays, 90 books): GDF 49 books /
    288 spells - 153 with the clause, 135 without; AoF 41 books / 240 spells - 134 with, 106 without; total 528 - **287
    with, 241 without**. Every clause sits in the opening target clause and every one targets ENEMIES (196 "Pick one
    enemy unit within N"", 45 "Pick one enemy model", 45 "Pick up to two enemy units", 1 "Pick up to three enemy
    units"); no friendly spell needs LoS, and no other "sight" phrasing occurs. The re-import is #439 (hand-off note
    there); the shipped 3.5 books keep their shape-based flags until then (427 true / 565 false).
  - **2 Boosters.** `CastSpellStage.FindEligibleAssisters` was already range-only (18", `CASTER_ASSIST_RANGE_INCHES`,
    base to base, no LoS); pinned for both sides with a booster and a hinderer 15" away behind a wall, and commented.
    The CLI/GUI booster prompts never filtered anything (nothing to change).
  - **3 Cast roll modifiers.** All five terms (rules = Spell Amplifier via `CastRollRules.Net`, relay = Spell Conduit's
    `castRollBonus`, self-boost, boosters/hinderers, granted Casting Buff/Debuff tokens) were already one sum and one
    `ClampSuccessRollNeeded`; no other cast-roll bonus exists in the engine or the supplements. Gap fixed: the picker's
    `BaseThreshold` ignored the caster's granted cast tokens (D3). New `ChooseSpellRequest.BaseThresholdNote` ("base 4+,
    rules +1, granted -1") shares its words with the banner (`DescribeRollParts`). Tests: Amplifier +1 turns a natural 3
    into a success (`ACasterWithAnAmplifiedStaff_CastsOnAThree`, from #427); Amplifier plus one enemy -1 token nets 4+ (3
    fails, 4 lands); Amplifier + one self-boost token casts on a 2 (the patch notes' "2+ with one spell token");
    Amplifier + relay = 2+ with both named in the banner.
  - **4 Spell Drain.** Book rule in GDF Blessed Sisters, Daemons of Change, Human Inquisition and AoF Beastmen, Daemons
    of Change, Sky-City Dwarves (identical text everywhere): "Once per activation, before attacking, pick one enemy
    model within 18" in line of sight, which must take a Morale Test. If failed, it loses 2 spell tokens." No new
    engine piece: the Mind Control / Fatigue Debuff shape (`moraleTestThen`, executed by
    `GameOperationServices.MoraleTestThen`) with `consumeToken SpellTokens 2` as the on-failure arm; `RemoveTokens`
    floors at zero. Authored once in `GdfRuleSupplement.json` (D5, D6).
  - **5 Picker and CLI.** `SpellOption.RequiresLineOfSight` (engine-computed, JSON round-trip tested); the CLI prints
    "(LoS)" after such spells and a "Casting roll: N+ before boosts (terms)." line; the GUI tags the row "(LoS)" with a
    hover tooltip and its "Roll needed" line names the terms; a LoS spell whose in-range targets are all hidden is
    greyed with "no target in line of sight" (D4). Resolver tests: `FdgRaylib.Tests/ChooseSpellResolverTests.cs` (new;
    the CLI via redirected console like `SelectionResolverTests`, the GUI through its pure string helpers).
  - **6 AI.** `SpellValuation` already read the per-spell flag (no caster-level assumption); pinned by
    `TacticianCastingTests.Casting_ReadsLineOfSightFromTheSpell_NotTheCaster` (both polarities). Pricing gaps noted on
    #442 (D7).
  - **7 Text parity.** `CoreRuleCatalog.Caster.Description` is the 3.6 text verbatim, ASCII-folded (D8). Casting
    leftovers handed to #436.
- 2026-10-29 (from #427): **Spell Amplifier is in, and the hook is yours to extend.** Rules author
  `Effect.RollModifier(ERollKind.Cast, +N)` at `EHookID.Casting_OnSpellCastAttempt` (previously declared but unwired;
  context `SpellCastAttemptContext(IUnit Caster)` in `Rules/Dispatch/Contexts/` - widen the record, e.g. with the spell
  name, if one of your "+1 to casting rolls" rules needs it). `CastRollRules.Net(caster, evaluator, live)`
  (`Rules/Dispatch/CastRollRules.cs`) folds them over the unit, its living models AND every living model's weapon
  (Spell Amplifier is a weapon-scoped catalog rule, `CoreRuleCatalog.SpellAmplifier`; an argument-less rule fires once
  per bearer, so two staffs are +1). Wired in `CastSpellStage`: `int rules = CastRollRules.Net(..., live: true)` is
  added into `netModifier` beside boost / assist / granted / relay (line ~211), the roll breakdown prints "rules +N",
  and the spell picker reads `CastRollRules.Net(..., live: false)` into `ChooseSpellRequest.BaseThreshold` (4+ less
  the bonus - so GUI "Roll needed", the CLI boost cap and AI boost sizing already agree). The 2+..6+ clamp still
  applies to the sum (`DiceUtilities.ClampSuccessRollNeeded`). Not touched: the booster scan, per-spell LoS, once per
  activation, Spell Drain; `Casting_OnSpellAssistOffered` / `_OnSpellResolved` remain unwired. Tests:
  `SpellAmplifierRuleIntegrationTests` is the template for yours (RecordingCastRequester + a fixed die + a debuff spell
  whose landing is the observable).
- 2026-10-29: filed with the plan.

## Decisions

- **D1 - the importer's LoS contract (2026-10-30).** `TargetSelector.RequireLineOfSight` is true iff the spell text
  matches `\bin line of sight\b` (case-insensitive) anywhere in it, for EVERY effect shape (it was true for damage
  spells and false for everything else). Matching anywhere rather than only inside the target clause is deliberate:
  all 287 corpus clauses are in the target clause, so the two are equivalent today, and the looser test cannot silently
  drop a reworded variant. `OprBookImporter.SpellTextRequiresLineOfSight`.
- **D2 - "Once per activation" stays the layered Cast action (2026-10-30) - OWNER REVIEW.** 3.6 rewrote 3.5's "at any
  point before attacking" as "Once per activation, before attacking, spend ... to try casting one or more spells (only
  one try per spell)". As the brief instructed, the engine keeps its reading: Cast is a layered menu action that may
  be taken again (cast, move, cast another spell) until the unit attacks, with #249's one try per spell. A stricter
  reading - ONE casting window per activation, closed once the unit does anything else after casting - is not built.
  Cost if wanted: a "cast window closed" flag on `UnitActionContext` set when a non-Cast action follows a cast, gated in
  `ChooseActionStage.GetCanCast` and `CastSpellStage.Enter` (the AI already casts before moving).
- **D3 - the picker's threshold carries the caster's granted cast tokens (2026-10-30).** `BaseThreshold` = 4+ less the
  passive rules (Spell Amplifier) AND the caster's Casting Buff/Debuff tokens, PEEKED (`GrantedRollModifiers.PeekNet`;
  browsing and cancelling the menu never spends a one-shot debuff). Excluded on purpose, because each depends on a
  choice still to come: the relay bonus (which target - stated by the relay note and per target row), the self-boost
  (this menu) and boosters/hinderers (asked after the caster commits). Behaviour change: a debuffed caster used to see
  "Roll needed: 4+" and a boost cap one short while its real roll needed 5+.
- **D4 - the greyed-row reason (2026-10-30).** "no target in line of sight" only when the spell requires LoS AND some
  target would be legal from some origin (caster or relay) if sight were ignored; otherwise the generic "no valid
  target" (out of range is not a sight problem). The tag is its own field: the label stays "Name (cost)" because the
  Tactician finds spells by it.
- **D5 - Spell Drain is authored once, in the GDF supplement (2026-10-30).** The AoF text is identical, and the AoF bake
  merges GDF then AoF (`RuleSupplementSet`), so an AoF copy would be a redundant redefinition. No shipped (3.5) book
  names it, so coverage is unchanged; the #438 re-bake will embed it in the six books.
- **D6 - Spell Drain may target any enemy; the tokens come off its unit's pool (2026-10-30) - OWNER REVIEW.** "Pick one
  enemy model" is modelled as picking that model's unit (target selectors are unit-level: a unit with one model within
  18" and in sight qualifies), with no Caster filter (`requiredToken: null`), as written. Picking a unit that holds no
  tokens wastes the once-per-activation use and nothing else (a rule's Morale Test does not Shake on a fail). "It loses
  2 spell tokens" drains the picked unit's pool, which is where the engine keeps spell tokens (a joined Caster hero funds
  its host's pool; Spell Accumulator pools elsewhere are untouched). Alternative: `requiredToken: {"id":
  "SpellTokens"}` to list only units holding tokens - a UX filter the text does not state.
- **D7 - the AI is confirmed, not repriced (2026-10-30).** `SpellValuation` already read LoS per spell. There is no AI
  boost policy (solo and Tactician always reply 0 boost), so "the AI's boost sizing" reads nothing from
  `BaseThreshold`. The flat 0.5 success chance (no Amplifier, granted tokens, relay, boost or hinder) and the
  caster-only targeting mirror (no relay origin) are recorded on #442 rather than changed here: repricing moves Tactician
  choices outside this item's scope.
- **D8 - Caster text (2026-10-30).** The catalog description is the Core Rules v3.6.0 Caster text verbatim, ASCII-folded
  (curly apostrophe and inch mark to `'` and `"`), "X" kept like the other X-rated descriptions; it has no LoS clause
  because 3.6 has none.

## Ledger

| Facet | Code anchor | Test(s) |
|-------|-------------|---------|
| LoS from the spell text (data path) | `OprBookImporter.SpellTextRequiresLineOfSight`, `TryParseSpell` | `OprBookImporterTests.Import_SpellLineOfSight_FollowsTheSpellsOwnClause_NotItsEffectShape`, `.SpellTextRequiresLineOfSight_IsTheClauseTest` |
| No clause = no LoS needed (engine) | `SpellTargeting.WithinRangeAndSight` | `CasterRuleIntegrationTests.Cast36_ASpellWithoutALineOfSightClause_TargetsAUnitBehindAWall`, `.Cast36_ASpellWithTheClause_CannotTargetThatSameUnit`, `.Cast36_TheClauseIsWhatTheWallBlocks_WithoutTheWallTheSameSpellLands` |
| Boosters 18", no LoS | `CastSpellStage.FindEligibleAssisters` | `.Cast36_ABoosterBehindAWall_FifteenInchesAway_StillBoosts`, `.Cast36_AnEnemyHindererBehindAWall_StillHinders` |
| One sum, one [2,6] clamp | `CastSpellStage.Enter` (`netModifier`, `ClampSuccessRollNeeded`) | `SpellAmplifierRuleIntegrationTests.ACasterWithAnAmplifiedStaff_CastsOnAThree`, `.AmplifierPlusOne_AndOneEnemyHinderToken_NetFourPlus`, `.AmplifierAndOneSelfBoostToken_CastOnATwo`, `SpellConduitRuleIntegrationTests.ARelayedCast_AndSpellAmplifier_StackIntoOneThreshold` |
| Banner breakdown | `CastSpellStage.BuildRollBreakdown` / `DescribeRollParts` | `.AmplifierAndHinder_TheBannerShowsBothTerms`, `.ARelayedCast_AndSpellAmplifier_StackIntoOneThreshold` |
| Picker own threshold + note | `CastSpellStage.PickSpell`, `ChooseSpellRequest.BaseThreshold` / `.BaseThresholdNote` | `.ThePickersBaseThreshold_CarriesTheCastersGrantedTokensToo_WithoutSpendingThem`, `.AnUnmodifiedCaster_HasNoBaseThresholdNote`, `ConcreteRequestTests.ChooseSpellRequest_LineOfSightTagAndThresholdNote_RoundTrip` |
| Spell Drain | `GdfRuleSupplement.json` "Spell Drain" (`moraleTestThen` + `consumeToken`) | `SpellDrainRuleIntegrationTests` (6: fail -2, floor 0, pass 0, no Shaken, once per activation, LoS), `FdgRaylib.Tests/SpellDrainShippedDataTests` (4), `RuleSupplementLintTests.EverySupplementRuleFires(Gdf:Spell Drain)` |
| Picker LoS tag + reason (engine) | `CastSpellStage.BuildSpellOffer`, `SpellOption.RequiresLineOfSight`, `CastSpellStage.NoTargetInSightReason` | `CasterRuleIntegrationTests.Cast36_ThePicker_TagsLineOfSightSpells_AndSaysWhyOneIsGreyed`, `.Cast36_ALineOfSightSpellWithNothingInRange_KeepsTheGenericReason` |
| CLI picker | `ChooseSpellResolver.RowLabel` / `.CastingRollLine` | `FdgRaylib.Tests/ChooseSpellResolverTests.Cli_*` (3) |
| GUI picker | `GuiChooseSpellResolver.RowText` / `.RollNeededLine` / `DrawLineOfSightTooltip` | `ChooseSpellResolverTests.Gui_*` (7), `.EveryPickerString_IsAscii` |
| AI per-spell LoS | `SpellValuation.WithinRangeAndSight` | `TacticianCastingTests.Casting_ReadsLineOfSightFromTheSpell_NotTheCaster` (2 cases) |
| Caster 3.6 text | `CoreRuleCatalog.Caster.Description` | `CasterRuleIntegrationTests.Caster_CarriesTheCore360Wording_WithNoLineOfSightClause` |

## GUI hand-verify (Chris)

1. Cast a no-LoS spell through a wall: any non-damage spell from a shipped 3.5 book carries the flag false (e.g. the
   Coven Magus's spells in `Scenarios/377-spell-verify.json`; 3.6 books with real clauses arrive with #439). With an
   enemy fully behind a Blocking piece, the target row is still offered and the spell lands.
2. The picker's LoS tag: a damage spell from a shipped 3.5 book (flag true) shows "(LoS)" with the tooltip; put its
   only target behind a wall and the row greys with "(no target in line of sight)".
3. The Spell Amplifier threshold line: a caster with a Caster Staff shows "Roll needed: 3+ (base 4+, rules +1)", and
   one boost token reads "Roll needed: 2+ (base 4+, rules +1, self +1)"; the cast banner lists the same terms.

## Outcome

Done 2026-10-30. Casting follows Core Rules v3.6.0: line of sight is each spell's own requirement (importer contract
set, engine default confirmed, picker/CLI/AI aware), boosters are range-only, every cast-roll modifier composes in one
sum with one clamp and the picker states the caster's own threshold, Spell Drain is playable, and the Caster text is
the 3.6 wording. Not done here, by design: the spell re-import (#439), the strict one-window reading (D2), AI repricing
(#442).

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
