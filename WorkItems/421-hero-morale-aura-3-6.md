# 421 — Hero 3.6: Tough(9+) heroes lend Quality for morale within 6"; not-Shaken gate

**Status**: todo
**Related**: #415, #006 (Hero join + hero-Quality morale), #428 (most-models gating), #175, #382 (joined-hero auras), #342 (hero name)
**Branch**: `feature/opr-3.6`. Model/effort: Opus 5.5 / high. Depends on #428 for the aura's "most models" wording only.

## Goal

Core Rules v3.6.0, Hero: "Heroes with up to Tough(6) may deploy as part of one multi-model unit without
another Hero, but must use the unit's Defense until all other models have been killed. The Hero's unit, and
units within 6" of friendly Heroes with Tough(9) or higher, may use the Hero's Quality for Morale Tests as
long as the Hero isn't Shaken." In 3.5.1 the joined hero "may take morale tests on behalf of the unit" with
no Shaken gate and no 6" aura.

"Done" = (a) a joined hero's unit tests morale at the hero's Quality only while the hero is not Shaken
(a Shaken hero's unit IS the hero's unit, so the gate collapses to "the unit isn't Shaken" - confirm with
the engine's Shaken token being unit-level); (b) any friendly unit within 6" of a non-Shaken friendly Hero
with Tough(9)+ (a lone hero or one riding in a transport? - off-table heroes don't project; decide and
record) may use that hero's Quality when it takes a Morale Test, measured at test time, best of several
heroes; (c) "may use" is a benefit, so the engine takes the better Quality automatically (no prompt), with a
log line naming the hero.

## What changes

1. Engine: `HeroStatRules` morale-quality seam gains the Shaken gate; new aura evaluation at every morale
   dispatch site (`MoraleUtilities.TakeMoraleTest` and friends) scanning friendly Tough(9)+ heroes within 6"
   of the testing unit (base-edge distance per the project's geometry seam). The hero's Tough is its rule
   argument, not its max wounds.
2. Tests: joined hero Shaken -> unit tests at its own Quality; Tough(9) hero at 5" -> neighbour tests at the
   hero's Quality; at 7" -> not; Tough(6) hero at 5" -> not; two heroes -> best; hero Shaken -> no aura.
3. Presentation: morale roll beat names the Quality source; CLI prints it.

## Acceptance

Tests green; smoke exit 0; `docs/EngineNotes.md` hero paragraph updated.

## Code anchors (survey 2026-10-29)

- `Rules/Dispatch/HeroStatRules.cs`, `HeroJoinResolver.cs`, `MoraleUtilities.cs` (TakeMoraleTest, Shaken
  short-circuit), `CoreRuleCatalog.Hero`. See the appended survey rows below for exact lines.

### Survey rows (code survey 2026-10-29)

Findings that shape the item: a joined hero's unit tests at the hero's Quality whenever the hero is alive
(no Shaken gate); there is no aura and no Tough(9)+ logic; the join cap is Tough(6), so Tough(9)+ heroes are
always standalone units (the aura is their only morale effect).

- `Rules/Dispatch/HeroStatRules.cs:27-39` `GetMoraleQuality`; `HeroJoinResolver.cs:26` `MaxHeroToughForJoin = 6`;
  Shaken auto-fail `MainUnitActionStage/MoraleUtilities.cs:75`.
- Callers: `DetermineMoraleSaveNeededStage.cs:31, :35`, `MoraleUtilities.cs:293` (wound-driven),
  `CastSpellStage.cs:396`, `StateMachine/GameOperationServices.cs:152`.
- Tests: `HeroStatRulesTests.cs`, `ShakenAlwaysFailsMoraleTests.cs`.

## Notes

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
