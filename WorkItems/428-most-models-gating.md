# 428 — "Most models have this rule" replaces every "all models" gate

**Status**: done 2026-10-29 (engine `ae95d03d`; superproject commit `428: ...` bumps it with the data/doc sweep). Owner review asked on Decisions D2 and D3.
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

- 2026-10-29: **Landed.**
  - **Engine.** `Condition.MostModelsHaveThisRule` already existed (#197 P7 No Retreat) with exactly the
    semantics this item asks for (strict majority of living models; a joined hero counts as a model but never
    inherits the host's static rules; unit-held grants count for everyone), so it was reused, not rebuilt. The
    counting moved into a shared `CountRuleCarriers` helper. `AllModelsHaveThisRule` is gone, replaced by
    `EveryModelHasThisRule` (JSON `everyModelHasThisRule`) for the two 3.6 holdouts, and by
    `LegacyAllModelsHaveThisRule` (JSON `allModelsHaveThisRule`, a subtype of the most-models gate) so old data
    parses with 3.6 semantics (D1). A unit with no living models is counted as it stood (D2).
  - **Catalog.** 21 gate sites switched to most-models: Stealth, Artillery (Subject facet), Evasive, Melee
    Evasion, Counter-Attack, Fearless, Regeneration, Resistance x2, Protected, Shielded, Fortified, Teleport,
    Vanguard, Fanatic, Aircraft x2, Ranged Shrouding, Darkborn (Defensive) x2, Melee Shrouding. Martial Prowess
    -> `EveryModelHasThisRule`. Only rule-gate code and comments changed; no effects touched (Artillery's
    3.6 numbers are still #425's).
  - **Data.** Scripted replace of the kind token, Inquisitorial Agent by hand: `GdfRuleSupplement.json`
    49 -> most + Inquisitorial Agent -> every; `AofRuleSupplement.json` 35; `AofBookOverrides/Chivalrous
    Kingdoms.json` + `Dark Elves.json` 1 each (Melee Shrouding). Verified structurally (no legacy kind left;
    Inquisitorial Agent is the only strict gate). `.fdgbook` files NOT re-baked (#438).
  - **Validator.** `Validate` (army-load gate) accepts `MostModelsHaveThisRule` (legacy subtype included) or
    `EveryModelHasThisRule` as the ownership gate on Subject-seat defensive entries; `ValidateAuthoring` the
    same for whole-unit reposition/reactivation, plus a new refusal of the legacy kind in newly authored data
    (D4). Negative control: HEAD's pre-sweep GDF supplement now reports 50 problems under `--validate-rules`.
  - **`EModelRuleScope`**: no change. The survey row was right that AnyOwner/AllOwners is the separate
    pooled-weapon mechanism; nothing there reads the ownership gate.
  - **Tests.** Engine: `AllModelsRuleGateIntegrationTests` -> `MostModelsRuleGateIntegrationTests` (5-of-9
    qualifies, 4-of-9 and 2-of-4 do not, casualties shift the count both ways, 10-model unit + joined hero
    lacking it qualifies, hero-only copy is a minority, destroyed unit counted as it stood, strict gate fails
    10 of 11, legacy kind answers like most, the #303 Instinctive hero-join scenario, six effect classes in
    both directions). Fearless (5/9, 4/9, casualties, 10 + hero) and Regeneration (5/9, 4/9, 10 + hero through
    the real AssignWoundsStage) re-pinned; Stealth pinned in the gate fixture. Re-pinned flips:
    `HeroSubjectRuleIntegrationTests` (host Shielded + hero lacking now applies; new down-to-one-grunt case),
    `TeleportRuleIntegrationTests` (same pair), `CompelClosestTargetRuleIntegrationTests` (2 of 3 compels, 1 of 3
    frees), `RuleTraceTests` + the HeroSubject trace test (new condition name), `RuleValidatorTests` and
    `UnitWideAbilityGateTests` (new gates + legacy accepted at load / refused at authoring),
    `ConditionSerializationTests` (three kinds; legacy parses nested and round-trips). App: 14 shipped-data
    tests now pin the exact type (`Is.TypeOf`, so a leftover legacy kind can't pass), plus new
    `MostModelsGateShippedDataTests` (no legacy kind in any supplement/override; strict gate only on
    Inquisitorial Agent and Martial Prowess; the merged supplements pass `ValidateAll`).
  - **Counts.** Engine 3390 -> 3417 passed (1 skipped before and after). FdgRaylib.Tests 3059 -> 3063.
  - **Verification.** Full `dotnet build` 0 warnings / 0 errors; headless smoke exit 0 ("Game result:
    outcome=Tie ... rounds=4"); `--validate-rules GdfRuleSupplement.json AofRuleSupplement.json` "OK: 334
    definitions, no problems." (each override added on top also OK); `--rule-coverage FdgRaylib/Assets/Books`
    "Total references: 22079 / Dead: 0"; `--trace-rules` smoke shows "Warriors's Stealth at
    Shooting_OnHitRollModifier/Subject: condition And(DistanceGreaterThan, MostModelsHaveThisRule) not met".
    Old-file check: `armies/GDF/3k - Robot Legions.fdgarmy` (11 embedded legacy kinds) played headless to exit
    0, and its embedded Self-Repair fired through the routed gate.
  - **Deferred, explicitly.** (1) Rule DESCRIPTIONS still say "all models" in 12 supplement definitions
    (text parity is #436's; recorded there). (2) The bundled `.fdgbook` files still carry the legacy kind, so
    they get most-models semantics at runtime already, including Inquisitorial Agent inside
    `HumanInquisition.fdgbook` until #438 re-bakes it (recorded there). (3) Test comments that QUOTE 3.5 rule
    text ("a unit where all models have this rule") were left as quotes; their 3.6 rewording rides #434-#436.
    (4) #303 is resolved in substance (the Instinctive hero-join scenario is pinned here), but closing it is
    the owner's call, so it stays open with a note. #304's warning premise mostly goes away with it.
    (5) Hive Bond and Hold the Line, both named in this item's Goal, were never on the all-models gate (they
    are ungated at `Morale_OnPreMoraleTest`), so the switch did not reach them. The pre-morale evaluation
    passes no models, so there is no hero leak; the only divergence is a host down to one model beside a
    joined hero lacking the rule. Handed to #434 with the details. The Goal's other names not found in the
    supplements (Dodge, Spell Resistance, Warding, Shrouded, Valiant, Grounded Defense/Evasion) are new or
    renamed 3.6 rules for #434/#435; Fortified, Evasive, Melee Evasion, Darkborn and Artillery are catalog
    entries and were switched here.
- 2026-10-29: filed with the plan.

## Decisions

- **D1 - names and the legacy alias (2026-10-29).** The old JSON kind `allModelsHaveThisRule` had to keep
  parsing AND mean most-models (plan rule 6), which leaves the two 3.6 "all models" rules needing a different
  kind. The strict gate is `EveryModelHasThisRule` / `everyModelHasThisRule`, and the C# name
  `AllModelsHaveThisRule` is retired, so no type is named "all" while meaning "most". System.Text.Json can't
  register one type under two discriminators, so the alias is a sealed subtype, `LegacyAllModelsHaveThisRule :
  MostModelsHaveThisRule` (hence `MostModelsHaveThisRule` is no longer sealed). That keeps every
  `is MostModelsHaveThisRule` check (validator included) working, the kind round-trips unchanged when an old
  file is re-saved, `--trace-rules` names it (`LegacyAllModelsHaveThisRule`) so a trace shows which
  definitions are still on the old kind, and the authoring validator can refuse it.
- **D2 - a unit with no living models (2026-10-29) - OWNER REVIEW.** The item says "more than half of the
  unit's living models", but Reinforcement's destroyed arm ("when a unit where most models have this rule is
  ... fully destroyed") is evaluated on a wiped-out unit. Options: (a) zero living answers false - kills that
  arm, a regression pinned by `ReinforcementRuleIntegrationTests.FullyDestroyed_AlsoTriggers_EvenKillerless`;
  (b) vacuously true, the old all-models behaviour - lets a destroyed unit whose only carrier was the joined
  hero use the hero's Reinforcement; (c) count the unit as it stood (all its models, dead included). **Chose
  (c)**: it is the reading of the text (the models that just died are the unit described) and the only one
  that is neither a regression nor a leak. No Retreat (the other most-models user) only fires on morale, so it
  is unaffected. One line in `Condition.CountRuleCarriers` if overruled.
- **D3 - old files and the two holdouts (2026-10-29) - OWNER REVIEW.** The legacy kind can't tell
  Inquisitorial Agent from the rules that moved to "most", so an old army file that embeds Inquisitorial
  Agent under `allModelsHaveThisRule` gets "most" for it (and so does the bundled `HumanInquisition.fdgbook`
  until #438 re-bakes it from the swept supplement). Martial Prowess is catalog-only, so it is unaffected. The
  difference only shows for a multi-model Inquisitorial Agent unit with a joined hero lacking the rule.
  Alternatives: a name-keyed remap at army load (special-cases one rule name in the loader), or a rewrite in
  #440's legacy-load pass. **Recommendation: accept** - the book side fixes itself at #438, and the
  old-army edge is negligible. Revisit in #440 if the owner disagrees.
- **D4 - authoring refusal of the legacy kind (2026-10-29).** A small addition beyond the item text:
  `ValidateAuthoring` (run by `--validate-rules` and the bake) now rejects `allModelsHaveThisRule`, so new data
  must say `mostModelsHaveThisRule` or `everyModelHasThisRule`. The army-load gate (`Validate`) still accepts
  it. That is what makes the supplement sweep self-checking.
- **D5 - descriptions untouched (2026-10-29).** Twelve supplement descriptions still read "all models". Their
  3.6 wording is #436's text-parity job, and nothing on this branch ships before the plan is green, so they
  were left alone rather than half-reworded here. Recorded in #436.

## Outcome

Done 2026-10-29. One most-models gate (`Condition.MostModelsHaveThisRule`) now governs every unit-wide rule in
the core catalog and the GDF/AoF supplements and overrides. Inquisitorial Agent and Martial Prowess keep the
strict `EveryModelHasThisRule`, and the pre-3.6 `allModelsHaveThisRule` kind loads as the most-models gate for
old files. A joined hero is one model of the unit, so it no longer switches off a rule the rest of the unit
has (#303's scenario). Engine `ae95d03d`. Open for the owner: D2 and D3.

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
