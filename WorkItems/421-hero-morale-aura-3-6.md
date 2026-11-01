# 421 — Hero 3.6: Tough(9+) heroes lend Quality for morale within 6"; not-Shaken gate

**Status**: done 2026-10-30 (owner review asked on D3, D4; GUI hand-verify pending)
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

- 2026-10-30: **Landed.** Engine commits (submodule, `feature/opr-3.6`): `faa13f11` (part 1, Shaken gate),
  `d1dcc838` (part 2, the aura + the one morale-Quality seam), `05be3515` (part 3, Defense-clause pin), `0f48e94f`
  (part 4, log line / roll chip / Hero description). The superproject commit `421: ...` bumps the pointer with
  `docs/EngineNotes.md`, this ledger, the plan row and the #442 / #436 hand-offs. No app code changed: the CLI prints
  the engine's log line, and the GUI dice panel renders the roll's chips verbatim (`DiceOverlay.LayoutChips`).
  Engine suite 3924 -> 3956 passed (1 skipped); app suite 3104 -> 3104; `--validate-rules` 294 definitions OK;
  `--rule-coverage` unchanged (Dead 36, 36 no-definition across 2 names); headless smoke exit 0 with a `Game result:`
  line. Every new test was broken on purpose and seen failing, then restored (engine mutations: the Shaken gate
  removed; aura range 8"; min Tough 6; no Shaken / no on-table gate on the aura hero; no friendly check; same-player
  instead of team-aware; nearest instead of best; Tough read as remaining wounds; per-model Tough ignored; distance
  +1"; aura switched off; joined hero used even when worse; `GetSaveDefense` switching early / never; no log line;
  chips only when otherwise modified; beat built without the source; Hero description reverted).
- 2026-10-30: findings before building. (a) Shaken is a UNIT token everywhere (`MoraleUtilities.ApplyShaken`,
  the spillout, every reader is `unit.Tokens`), so "the Hero isn't Shaken" for a joined hero is "its unit isn't
  Shaken" (D1). (b) Four morale dispatch sites, each computing its own Quality from `HeroStatRules.GetMoraleQuality`:
  `DetermineMoraleSaveNeededStage` (melee loser, #419), `MoraleUtilities.ResolveWoundDrivenMorale` (shooting / strafe
  casualties), `CastSpellStage.ResolveConditionalSpell`, `GameOperationServices.MoraleTestThen` (Mind Control, Fatigue
  Debuff, Spell Drain #429). All four now call one seam, `MoraleUtilities.ResolveMoraleQuality`. (c) No dedicated
  aura-range helper exists; the closest "friendly unit within X" precedent is the casting-support scan
  (`CastSupport.NeighboursOf`, 3D base to base via `UnitCompareUtilities.MinDistanceBetweenUnits`), which the aura
  copies (D4). (d) The #006 Defense clause was already right (D7). (e) Nothing in the AI or the Calculator estimates
  morale Quality (D9).
- 2026-10-29: filed with the plan.

## Decisions

- **D1 - the joined hero's Shaken gate collapses to "the unit isn't Shaken".** Shaken lives on `IUnit.Tokens`, never on
  a model, so `HeroStatRules.GetMoraleQuality` now lends the joined hero's Quality only while the hero is alive AND its
  unit carries no Shaken token. Because a Shaken unit always fails its Morale Tests before any Quality is read
  (`TakeMoraleTest`'s short-circuit), the gate never changes a joined unit's own pass or fail; it matters for the
  reported threshold and is the same gate the aura applies to the lending Hero.
- **D2 - "may use" is automatic: the best Quality on offer, no prompt.** `ResolveMoraleQuality` takes the lowest of the
  unit's own, its joined hero's (D1) and every projecting aura Hero's. Ties keep the unit's own, then the joined hero,
  then the nearer aura Hero, so a Hero is only named when it actually changed the number. Consequence (3.5 -> 3.6): a
  joined hero WORSE than its unit is no longer used - 3.5.1 had it take the test "on behalf of the unit"; 3.6 says the
  unit "may use" its Quality (`AJoinedHeroWorseThanItsUnit_IsNotUsed`).
- **D3 (OWNER REVIEW) - only a Hero on the table projects, and only to a unit on the table.** A Tough(9)+ Hero in
  reserve (Ambush), embarked, or pushed off the table (`OffTableFromForcedMove`) lends nothing - `IUnitExtensions.
  GetIsOnBattlefield`, the same predicate targeting uses - and a testing unit that is not on the table gets no aura
  either. The rule text measures "within 6"", which has no meaning off the table. (Embarked is moot in practice: a
  Tough(9) Hero is over the Tough(6) transport cap.) Alternative: let a Hero inside a transport project from the
  transport's hull - not built.
- **D4 (OWNER REVIEW) - the 6" is 3D base to base from the Hero's model to the testing unit's closest living model,
  inclusive, at test time.** `UnitCompareUtilities.MinDistanceFromModelToUnit(..., includeVertical: true)` - the measure
  of the casting-support "friendly units within 12"" scan and every shooting range gate; `AbilityTargeting` (activated
  abilities' picks) measures 2D instead. "Within 6"" is `<= 6`. Measured when each test is taken (the melee loser's
  Quality is resolved in `DetermineMoraleSaveNeededStage`, immediately before `RollForMoraleStage` rolls it).
- **D5 - a Hero's Tough is its Tough rule's argument.** `HeroStatRules.GetToughValue`: the higher of the unit's Tough and
  the model's own (#407 per-model grants); a joined hero has only its own (relocated onto its model at the join). Never
  remaining wounds, so a wounded Tough(9) Hero still projects. (Only Tough sets max wounds in the engine and the data,
  so "argument" and "max wounds" agree; the argument is the honest read.)
- **D6 - every Morale Test gets it, and "friendly" is team-aware.** The 3.6 sentence says "for Morale Tests" without
  qualification, so the casualty test, the melee loser's, a spell's (Terrifying Fury-shape) and a rule-forced one
  (Spell Drain, Mind Control, Fatigue Debuff) all read the seam. Friendly = `ITeamExtensions.AreAllied` (an allied
  player's Hero counts; an enemy's never). The testing unit's own Hero is "the Hero's unit" (its own Quality), so the
  aura scan skips it.
- **D7 - the Defense clause needed no change.** "must use the unit's Defense until all other models have been killed"
  is `HeroStatRules.GetSaveDefense` (#006), read by the shared `DetermineSaveRollsNeededStage` that shooting, melee,
  Impact and the synthetic paths all use; no other stage reads `unit.Defense`. Pinned now through the real save
  pipeline (`HeroDefenseClauseRuleIntegrationTests`).
- **D8 - presentation.** When a Hero lends the Quality, `TakeMoraleTest` logs "{unit} tests morale at Quality 3+ (Hero
  aura: {hero})." (or "(joined Hero: {hero})"), and the morale roll's first chip carries the same text and always shows
  (`ComposeMoraleTags`); a test at the unit's own Quality is unchanged (no extra line, chips only when modified). ASCII.
  The CLI shows the log line; the GUI's `DiceOverlay` renders chips verbatim, so no app change. `CoreRuleCatalog.Hero`'s
  description is the 3.6 text verbatim.
- **D9 - AI parity is a no-op.** No AI or Calculator code reads `GetMoraleQuality` or estimates morale Quality (the
  Tactician's `MoraleBreakBonus` is flat); its rollouts run the real stages, so they see the aura. Pricing ideas
  (discount the break bonus by the pass chance at the seam; value staying inside a friendly Hero's 6") are handed to
  #442's Notes.

## Ledger (facet -> code anchor -> test)

| Facet | Code anchor | Test |
|---|---|---|
| Joined hero lends Quality only while not Shaken (D1) | `Rules/Dispatch/HeroStatRules.cs` `GetMoraleQuality` | `HeroStatRulesTests.GetMoraleQuality_UsesUnitQuality_WhileTheJoinedHeroIsShaken`, `GetMoraleQuality_UsesHerosQuality_AgainOnceTheShakenTokenClears`, `ShakenIsAUnitToken_SoTheJoinedHeroIsShakenExactlyWhenItsUnitIs` |
| One seam for every morale test; best Quality wins (D2) | `MoraleUtilities.ResolveMoraleQuality` / `MoraleQuality` / `EMoraleQualitySource`; callers `DetermineMoraleSaveNeededStage`, `ResolveWoundDrivenMorale`, `CastSpellStage.ResolveConditionalSpell`, `GameOperationServices.MoraleTestThen` | `HeroMoraleAuraRuleIntegrationTests.AHeroNoBetterThanTheUnit_IsNotNamed`, `AJoinedHeroWorseThanItsUnit_IsNotUsed`, `JoinedHero_BeatsAWorseAura_AndAnAuraBeatsAWorseJoinedHero`, `TheHeroItself_TestsAtItsOwnQuality` |
| Tough(9)+ Hero within 6" lends its Quality (wound-driven test) | `HeroStatRules.TryGetMoraleAuraHero`, `MoraleAuraMinTough`, `MoraleAuraRangeInches`; `MoraleUtilities.BestAuraQuality` | `ToughNineHero_At5Inches_NeighbourTestsAtTheHerosQuality`, `ToughNineHero_At7Inches_GivesNothing`, `ToughSixHero_At5Inches_GivesNothing` |
| Two Heroes -> the best Quality, not the nearest | `BestAuraQuality` | `TwoHeroes_TheBestQualityWins_NotTheNearest` |
| Hero's unit Shaken / dead -> no aura | `TryGetMoraleAuraHero` | `HerosUnitShaken_GivesNothing`, `DeadHero_GivesNothing` |
| Off-table Hero -> no aura (D3) | `TryGetMoraleAuraHero` (`GetIsOnBattlefield`), `BestAuraQuality` | `HeroInReserve_GivesNothing` |
| Base-to-base 3D, inclusive, at test time (D4) | `UnitCompareUtilities.MinDistanceFromModelToUnit` | `MeasuredBaseToBase_NotCentreToCentre` (+ the 5"/7" pairs) |
| Tough = rule argument incl. per-model grants (D5) | `HeroStatRules.GetToughValue` | `WoundedToughNineHero_StillProjects_ToughIsTheRuleNotTheWoundsLeft`, `PerModelToughGrant_CountsAsTheHerosTough` |
| Friendly, team-aware (D6) | `BestAuraQuality` (`ITeamExtensions.AreAllied`) | `EnemyHero_At5Inches_GivesNothing`, `TeammatesHero_IsFriendly_AndLendsItsQuality` |
| Melee loser's test (#419) | `DetermineMoraleSaveNeededStage` -> `DetermineMoraleSaveNeededResult.Quality` -> `RollForMoraleStage` | `MeleeLoser_UsesTheAura(5, pass)`, `(7, fail)` |
| Rule-forced test: Spell Drain (#429) | `GameOperationServices.MoraleTestThen` | `SpellDrainVictim_UsesItsFriendlyHerosAura(5, 3 tokens)`, `(7, 1 token)` |
| Spell's conditional test | `CastSpellStage.ResolveConditionalSpell` | `CasterRuleIntegrationTests.CastSpellStage_ConditionalSpell_TargetUsesAFriendlyToughNineHerosQuality(5, not fatigued)`, `(7, fatigued)` |
| Defense clause (D7) | `HeroStatRules.GetSaveDefense` via `DetermineSaveRollsNeededStage` | `HeroDefenseClauseRuleIntegrationTests.WhileOtherModelsLive_TheUnitSavesAtTheUnitsDefense`, `OnceEveryOtherModelIsDead_TheHeroSavesAtItsOwnDefense` |
| Log line + roll chip name the source (D8) | `MoraleUtilities.TakeMoraleTest(.., MoraleQuality, ..)`, `MoraleQuality.Describe`, `ComposeMoraleTags(MoraleQuality, ..)` | `TheLogLineAndTheRollChip_NameTheHeroWhoseAuraWasUsed`, `AJoinedHerosLogLine_NamesTheJoinedHero`, `AUnitOnItsOwnQuality_LogsNoSourceLine`, `DiceBeatGlanceMetadataTests.MoraleTags_AHeroSourcedQuality_NamesTheHero_AndAlwaysShows` |
| Hero description = 3.6 text | `CoreRuleCatalog.Hero` | `HeroDescription_IsTheCore360Wording` |
| AI parity (D9) | none (no estimator reads morale Quality) | hand-off in #442 Notes |

## GUI hand-verify (Chris)

1. Field a Tough(9)+ Hero standing alone (a big named character or a monster Hero) within 6" of a friendly unit with a
   worse Quality, then shoot that unit to half strength (or lose a melee with it). The morale roll's panel shows the
   Hero's number as the threshold and a chip "Quality N+ (Hero aura: <Hero name>)"; the log has "<unit> tests morale at
   Quality N+ (Hero aura: <Hero name>)."
2. Same with the Hero 7"+ away, or Shaken: no chip naming it, and the unit's own Quality in the log's "needed" line.
3. A joined hero with a better Quality than its squad: the squad's morale roll shows "Quality N+ (joined Hero: <name>)".
4. A unit testing at its own Quality with no modifiers: the morale panel looks as before (no chip row).
5. The rule glossary / hover for "Hero" shows the 3.6 text.

## Outcome

Done 2026-10-30. 3.6 Hero morale is in: a joined hero lends its Quality only while not Shaken (and only when better),
and a friendly Tough(9)+ Hero on the table lends its Quality to every friendly unit within 6" for any Morale Test,
best Quality automatically, through one seam (`MoraleUtilities.ResolveMoraleQuality`) that all four morale dispatch
sites read. The Defense clause was already #006's behaviour and is now pinned through the save pipeline. The morale
log line and roll chip name the lending Hero; the Hero description is the 3.6 text. Owner review asked on D3
(off-table Heroes project nothing) and D4 (3D base-to-base, inclusive). Deferred (recorded in #442): AI pricing of
the aura (break-bonus discount, positioning). Nothing else cut.

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
