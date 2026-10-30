# 429 — Casting 3.6: LoS per spell, +1 casting modifiers, once per activation, Spell Drain

**Status**: todo
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
