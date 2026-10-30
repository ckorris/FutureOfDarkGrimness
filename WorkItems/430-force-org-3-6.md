# 430 — Force org and points validation 3.6 (GDF + AoF levels, Unique Hero once per army)

**Status**: done 2026-10-30 (owner review asked on D1, D2, D3, D4, D6; GUI hand-verify pending)
**Related**: #415, #003 (force-org warnings), #378 (AoF game-system field), #402 (Army Source setting), #441 (Forge UI shows the warnings)
**Branch**: `feature/opr-3.6`. Model/effort: Sonnet 5.5 / medium.

## Goal

Core Rules v3.6.0 (GF): "Force Org. (optional): Players may only bring up to 1 Hero per 500pts in their army,
and up to 1+X copies of the same unit, where X is 1 per 1000pts in their army (combined units count as one).
No single unit may be worth over 35% of total points, and armies may only have max. 1 unit per 200pts, and
max. 1 model or Tough per 20pts." Suggested sizes 1000pts / 2000pts. AoF Core Rules v3.6.0: 1 Hero per
375pts, X is 1 per 750pts, suggested 750pts / 1500pts (the 35% / 200pts / 20pts clauses: read the AoF text and
record the numbers here). Book rule Unique Hero (replaces Unique): "Counts as having Hero, and this model may
only be taken once per army."

"Done" = the #003 validator (lobby + Forge amber warnings) applies the 3.6 limits per game system
(`ArmyListFile.GameSystem`, absent = GDF), adds the unit-count and model-or-Tough caps, counts Unique Hero
as a Hero, and errors on a second copy of a Unique Hero; the lobby's default points presets follow the
suggested sizes per system.

## What changes

1. Engine: force-org constants become a per-system table; new caps; `Unique Hero` -> Hero semantics in
   `HeroJoinResolver`/hero eligibility (it must be able to join a unit like any Hero) via a catalog entry
   that grants Hero (`UnitAura`-style grant or a plain AddRule) plus the once-per-army validation.
2. App: warning strings (ASCII only); lobby presets.
3. Tests: `ForceOrgValidatorTests` (or nearest) for each cap in both systems; Unique Hero join + duplicate.

## Acceptance

Tests green; Forge shows the new warnings on an over-cap list; hand-verify (Chris) optional.

## Code anchors (survey 2026-10-29)

- Force-org validator from #003 (`FutureOfDarkGrimness/ArmyBuilding/` validator), `GameSystems` class,
  lobby points preset in `FdgRaylib/Rendering/LobbyScreen.cs`. See the appended survey rows below.

### Survey rows (code survey 2026-10-29)

Findings that shape the item: today 1 Hero per 500pts and a FLAT 3 copies per unit (not 1+X per 1000pts);
the 35% cap, 1 unit/200pts and 1 model-or-Tough/20pts are not implemented; nothing anywhere knows AoF points
levels.

- `SaveLoad/ForceOrgValidator.cs` `Validate :24-62` (`POINTS_PER_HERO = 500` :15, `MAX_COPIES_PER_UNIT = 3` :18,
  cap :29, warnings :39/:53/:58-60, `IsHero` :76). `ArmyBuilding/ListValidator.cs:16` wraps it (:123; hero Tough
  cap :191). UI `FdgRaylib/Rendering/ArmyBuilderScreen.cs:279`.
- Game system: `ArmyBuilding/GameSystems.cs:8-49` (slugs, Normalize, DisplayName; enum :61-74);
  `BookModel.cs:41`, `SaveLoad/ArmyListFile.cs:32`, `GameModel/GameSettings.cs`; lobby gate
  `ArmyBuilding/LaunchGate.cs:53`.
- Tests: `ForceOrgValidatorTests.cs`, `ListValidatorTests.cs`.

## Notes

- 2026-10-30: done. One engine commit on `feature/opr-3.6`: `e1eb0cd8` (per-system table, the five caps, Unique Hero
  grant + once-per-army, tests); one superproject commit bumps the pointer with the app side (lobby Army Source ->
  suggested sizes, Forge default limit and hover, docs, ledger). Engine suite 3829 -> 3924 passed (1 skipped); app suite
  3090 -> 3104; `--validate-rules` 294 definitions OK; `--rule-coverage` unchanged (Dead 36, the two Counter-Attack
  names); headless smoke exit 0 (`Game result:` line). Every new test was broken on purpose (24 engine mutations: each
  cap's `>`/`>=`, each AoF number, the GDF copy step, the 35% constant, ignoring Tough / grants / the join fold, the
  Hero grant, the Unique Hero branch and its warning filter, the estimator; 5 app mutations: default size, the
  retarget, both hovers) and seen failing, then restored.
- 2026-10-30: 3.6 text read (core text extracts; the book snapshots for Unique Hero). **AoF numbers** (Age of Fantasy
  Core Rules v3.6.0, "Force Org. (optional)"): 1 Hero per **375**pts; up to 1+X copies of the same unit, X = 1 per
  **750**pts (combined units count as one); no single unit over **35%** of total points; max 1 unit per **150**pts;
  max 1 model or Tough per **15**pts; suggested sizes **750**pts to start, **1500**pts for a full match. Grimdark
  Future: 500 / 1000 / 35% / 200 / 20, suggested 1000 / 2000. AoF is the GDF row x 3/4 throughout except the 35%.
  Unique Hero: identical sentence in all 26 books that carry it (14 GDF, 12 AoF), no unit carries Hero alongside it,
  so a bearer is a Hero ONLY through this rule; the catalog description is that sentence verbatim.
- 2026-10-30: premise correction. The item (and the task) said the #003 amber warnings show in "lobby + Forge". They
  do not show in the lobby: `ForceOrgValidator` is called only by `ArmyBuilderScreen` (hand-authored armies) and, through
  `ListValidator`, by `ArmyForgeScreen` (the list pane's issue lines, amber for Warnings). The lobby's `LaunchGate`
  carries `BlockingProblems` (no army / over the LOBBY points / wrong system) and `OverridableProblems` (ListValidator
  ERRORS only, as a "launch anyway?" confirm). So the 3.6 caps surface where the #003 warnings always did, and the
  Unique Hero duplicate (an Error) reaches the lobby as the overridable confirm, exactly like 3.5 Unique. A lobby-side
  amber force-org line would be a new UI surface (a design fork); not built, recorded here and in #441.
- 2026-10-30: #442 hand-off not needed: the Random Army path (`BotArmyPicker`, `ArmyCatalog`) picks SAVED army files
  by points and Army Source and never builds a list, so no AI code reads these caps. (It does read the lobby budget,
  which D6 now moves with the source.)
- 2026-10-30: the pre-existing #003 warnings kept their wording prefixes ("Too many Heroes", "Too many copies of",
  "Over points limit", "no non-Hero units") because `ListValidator` and the tests key on them; the new ones are "Unit
  ... is too large", "Too many units", "Too many models/Tough", "Unique Hero ... taken N times". All ASCII.
- 2026-10-30: existing tests that relied on the flat 3 copies at `PointsLimit = 100000` (`ListValidatorTests`
  TooManyCopies / FourCombinedPairs / CombinedPairsAndPlainSquads / ValidCombinedPair) moved to a 2000 limit (3.6 cap:
  3 copies). No other existing test changed meaning.
- 2026-10-29: filed with the plan.

## Decisions

- **D1 (OWNER REVIEW) - how a model with Tough counts toward "max. 1 model or Tough per 20pts".** Each model counts
  `max(1, its Tough)`: a 10-model squad is 10, a Tough(6) monster is 6, 5 models of Tough(3) are 15. A unit-wide
  Tough(X) applies to every model; a per-model grant (#407, a Weapon Team's Tough(3)) counts for that model only, and a
  model carrying its own Tough takes the higher of its own and the unit's (as army load does), so a grant never lowers
  the unit's. The rule text does not say whether Tough replaces the model or adds to it, nor whether "Tough" means the
  per-model value or the unit's best. Alternatives: `max(models, Tough)` per unit (a 5-model Tough(3) squad = 5), or
  count Tough only on single-model units. One function (`ForceOrgValidator.ModelOrToughWeight`) to change.
- **D2 (OWNER REVIEW) - the points basis is the army's LIMIT, not its running total.** Every "per N pts" allowance and
  the 35% share read `PointsLimit` (the size of game the list is built for), falling back to `TotalPoints` only when no
  limit is set. This is how the Hero cap has always read (#003), and it keeps a half-built list from tripping the
  35% / unit-count caps with every row it adds (the first 100-pt unit of a new list is 100% of its own total). The rule
  text says "total points" / "pts in their army", read literally the actual total; the cost is that a deliberately
  under-spent list is measured against the full game size. Alternative: `max(limit, total)` or the strict total.
- **D3 (OWNER REVIEW) - a joined Hero folds into its host for the 35% and unit-count caps.** A single-model Hero within
  the join Tough cap whose `JoinsUnitId` names another multi-model, non-Hero unit in the list fights as part of that
  unit, so its cost adds to the host's and it is not a unit of its own; an ineligible join (deploys solo at game time)
  stays a unit. The Hero still counts for the Hero cap, the copy cap (as itself) and the model cap. "Combined units count
  as one" needs no fold: `ListCompiler.MergeCombinedUnits` already emits ONE `UnitFileEntry` per combined pair (models and
  points summed), and a pre-2026-07-24 file's " (Combined)" suffix is stripped for the copy grouping. Alternative: count
  every army-file entry as a unit (stricter 200pts cap, looser 35%).
- **D4 (OWNER REVIEW) - how Unique Hero "counts as having Hero".** A marker definition in `CoreRuleCatalog`
  (`UniqueHero`, in `All`, description = the 3.6 sentence) plus a one-row "counts as having" table
  (`CoreRuleCatalog.ImpliedRuleName`: Unique Hero -> Hero). `GameBootstrap.AttachRulesFromArmyList` attaches the implied
  rule through the army's resolver (deduped, so a unit naming both gets one Hero), so every engine read of Hero - the
  #006 join (`HeroJoinResolver`, same Tough(6) cap and host eligibility), `TransportUtilities.IsHeroModel`, the saved
  rule list, the resume snapshot, mid-game Spawn/Split units - sees a real Hero attachment with no new name check. The
  file-level reads (`ForceOrgValidator.IsHero` / `HasHeroRule`, which the Forge, Army Builder, calculator and
  `DefaultBaseEstimator` all go through) use the same table. Rejected: an `Effect.AddRule` at unit creation (it leaves a
  RuleGrant TOKEN, and the Hero reads are structural - the join runs before any creation hook), and an `ImpliedRules`
  field on `SpecialRuleDefinition` (a schema change on every embedded definition and every baked book for one row of
  data; the table can move onto the definition later without touching the readers). Visible consequence: a Unique
  Hero's rule list shows both "Unique Hero" and "Hero".
- **D5 - Unique Hero once per army is an Error, on the same footing as 3.5 Unique.** `ListValidator` flags each copy
  beyond the first of a roster unit carrying Unique Hero (keyed by `RosterUnitId`, message "X: Unique Hero - the army may
  only include one copy."). `LaunchGate` policy is unchanged and recorded: catalog Errors are `OverridableProblems`, a
  "launch anyway?" confirm, never `BlockingProblems`; the optional caps are Warnings and the gate never sees them (both
  pinned in `LaunchGateTests`). An army with no catalog behind it (hand-authored, imported) cannot be Errored, so
  `ForceOrgValidator` warns ("Unique Hero "X" taken 2 times (once per army)."); `ListValidator` drops that warning for
  Forge lists, which already carry the Error.
- **D6 (OWNER REVIEW) - what "the lobby's default points presets" became.** The lobby has no preset list - only a
  default (2000, Army Source All = Grimdark Future) and a +/-250 field. Built: (a) `ForceOrgLimits.Retarget` - when the
  host changes Army Source and the budget still sits on a suggested size of the old source, it follows to the matching
  size of the new one (start for start, full for full: 2000 -> 1500 on Age of Fantasy, 1500 -> 2000 back); a typed number
  is left alone; All reads as Grimdark Future. Only the live combo does this (`LobbyScreen.SetArmySource`), so a
  remembered host config (`UserConfig` sets points then source through the view model) is never rewritten. (b) The Army
  Points hover names the selected source's suggested sizes. (c) The Forge's new-list limit starts at the book system's
  starter size (GDF 1000 / AoF 750), follows a system switch the same way, and the "pts limit" box has a hover with the
  suggested sizes and the system's force-org numbers. Alternative: preset buttons in the lobby; both a UI call for Chris.
- **D7 - unknown or absent game systems read the Grimdark Future row.** Absent = GDF is the project rule (#378); a slug
  neither constant names (an OPR custom-book army) gets the GDF row too since OPR publishes no third table; the lobby's
  "All" is GDF for the same reason.
- **D8 - the Hero allowance is still `floor(basis / N)`.** A 400-pt list with a Hero warns (allowance 0), as it always
  did; the 3.6 text ("up to 1 Hero per 500pts") is read literally, not as "at least one". The copy allowance is
  `1 + floor(basis / M)` (so 2 at 1000, 3 at 2000 - the old flat 3 survives only at 2000).
- **D9 - the optional caps stay Warnings.** Force Org is optional in the rules; none of the new caps is an Error, none
  reaches `LaunchGate`, none blocks saving.

## Ledger (facet -> code anchor -> test)

| Facet | Code anchor | Test |
|---|---|---|
| Per-system table (GDF / AoF numbers, suggested sizes, unknown slug = GDF) | `ArmyBuilding/ForceOrgLimits.cs` `GrimdarkFuture` / `AgeOfFantasy` / `For` | `ForceOrgValidatorTests.ForceOrgLimits_For_AbsentAndUnknownSlugsReadAsGdf`, every `*_PerSystem` case |
| Hero cap 1 per 500 / 375 | `ForceOrgValidator.Validate` (Hero cap), `ForceOrgLimits.MaxHeroes` | `HeroCap_IsOnePerNPoints_PerSystem` (16 cases at / over, incl. the floor rows), `HeroCap_SameLimit_DiffersBySystem` |
| Copies 1+X, X per 1000 / 750; combined counts once | `Validate` (Copy cap), `ForceOrgLimits.MaxCopies`; `ListCompiler.MergeCombinedUnits` | `CopyCap_IsOnePlusXPerNPoints_PerSystem`, `CopyCap_SameLimit_DiffersBySystem`, `CopyCap_CombinedPairCountsAsOneCopy`, `ListValidatorTests.CombinedPair_IsOneCopyAndOneUnit_At36Limits` (+ the four pre-existing combined tests) |
| 35% single-unit cap | `Validate` (Unit-size cap), `ForceOrgLimits.MaxUnitPoints`, `TableUnits` | `UnitShareCap_Is35Percent_PerSystem`, `UnitShareCap_CountsAJoinedHeroTowardItsHost` |
| 1 unit per 200 / 150 | `Validate` (Unit-count cap), `ForceOrgLimits.MaxUnits` | `UnitCountCap_IsOnePerNPoints_PerSystem`, `UnitCountCap_AJoinedHeroIsNotAUnitOfItsOwn` |
| 1 model or Tough per 20 / 15 (D1) | `Validate` (Model-or-Tough cap), `ModelOrToughWeight`, `ForceOrgLimits.MaxModelsOrTough` | `ModelCap_IsOneModelPerNPoints_PerSystem`, `ModelCap_ToughCountsInPlaceOfTheModel`, `ModelCap_APerModelToughGrantCountsOnlyThatModel`, `ModelCap_AGrantNeverLowersTheUnitsOwnTough` |
| Basis = limit, else total (D2) | `ForceOrgValidator.ForcePointsBasis` | `SizeCaps_UseTheTotalWhenNoLimitIsSet` |
| Forge lists use the BOOK's system | `ListValidator.Validate` -> `ForceOrgValidator.Validate(compiled)` (`compiled.GameSystem`) | `ListValidatorTests.ForceOrgWarnings_UseTheBooksGameSystem` |
| Unique Hero catalog entry, 3.6 text | `CoreRuleCatalog.UniqueHero` / `UniqueHeroName` / `ImpliedRuleName` | `UniqueHeroRuleIntegrationTests.UniqueHero_Catalog_CarriesTheThreePointSixText`, `RuleCatalogLintTests` allowlist entry |
| Unique Hero is a Hero (army load) | `GameBootstrap.AttachRulesFromArmyList` -> `AttachImpliedRules` | `UniqueHero_HoldsExactlyOneHeroRule_EvenWhenTheEntryNamesHeroToo`, `UniqueHero_GrantsHero_ToTheBearerOnly` |
| Unique Hero joins like a Hero (same caps) | `HeroJoinResolver` (reads the attached Hero) | `UniqueHero_JoinsAMultiModelUnit_LikeAHero`, `UniqueHero_OverToughSix_DoesNotJoin_SameCapAsAHero`, `UniqueHero_CannotJoinAUnitThatAlreadyHasAHero` |
| Unique Hero is a Hero for transport | `TransportUtilities.IsHeroModel` | `SoloUniqueHero_UsesTheHeroTransportRules` |
| Unique Hero counts toward the Hero cap / IsHero everywhere | `ForceOrgValidator.IsHero` / `HasHeroRule` / `NamesHero` | `UniqueHero_CountsAsAHero_ForTheHeroCapAndIsHero`, `UniqueHero_ThroughAnAlias_StillCountsAsAHero`, `ListValidatorTests.UniqueHero_CountsTowardTheHeroCap`, `UniqueHero_CanJoin_AndTheJoinToughCapApplies` |
| Unique Hero estimated base | `DefaultBaseEstimator.Estimate` -> `HasHeroRule` | `DefaultBaseEstimatorTests.UniqueHero_CountsAsAHero_ForTheEstimatedBase` |
| Duplicate Unique Hero = Error (D5) | `ListValidator.Validate` (uniqueRule) | `ListValidatorTests.TwoCopiesOfUniqueHero_IsAnError_OnTheSecondCopy_AndNotAlsoAWarning`, `OneCopyEachOfTwoUniqueHeroes_NoUniqueIssue` |
| ... warning for catalog-less armies | `ForceOrgValidator.Validate` (Unique Hero) | `UniqueHero_TakenTwice_WarnsForArmiesWithoutACatalog` |
| LaunchGate policy: Unique Hero Error overridable; caps never gate | `LaunchGate.OverridableProblems` (unchanged) | `LaunchGateTests.DuplicateUniqueHero_IsOverridable_NeverBlocking`, `OverEveryForceOrgCap_IsOnlyAWarning_TheGateNeverSeesIt` |
| Lobby Army Source -> suggested sizes (D6) | `ForceOrgLimits.Retarget`; `LobbyScreen.SetArmySource` / `ArmyPointsTooltip` | `ForceOrgValidatorTests.ForceOrgLimits_Retarget_MapsStockSizesAndLeavesTypedOnesAlone`, `LobbyArmyPointsTests` |
| Forge default limit, system switch, hover | `ArmyForgeScreen.DefaultPointsLimit` / `SwitchBook` / `PointsLimitTooltip` | `ArmyForgeScreenTests.NewListStartsAtTheSystemsSuggestedStarterSize`, `SwitchingSystem_FollowsAStockLimit_ButKeepsATypedOne`, `PointsLimitTooltip_NamesTheSystemsSizesAndForceOrgNumbers_InAscii` |

## Forge hand-verify (Chris)

1. Open the Army Forge on a GDF book, set the "pts limit" to 1000 and add six squads: the list pane shows amber "Too many
   units: 6 (max 5 at 1000 pts; 1 per 200)." and, for the same squad taken three times, "Too many copies of "X": 3 (max 2
   at 1000 pts; 1 + 1 per 1000)." Switch to an AoF book (the limit follows 1000 -> 750): at 750 the unit cap is 5 (1 per
   150) and the copy cap 2.
2. Hover the "pts limit" box: the suggested sizes (1000 / 2000 or 750 / 1500) and the system's numbers in plain ASCII.
3. Make one unit worth over 35% of the limit: amber "Unit "X" is too large: N pts (max M at L pts; 35% of the army)."
   A Hero joined to it adds to its cost.
4. Take a unit with 20+ models: "Too many models/Tough" appears once models (or Tough, per model) pass limit / 20.
5. A duplicate Unique Hero needs a 3.6 book (none of the bundled 3.5 books carries Unique Hero until #438): until then,
   check it with a hand-edited or DemoBook-style fixture, or after the re-bake: the second copy's row goes red with "X:
   Unique Hero - the army may only include one copy.", and the Hero join control is offered on the Unique Hero's row.
6. Lobby (host): set Army Source to Age of Fantasy with Army Points at 2000 -> it becomes 1500; back to All -> 2000;
   type 1750 and switch source -> it stays 1750. Hover Army Points for the suggested sizes.

## Outcome

Done 2026-10-30. The #003 validator is now a per-game-system table (`ForceOrgLimits`: GDF 500 / 1000 / 35% / 200 / 20,
AoF 375 / 750 / 35% / 150 / 15) read through the army's `GameSystem` (absent = GDF), with the Hero, 1+X copy (combined
pairs once), 35% unit-share, unit-count and model-or-Tough caps as amber warnings; Unique Hero is a catalog entry whose
bearer carries the real Hero rule (join, transport, force-org count, base estimate) and a duplicate is an Error; the
lobby's Army Points and the Forge's limit follow each system's suggested sizes. Awaiting Chris's answers on D1-D4 and D6
and the Forge/lobby hand-verify above. Deferred / recorded: a lobby-side amber force-org line (does not exist; a design
fork, see the premise correction), the 3.5 `Unique` removal and its tests (#436), re-baked books carrying Unique Hero
(#438), the Forge rule-text/glossary pass (#441).

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
