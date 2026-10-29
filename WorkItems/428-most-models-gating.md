# 428 — "Most models have this rule" replaces every "all models" gate

**Status**: todo
**Related**: #415, #093 (model-scope conditions), #303 (Instinctive vs joined hero - this item resolves it), #175 (Fear/Fearless gating), #183, #421, #432-#435 (consumers)
**Branch**: `feature/opr-3.6`. Model/effort: Opus 5.5 / medium. Do this EARLY: #421, #425, #432-#435 build on it.

## Goal

3.6 rewrites every unit-wide gate as "a unit where most models have this rule" (core: Fearless,
Regeneration, Stealth, Artillery; patch notes: "Rules that apply to the whole unit now require most models to
have them, not all"; faction: Battleborn, Honor Code, Changebound, Machine-Fog, Guarded, Guardian, Hive Bond,
Hold the Line, Knightborn, Fortified, Evasive, Melee Evasion, Defensive Frenzy/Growth, Fortified Growth,
Entrenched, Dodge, Spell Resistance, Warding, Screened, Sturdy, Darkborn, Shadowborn, Shrouded, Wild Veil,
Valiant, Grounded Defense/Evasion/Precision, No Retreat (already "most"), ...). Two faction rules keep "all":
Inquisitorial Agent and Martial Prowess ("if all models in this unit have this rule").

"Done" = the engine has one `MostModelsHaveThisRule` condition (strictly more than half of the unit's living
models carry the rule, counting a joined hero as a model of the unit - so a 10-model unit with a hero lacking
the rule still qualifies, which is the point of the change and closes #303), every core and supplement
definition that used `AllModelsHaveThisRule` is switched except the two above, and the JSON condition kind
is renamed or aliased so old embedded army files (3.5 `.fdgarmy`) still parse (keep the old kind as an alias
that maps to the old semantics? No - plan rule 6: old files load, but they get 3.6 semantics; keep the old
kind name parsing to the new condition and say so in the diagnostics).

## What changes

1. Engine: `Condition.MostModelsHaveThisRule` (+ JSON kind `mostModelsHaveThisRule`), `EModelRuleScope`
   update, `RuleValidator` accepting both kinds; catalog sweep; supplement sweep (`GdfRuleSupplement.json`,
   `AofRuleSupplement.json`, overrides) via a scripted replace, reviewed by hand for the two "all" holdouts.
2. Tests: 5-of-9 qualifies, 4-of-9 does not, hero-without-rule in a 10-model unit qualifies, casualties shift
   the count mid-game; Fearless/Regeneration/Stealth integration tests re-pinned; the #303 scenario.
3. Docs: `docs/EngineNotes.md` rule-gating paragraph.

## Acceptance

Tests green; `--validate-rules` on all supplements OK; `--rule-coverage` unchanged; `--trace-rules` shows the
new condition name.

## Code anchors (survey 2026-10-29)

- `Rules/Dispatch/EModelRuleScope.cs`, `Condition` (`Rules/Definitions/`), `RuleValidator.cs`,
  `CoreRuleCatalog` entries for Fearless/Regeneration/Stealth/Artillery. See the appended survey rows below.

### Survey rows (code survey 2026-10-29)

- `Rules/Definitions/Condition.cs:91-140` `AllModelsHaveThisRule` (every living model; a static unit rule counts
  for native models but not a joined hero; granted rules count for all). `Rules/Dispatch/EModelRuleScope.cs:21-25`
  (AnyOwner/AllOwners) is a separate pooled-weapon mechanism (`RollToHitStage.cs:97`, `DetermineHitRollStage.cs:66`,
  `ShootingForecast.cs:96`, `RuleEvaluator.cs:447/466`, `HeroStatRules.cs:84`, `CombatMath.cs:658`).
- Validator REQUIRES the gate on Subject hooks: `RuleValidator.cs:26-40`, `CheckAllModelsGate ~:124`,
  `CheckUnitWideSelfEffectGate ~:146`, `GatesOnAllModels :177-181` - teach it the new condition.
- Catalog entries on the gate (`CoreRuleCatalog.cs`): Stealth :98, Artillery :122, Evasive :184, Melee Evasion :202,
  Counter-Attack :628, Fearless :671, Regeneration :721, Resistance :1106/:1111, Protected :1125, Shielded :1182,
  Fortified :1203, Teleport :1506, Vanguard :1527, Fanatic :1549, Martial Prowess :1851 (KEEPS "all"),
  Aircraft :2022/:2027, Ranged Shrouding :2082, Darkborn :2130/:2135, Melee Shrouding :2158.
- Data: ~50 uses in `GdfRuleSupplement.json`, 35 in `AofRuleSupplement.json`, 228 across 66 `.fdgbook` files
  (the re-bake #438 regenerates the books).
- Tests: `AllModelsRuleGateIntegrationTests.cs`, `FearlessRuleIntegrationTests.cs`, `WoundIgnoreRuleIntegrationTests.cs`,
  `RuleValidatorTests.cs`, `UnitWideAbilityGateTests.cs`, `HeroSubjectRuleIntegrationTests.cs:134`,
  `RuleTraceTests.cs:75`, `VersatileDefenseRuleIntegrationTests.cs`, `DashRuleIntegrationTests.cs`.

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
