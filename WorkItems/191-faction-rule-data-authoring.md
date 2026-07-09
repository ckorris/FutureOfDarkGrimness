# 191 — Faction rule coverage, part 1: data-only clone rules

**Status**: todo
**Related**: #192 (engine-work half of the same audit), #100 (primitive catalog), #042 (rule architecture), #087 (custom rule authoring), #166a (`RuleFireLint`), #153 (`--apply-rules` / `--validate-rules`), SYS-5 in `SpecialRulesAudit.md`

## Goal

107 of the 204 unimplemented faction rule names in the shipped `.fdgbook` corpus (1,243 of the 2,185
dead references) are **renamed clones of primitives that already dispatch end-to-end**. They do nothing
today only because nobody authored a `SpecialRuleDefinition` for them. This item closes all of them as
**data**: definitions added to `FdgRaylib/Assets/Books/GdfRuleSupplement.json`, books re-baked with
`--apply-rules`.

Done = for every rule named below, `--rule-coverage` (slice 1) reports zero dead references, the
supplement lint is green, and the engine suite is untouched and still green.

**No engine (submodule) changes are in scope for this item.** If a rule appears to need one, it belongs
in #192 — stop and move it there rather than reaching into `FutureOfDarkGrimness/`.

## Why this item is separable (and Sonnet-runnable)

- **Closed vocabulary.** Every rule here is expressible in the existing JSON kinds — no new effect,
  condition, or hook. The vocabulary is fixed and enumerated below.
- **Mechanical verification.** `--validate-rules` hard-fails on a bad definition; `RuleSupplementLintTests`
  (app-side, `FdgRaylib.Tests`) runs `RuleFireLint` over every supplement definition and fails any rule
  that provably cannot fire. A no-op cannot land silently — that is exactly the Breath Attack class
  the lint was built to catch.
- **Templates exist.** Each family below names an already-shipped rule with the same shape to copy.
- **App-side only.** `GdfRuleSupplement.json` and the `.fdgbook` files live under `FdgRaylib/Assets/Books/`.

## Guardrails

1. **Never edit the `FutureOfDarkGrimness` submodule.** Not even "just the scope field" — that is #192 slice 0.
2. **Never commit, quote, or paste text from `../GDF Armies/`.** It is copyrighted OPR material. Rule
   *names* are fine; descriptions are not. Author the `description` field in your own words.
3. **Game text is ASCII-only** (CLAUDE.md). The `description` field is user-facing: no em dashes, arrows,
   ellipses, or `<=` glyphs. Use `-`, `->`, `...`, `>=`.
4. **One family per commit.** Implement -> verify -> commit -> tick the family's checkbox here.
5. **Never commit red.** Full verification loop below must pass before each commit.
6. **Don't silently cut scope.** If a rule in a family turns out not to fit its primitive, move it to
   #192 and say so in Notes the same day.

## Authoring vocabulary (the whole of it)

Effect kinds: `statModifier rollModifier reroll addExtraHit addExtraWound movementBonus ignoreRule
addRule aura dealHits heal grantToken consumeToken triggeredMove reactivate multiplyWounds qualityFloor
ignoreWoundOnRoll setMaxWounds multiplyHits chargeImpactHits reduceImpactDicePerModel extraMeleeWoundCount
strikeFirst targetIndividualModel restrictActions rangeModifier ignoreTerrainEffects
ignoreEnemyMovementBlock ignoreCover ignoreLineOfSight deferDeployment disembark embark moraleTestThen
applyFatigue markTarget reduceArmorPenetration countAsInTerrain perHitSaveModifier`

Condition kinds: `always unitHasRule allModelsHaveThisRule targetHasRule actionTypeIs unmodifiedRollEquals
distanceGreaterThan statGreaterOrEqualTo targetMajorityHasTough tokenPresent and or not afterMoving
isMelee isCharging isNotSpell isSpell`

Hooks: see `Rules/Foundation/EHookID.cs`. Only hooks with a registered context fire; `RuleValidator`
warns on the rest, and `RuleFireLint` fails them.

Seats: `Actor` (the rule's bearer is attacking/acting) vs `Subject` (the bearer is being attacked). The
whole "when this unit is shot / takes hits" family is `Subject`.

### Traps that will bite

- **`scope` must match the attachment site.** A `Weapon`-scoped rule named in a unit's `rules` list is
  dropped at load (and vice versa). Check where the corpus actually puts each name — this is exactly the
  bug #192 slice 0 fixes for `Precise`/`Thrust`.
- **`allModelsHaveThisRule` is meaningless at `Weapon` scope** and `RuleValidator` rejects it there.
- **Argumented rules cannot be granted.** `addRule`/`aura`/`markTarget` carry a name only; `RuleEvaluator`
  screens out granted names whose effects read `Arg(0)` (LAT-1). So no aura may grant `Tough(X)`-shaped rules.
- **Granted names must resolve.** `--validate-rules` checks this, and `--apply-rules` chases granted names
  transitively into the book. Author a base rule **before** its `Boost` / `Aura` / `Buff` / `Mark` variant.
- **`perHitSaveModifier`, not `rollModifier(Save)`, for "on a 6 to hit, AP(+N)".** A whole-attack save
  modifier applies to every hit in the volley (BUG-2, the Destructive bug). Mirror core `Rending`.

## Verification loop (run before every commit)

```bash
# 1. supplement parses + validates (hard gate; hook/capability fit, granted names resolve, no dupes)
dotnet run --project FdgRaylib/FdgRaylib.csproj -- --validate-rules FdgRaylib/Assets/Books/GdfRuleSupplement.json

# 2. every supplement rule provably fires (RuleFireLint over each definition)
dotnet test FdgRaylib.Tests/FdgRaylib.Tests.csproj

# 3. engine untouched and green
dotnet test FutureOfDarkGrimness/FutureOfDarkGrimness.csproj

# 4. re-bake every book (Apply embeds only the definitions that book references; idempotent)
for b in FdgRaylib/Assets/Books/*.fdgbook; do
  dotnet run --project FdgRaylib/FdgRaylib.csproj -- --apply-rules "$b" FdgRaylib/Assets/Books/GdfRuleSupplement.json
done

# 5. full build + headless smoke on a book-derived army (exit 0, expected log line)
dotnet build
dotnet run --project FdgRaylib/FdgRaylib.csproj -- --book-to-army FdgRaylib/Assets/Books/AlienHives.fdgbook /tmp/x.fdgarmy
printf "2\n2\n" | dotnet run --project FdgRaylib/FdgRaylib.csproj -- --headless --army /tmp/x.fdgarmy

# 6. coverage regressed to zero for this family (slice 1 tool)
dotnet run --project FdgRaylib/FdgRaylib.csproj -- --rule-coverage FdgRaylib/Assets/Books
```

A family is done when step 6 no longer lists any of its rule names, and steps 1-5 are clean.

## Slices

### Slice 1 — `--rule-coverage` reporting flag (do this first; app-side)

The measurement loop for everything below, and the SYS-5 "reconciliation report" the audit asked for.
Add a `--rule-coverage <booksDir>` flag to `FdgRaylib/Program.cs` that, for each book: builds
`CoreRuleCatalog.CreateResolver()`, `RegisterOrReplace`s the book's embedded `ruleDefinitions`, then runs
`ArmyListRuleResolution.ResolveForScope` over every rule reference at its real attachment scope
(unit `rules`, `weapons[].specialRules`, `items[].rules`, and the same three inside `sections[].options[]`
as `rulesGained` / `weaponsGained` / `itemsGained`). Print a table of unresolved names with reference
counts and the failure class (no definition / scope mismatch), plus a corpus total.

Baseline it must reproduce on today's data (**post-#192 slice 0**, shipped 2026-07-09): **13,870 references;
2,197 dead (2,185 no-definition across 204 names, 12 scope-mismatch — all `Strafing`, deferred to its own
#192 slice).** If your numbers differ, the walker is missing a site.

Note the walker must treat a `Weapon`-scoped rule named at unit level as **attaching**, not as a mismatch:
wargear flattens into the unit's rule list, and slice 0 re-homes those rules onto the unit's weapons. Only
a `Unit`-scoped rule named on a weapon is dropped now. `FdgRaylib.Tests/BookRuleScopeTests.cs` encodes the
same walk and is the cheaper thing to copy.

### Slices 2-16 — one per family

Order matters only within a family (base rule before its `Boost`/`Aura`/`Buff`/`Mark` variants).
Families are independent of each other; F1 is the single biggest win.

| # | Family | Refs | Reuses / copy from | Rules |
|---|--------|-----:|--------------------|-------|
| F1 | vs-Tough/Defense weapons | 232 | `targetMajorityHasTough`, `statGreaterOrEqualTo`, `ignoreRule` (all live; #100 pt.3) | Shatter, Tear, Disintegrate, Melee Slayer, Melee Slayer Aura, Ranged Slayer Aura, Slayer Mark, Ignores Regeneration, Ignores Regeneration in Melee |
| F4 | defensive distance rules | 172 | core `Fortified` / `Melee Shrouding` + `distanceGreaterThan` | Changebound, Primeborn, Sturdy, Guardian, Machine-Fog, Changebound Boost, Changebound Boost Aura, Guardian Boost, Guardian Boost Aura, Sturdy Boost Aura, Machine-Fog Boost Aura |
| F2 | ignore-wound | 143 | core `Regeneration` / `Resistance` (`ignoreWoundOnRoll`, `isSpell`) | Plaguebound, Knightborn, Self-Repair, Plaguebound Boost, Plaguebound Boost Aura, Self-Repair Boost Buff, Regeneration Buff |
| F3 | extra-attack on unmodified 6 | 118 | supplement `Predator Fighter` | Bloodborn, Clan Warrior, Primal, Primal Boost Buff, Predator Shooter Aura, Clan Warrior Boost Aura |
| F7 | movement bonus | 92 | supplement `Highborn` / `Highborn Boost` / `Highborn Boost Aura` (exact template incl. the Aura chain) | Lustbound, Scurry, Swift, Lustbound Boost, Lustbound Boost Aura, Scurry Boost Aura, Swift Aura, Swift Buff |
| F5 | extra wound on unmodified 1 to block | 83 | core `Shred` (`addExtraWound`) | Warbound, Infected, Warbound Boost, Warbound Boost Aura, Infected Boost Aura, Shred Mark |
| F6 | extra hit on unmodified 6 | 80 | core `Furious` / `Surge` (`addExtraHit`) | Ferocious, Devout, Point-Blank Surge, Devout Boost, Ferocious Boost, Devout Boost Aura, Ferocious Boost Aura, Point-Blank Piercing Aura, Surge when Shooting, Brutal Fighter |
| F8 | offensive conditional modifiers | 63 | core `Good Shot` + `distanceGreaterThan` / `isCharging` | Havocbound, Targeting Visor, Bad Shot, Havocbound Boost Aura, Targeting Visor Boost Aura |
| F12 | Mark family | 58 | `markTarget` (built, #100 pt.14a) | Unstoppable Mark, Rending Mark, Bane Mark, Furious Mark, Relentless Mark, Precision Fighting Mark, Piercing Shooting Mark, Precision Shooting Mark |
| F16 | **BLOCKED** wargear names | 48 | -- | Banner, Sergeant, Musician, Armor |
| F10 | reroll sinks | 41 | core rules using `reroll` (`RerollSink`) | Mischievous, Scrapper, Mischievous Boost Aura, Scrapper Boost Aura |
| F9 | morale +1 | 33 | supplement `Hive Bond` / `Hive Bond Boost` | Hold the Line, Hold the Line Boost Aura, Courage Buff |
| F14 | aura wrappers of live rules | 30 | `aura` + `CoreRuleCatalog`'s aura factory | Rending when Shooting Aura, Precision Fighter Aura, Precision Shooter Aura, Piercing Fighter Aura, Piercing Shooter Aura, Thrust in Melee Aura, Precision Charge Aura, Strider Aura, Increased Shooting Range Mark |
| F11 | ignore cover / LoS | 25 | core `Indirect` (`ignoreCover`, `ignoreLineOfSight`) | Ignores Cover, Ignores Cover Aura, Ignores Cover when Shooting, Ignores Cover when Shooting Aura, Indirect Mark, Indirect when Shooting Aura |
| F13 | Buff family | 15 | core `Furious Buff` (pre-attack cross-unit grant, `FirstTrigger`) | Rapid Advance Buff, Entrenched Buff, Precision Shooter Buff, Bane in Melee Buff, Guarded Buff, Precision Fighter Buff, Precision Attacks Buff, Increased Shooting Range Buff |
| F15 | already-built one-offs | 10 | `triggeredMove` caster-directed (#18), `applyFatigue` (#9), `Shooting_OnUnitDestroyed` (now fires, audit 1a.9) | Mind Control, Fatigue Debuff, Vengeance |

**F16 is blocked, not deferred silently.** `Banner`, `Sergeant`, `Musician`, `Armor` are wargear names
the OPR importer emitted as rule names; they are absent from the rules-page extract, so their mechanics
are unknown. Do not guess. Ask before authoring; if they turn out to be pure list-building flavor, they
should follow `Unique`'s precedent (a registered zero-hook definition, enforced elsewhere or nowhere).

### Notes on the F14 aura wrappers

Several aura names grant a base rule whose canonical spelling may differ (the resolver is
case-insensitive but not fuzzy: "Bane when Shooting Aura" grants "Bane when shooting"). If a granted
name has no definition anywhere, `--validate-rules` fails — that is the intended signal. Two of these
(`Precision Fighter Aura`, `Piercing Fighter Aura`) may need their base rule authored first; if the base
is itself absent from both catalog and this item's families, it is engine work -> #192.

## Notes

- 2026-07-09: #192 slice 0 shipped, moving 145 refs from dead to attaching. The dead total is now **2,197**
  (2,185 no-definition + 12 `Strafing`). The 107 names this item authors are unchanged — slice 0 only
  touched rules that already had definitions.
- 2026-07-09: Filed. Scope derived from a full-corpus resolution run: the engine's own
  `ArmyListRuleResolution.ResolveForScope` driven over all 44 books with `CoreRuleCatalog` + each book's
  embedded definitions. 13,870 references; 10,780 attach; 748 implemented outside the hook system
  (`Hero`, `Transport`, `Limited`, `Unique`); 2,342 dead. Of the 204 dead names, these 107 need no
  engine work.

## Decisions

- **Data, not aliases.** `RuleResolver.RegisterAlias` exists and is unused in production. Several of
  these rules are byte-identical in effect (Ferocious/Devout; Mischievous/Scrapper; Plaguebound/Self-Repair),
  so aliasing is tempting. Rejected: aliases share a `SpecialRuleDefinition` *instance*, and `ignoreRule`
  compares by reference — so suppressing one would suppress its twin across factions. Author separate
  definitions; they also carry distinct `description` text for the UI.
- **Coverage tool first.** Slice 1 exists so "done" is measured, not asserted. It doubles as SYS-5's
  import reconciliation report.

## Outcome

_(written when the item closes)_
