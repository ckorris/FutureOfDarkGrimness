# 196 — Faction rule coverage, part 1: data-only clone rules

**Status**: done (F16 excepted — blocked on owner input; see F16 row and Outcome)
**Related**: #197 (engine-work half of the same audit), #100 (primitive catalog), #042 (rule architecture), #087 (custom rule authoring), #166a (`RuleFireLint`), #153 (`--apply-rules` / `--validate-rules`), SYS-5 in `SpecialRulesAudit.md`

## Goal

107 of the 204 unimplemented faction rule names in the shipped `.fdgbook` corpus (1,243 of the 2,185
dead references) are **renamed clones of primitives that already dispatch end-to-end**. They do nothing
today only because nobody authored a `SpecialRuleDefinition` for them. This item closes all of them as
**data**: definitions added to `FdgRaylib/Assets/Books/GdfRuleSupplement.json`, books re-baked with
`--apply-rules`.

Done = for every rule named below, `--rule-coverage` (slice 1) reports zero dead references, the
supplement lint is green, and the engine suite is untouched and still green.

**No engine (submodule) changes are in scope for this item.** If a rule appears to need one, it belongs
in #197 — stop and move it there rather than reaching into `FutureOfDarkGrimness/`.

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

1. **Never edit the `FutureOfDarkGrimness` submodule.** Not even "just the scope field" — that is #197 slice 0.
2. **Never commit, quote, or paste text from `../GDF Armies/`.** It is copyrighted OPR material. Rule
   *names* are fine; descriptions are not. Author the `description` field in your own words.
3. **Game text is ASCII-only** (CLAUDE.md). The `description` field is user-facing: no em dashes, arrows,
   ellipses, or `<=` glyphs. Use `-`, `->`, `...`, `>=`.
4. **One family per commit.** Implement -> verify -> commit -> tick the family's checkbox here.
5. **Never commit red.** Full verification loop below must pass before each commit.
6. **Don't silently cut scope.** If a rule in a family turns out not to fit its primitive, move it to
   #197 and say so in Notes the same day.

## Authoring vocabulary (the whole of it)

Effect kinds: `statModifier rollModifier reroll addExtraHit addExtraWound movementBonus ignoreRule
addRule aura dealHits heal grantToken consumeToken triggeredMove reactivate multiplyWounds qualityFloor
ignoreWoundOnRoll setMaxWounds multiplyHits chargeImpactHits reduceImpactDicePerModel extraMeleeWoundCount
strikeFirst targetIndividualModel restrictActions rangeModifier ignoreTerrainEffects
ignoreEnemyMovementBlock ignoreCover ignoreLineOfSight deferDeployment disembark embark moraleTestThen
applyFatigue markTarget reduceArmorPenetration countAsInTerrain perHitSaveModifier`

Condition kinds: `always unitHasRule allModelsHaveThisRule targetHasRule actionTypeIs unmodifiedRollEquals
distanceGreaterThan attackedFromOverInches statGreaterOrEqualTo targetMajorityHasTough tokenPresent and or
not afterMoving isMelee isCharging isNotSpell isSpell`
(`attackedFromOverInches` added by #197: the distance the attack was *launched* from — the live distance when
shooting, the charge's declared distance in melee. Use it for every "shot or charged from over N inches away".)

Hooks: see `Rules/Foundation/EHookID.cs`. Only hooks with a registered context fire; `RuleValidator`
warns on the rest, and `RuleFireLint` fails them.

Seats: `Actor` (the rule's bearer is attacking/acting) vs `Subject` (the bearer is being attacked). The
whole "when this unit is shot / takes hits" family is `Subject`.

### Traps that will bite

- **`scope` must match the attachment site.** A `Weapon`-scoped rule named in a unit's `rules` list is
  dropped at load (and vice versa). Check where the corpus actually puts each name — this is exactly the
  bug #197 slice 0 fixes for `Precise`/`Thrust`.
- **`allModelsHaveThisRule` is meaningless at `Weapon` scope** and `RuleValidator` rejects it there.
- **Argumented rules cannot be granted.** `addRule`/`aura`/`markTarget` carry a name only; `RuleEvaluator`
  screens out granted names whose effects read `Arg(0)` (LAT-1). So no aura may grant `Tough(X)`-shaped rules.
- **Granted names must resolve.** `--validate-rules` checks this, and `--apply-rules` chases granted names
  transitively into the book. Author a base rule **before** its `Boost` / `Aura` / `Buff` / `Mark` variant.
- **`perHitSaveModifier`, not `rollModifier(Save)`, for "on a 6 to hit, AP(+N)".** A whole-attack save
  modifier applies to every hit in the volley (BUG-2, the Destructive bug). Mirror core `Rending`.
- **A `Boost` is the INCREMENT, not the boosted rule.** (Learned the hard way — see Notes.) Base and Boost
  both attach, and the sinks *add*. So a Boost that widens "extra hit on a 6" to "5-6" must emit the 5 only;
  one that raises "+1\"/+3\"" to "+2\"/+6\"" must emit "+1\"/+3\""; one that removes a `>9"` gate must fire
  only *inside* 9". Exception: `ignoreWoundOnRoll` folds via `WoundIgnoreSink`, which keeps the **best**
  (lowest) threshold rather than adding — so those Boosts state the boosted threshold directly.
- **Check where an effect is CONSUMED, not just that the hook accepts it.** `RuleValidator` and `RuleFireLint`
  both pass a `rollModifier(Hit)` emitted at `Shooting_OnHitRollComplete`, and it does nothing: the dice are
  already rolled, and only `Save` deltas fold from that hook. Hit modifiers belong at
  `Shooting_OnHitRollModifier` (copy core `Stealth`). The lint's blind spot here is a filed #197 slice.
- **`distanceGreaterThan` never passes in melee** — base contact is `<= 2"`. For "shot or charged from over
  9\" away", use `attackedFromOverInches` (#197), which reads the charge's launch distance in melee.

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

Baseline it must reproduce on today's data (**post-#197 slice 0**, shipped 2026-07-09): **13,870 references;
2,197 dead (2,185 no-definition across 204 names, 12 scope-mismatch — all `Strafing`, deferred to its own
#197 slice).** If your numbers differ, the walker is missing a site.

Note the walker must treat a `Weapon`-scoped rule named at unit level as **attaching**, not as a mismatch:
wargear flattens into the unit's rule list, and slice 0 re-homes those rules onto the unit's weapons. Only
a `Unit`-scoped rule named on a weapon is dropped now. `FdgRaylib.Tests/BookRuleScopeTests.cs` encodes the
same walk and is the cheaper thing to copy.

### Slices 2-16 — one per family

Order matters only within a family (base rule before its `Boost`/`Aura`/`Buff`/`Mark` variants).
Families are independent of each other; F1 is the single biggest win.

| # | Family | Refs | Status | Reuses / copy from | Rules |
|---|--------|-----:|--------|--------------------|-------|
| F1 | vs-Tough/Defense weapons | 232 | **DONE** (232/232) | `targetMajorityHasTough`, `statGreaterOrEqualTo`, `ignoreRule` (all live; #100 pt.3) | Shatter, Tear, Disintegrate, Melee Slayer, Melee Slayer Aura, Ranged Slayer Aura, Slayer Mark, Ignores Regeneration, Ignores Regeneration in Melee |
| F4 | defensive distance rules | 172 | **DONE** (172/172) | core `Fortified` / `Melee Shrouding` + `distanceGreaterThan` | Changebound, Primeborn, Sturdy, Guardian, Machine-Fog, Changebound Boost, Changebound Boost Aura, Guardian Boost, Guardian Boost Aura, Sturdy Boost Aura, Machine-Fog Boost Aura |
| F2 | ignore-wound | 143 | **DONE** (143/143) | core `Regeneration` / `Resistance` (`ignoreWoundOnRoll`, `isSpell`) | Plaguebound, Knightborn, Self-Repair, Plaguebound Boost, Plaguebound Boost Aura, Self-Repair Boost Buff, Regeneration Buff |
| F3 | extra-attack on unmodified 6 | 118 | **DONE** (118/118) | supplement `Predator Fighter` | Bloodborn, Clan Warrior, Primal, Primal Boost Buff, Predator Shooter Aura, Clan Warrior Boost Aura |
| F7 | movement bonus | 92 | **DONE** (92/92) | supplement `Highborn` / `Highborn Boost` / `Highborn Boost Aura` (exact template incl. the Aura chain) | Lustbound, Scurry, Swift, Lustbound Boost, Lustbound Boost Aura, Scurry Boost Aura, Swift Aura, Swift Buff |
| F5 | extra wound on unmodified 1 to block | 83 | **PARTIAL** (73/83, 10 -> #197) | core `Shred` (`addExtraWound`) | Warbound, Infected, Warbound Boost, Warbound Boost Aura, Infected Boost Aura, Shred Mark |
| F6 | extra hit on unmodified 6 | 80 | **DONE** (80/80) | core `Furious` / `Surge` (`addExtraHit`) | Ferocious, Devout, Point-Blank Surge, Devout Boost, Ferocious Boost, Devout Boost Aura, Ferocious Boost Aura, Point-Blank Piercing Aura, Surge when Shooting, Brutal Fighter |
| F8 | offensive conditional modifiers | 63 | **DONE** (63/63) | core `Good Shot` + `distanceGreaterThan` / `isCharging` | Havocbound, Targeting Visor, Bad Shot, Havocbound Boost Aura, Targeting Visor Boost Aura |
| F12 | Mark family | 58 | **DONE** (58/58) | `markTarget` (built, #100 pt.14a) | Unstoppable Mark, Rending Mark, Bane Mark, Furious Mark, Relentless Mark, Precision Fighting Mark, Piercing Shooting Mark, Precision Shooting Mark |
| F16 | **BLOCKED** wargear names | 48 | **BLOCKED** (0/48) - needs owner input | -- | Banner, Sergeant, Musician, Armor |
| F10 | reroll sinks | 41 | **PARTIAL** (35/41, 6 -> #197) | core rules using `reroll` (`RerollSink`) | Mischievous, Scrapper, Mischievous Boost Aura, Scrapper Boost Aura |
| F9 | morale +1 | 33 | **DONE** (33/33) | supplement `Hive Bond` / `Hive Bond Boost` | Hold the Line, Hold the Line Boost Aura, Courage Buff |
| F14 | aura wrappers of live rules | 30 | **DONE** (30/30) | `aura` + `CoreRuleCatalog`'s aura factory | Rending when Shooting Aura, Precision Fighter Aura, Precision Shooter Aura, Piercing Fighter Aura, Piercing Shooter Aura, Thrust in Melee Aura, Precision Charge Aura, Strider Aura, Increased Shooting Range Mark |
| F11 | ignore cover / LoS | 25 | **DONE** (25/25) | core `Indirect` (`ignoreCover`, `ignoreLineOfSight`) | Ignores Cover, Ignores Cover Aura, Ignores Cover when Shooting, Ignores Cover when Shooting Aura, Indirect Mark, Indirect when Shooting Aura |
| F13 | Buff family | 15 | **DONE** (15/15) | core `Furious Buff` (pre-attack cross-unit grant, `FirstTrigger`) | Rapid Advance Buff, Entrenched Buff, Precision Shooter Buff, Bane in Melee Buff, Guarded Buff, Precision Fighter Buff, Precision Attacks Buff, Increased Shooting Range Buff |
| F15 | already-built one-offs | 10 | **DEFERRED to #197** (0/10) | ~~`triggeredMove` caster-directed (#18), `applyFatigue` (#9)~~ - wrong: needs `moraleTestThen`, which only fires inside spell casting (see Decisions) | Mind Control, Fatigue Debuff, Vengeance |

**1,169 of 1,243 refs (94%) now resolve as data.** 74 refs did not: 16 moved to #197 (F5's Warbound/Infected
Boost, F10's Mischievous/Scrapper Boost - both hit a real primitive gap, not a lint gap), 10 moved to #197
(all of F15 - the family's premise was wrong, see Decisions), and F16's 48 remain blocked on owner input.

**F16 is blocked, not deferred silently.** `Banner`, `Sergeant`, `Musician`, `Armor` are wargear names
the OPR importer emitted as rule names; they are absent from the rules-page extract, so their mechanics
are unknown. Do not guess. Ask before authoring; if they turn out to be pure list-building flavor, they
should follow `Unique`'s precedent (a registered zero-hook definition, enforced elsewhere or nowhere).

**Asked the owner 2026-07-09.** Verdict: don't guess, investigate further. Two structurally different
groups surfaced while checking which books use them (`.fdgbook` corpus, `--rule-coverage`-style scan):

- **`Armor` is `coreNumeric`** (`Armor(X)`), always bundled inside a single item alongside other
  well-known numeric core rules (`Fast`, `Fear(1)`, `Impact(6)`, `Tough(6)`) — e.g. Wormhole Daemons of
  Lust's "Razor-Flail Chariot (Armor(3), Fast, Fear(1), Impact(6), Tough(6))", or Human Defense Force's
  "Heavy Armor (Armor(4))" on Company Leader/Veterans. It reads like a mount/vehicle stat-block slot
  (a Defense-side numeric, the shape `Tough(X)` is on the wound side), not flavor text — a wrong guess at
  its mechanic risks a real balance error, not just a cosmetic gap. Appears across Dark Elf Raiders,
  Goblin Reclaimers, Human Defense Force, Saurian Starhost, and three Wormhole Daemons sub-factions
  (Lust/War/Change).
- **`Banner`/`Sergeant`/`Musician` are plain `core` rules** (no argument), offered as a uniform
  "upgrade up to three models, pick one each" champion package at a consistent 5/15/10-point spread
  (Sergeant/Musician/Banner respectively) across all four Wormhole Daemons sub-factions
  (Change/Lust/Plague/War) and War Disciples — the same shape and cost in every book that has it.

Owner's read: Banner is very likely an aura-like unit-wide buff (its cost, 3x Sergeant's, points that
way); Musician "sounds bard-like, also like an aura"; Sergeant is unclear beyond "assigned to weapon
names and whatnot, nothing specific." Owner's working theory is these may be **miscategorized aura
abilities** rather than inert flavor — the consistent cross-faction cost/shape supports a real shared
mechanic, not per-faction flavor text that would vary. `Armor`'s mechanic is completely open. **Left
blocked pending further investigation** (a fresh, targeted read of the OPR wargear/upgrade rules text
for these four specific names, rather than the rules-page extract this item was scoped against, which
doesn't cover wargear).

### Notes on the F14 aura wrappers

Several aura names grant a base rule whose canonical spelling may differ (the resolver is
case-insensitive but not fuzzy: "Bane when Shooting Aura" grants "Bane when shooting"). If a granted
name has no definition anywhere, `--validate-rules` fails — that is the intended signal.

**Resolved 2026-07-09.** Six of the nine Auras needed a base rule authored first, not two as guessed here:
`Precision Fighter`, `Precision Shooter`, `Piercing Fighter`, `Piercing Shooter`, `Precision Charge`
(F14), and `Rending when shooting` (F14) — none referenced directly by the corpus, all expressible in the
existing closed vocabulary (simple hit/AP shifts gated only by combat kind), so all six landed as data, not
engine work. `Thrust in Melee Aura` and `Strider Aura` needed no new base — they grant the existing catalog
`Thrust`/`Strider` directly. F12's Mark family needed three more of the same shape (`Precision Fighting`,
`Piercing Shooting`, `Precision Shooting`), and F13's Buff family two (`Guarded`, `Precision Attacks`, plus
`Entrenched` from naming convention alone — see below).

## Notes

- 2026-07-09: **Three defect classes found in this item's shipped data while building #197's shoot-or-charge
  gate.** All three were fixed there (app `27c55c4`); recorded here because this is where they were authored.
  - **A Boost is the INCREMENT, not the boosted rule.** The corpus writes "gets extra hits on 5-6, instead of
    only on 6", but the engine composes base + Boost *additively* (`HitInjectionSink`, `RollModifierSink`,
    `MovementModifierSink` all add; `WoundIgnoreSink` takes the min, which is the only reason F2's Boosts were
    right). `Devout` + `Devout Boost` gave **two** extra hits on a natural 6. 45 corpus units carry both.
    Affected F3, F4, F6, F7, F8. I generalized the wrong lesson from the shipped `Highborn Boost` template,
    which is correct only because its increment happens to equal its base.
  - **A `rollModifier(Hit)` at `Shooting_OnHitRollComplete` is never read** — the dice are already rolled, and
    only `Save` deltas fold from that hook. `Changebound` and `Machine-Fog` (F4) were complete no-ops. Core
    `Stealth`, the identical shape, correctly sits at `Shooting_OnHitRollModifier`. **Check where an effect is
    consumed, not just that the hook accepts the condition.**
  - **`distanceGreaterThan` can never pass in melee** (base contact is <= 2in), so six rules' "or charged from
    over 9in away" arm was dead. Use `attackedFromOverInches`, built in #197.
  - **Why the gates missed all three:** `--validate-rules` checks structure, and `RuleFireLint` proves an entry
    *can* fire but explicitly does not check that its operations are *consumed* at that hook (its own doc says
    so), nor what several rules *sum to*. `FdgRaylib.Tests/BoostRuleCompositionTests.cs` now asserts the **net**
    effect through the real evaluator and sinks; the lint's consumption gap is filed as a #197 slice.
- 2026-07-09: F16 asked of the owner; verdict is "investigate further, don't guess" — see the F16 section
  above for the corpus data (which books/units, `Armor`'s `coreNumeric` shape vs. `Banner`/`Sergeant`/
  `Musician`'s uniform cross-faction champion-package shape) and the owner's aura-miscategorization
  theory. Still blocked; not authored.
- 2026-07-09: Renumbered 191 -> 196 (owner-directed merge with origin/master — see the superproject
  merge commit). A parallel Tactician AI session had already claimed 191/192/193/194 with real merged
  work by the time this item's number was chosen; per the never-reuse rule the unmerged local item
  yields. No content changed, only the number and cross-references.
- 2026-07-09: **Item closed** (F16 excepted — blocked on owner input). Slice 1 (`--rule-coverage`) shipped
  first, then F1/F4/F2/F3/F7/F5/F6/F8/F12/F10/F9/F14/F11/F13 in that order (F5 and F10 partial, F15 fully
  deferred), one commit per family/slice, engine untouched throughout (1338 tests green at every step;
  never edited). Corpus dead-reference count: 2,197 -> 1,028 (1,016 no-definition across 109 names, 12
  scope-mismatch — unchanged, all `Strafing`, #197's business). Full verification loop (validate, fire-lint,
  engine suite, rebake all 47 books, build, headless smoke, coverage delta) ran and passed before every
  commit; coverage deltas matched each family's expected ref count exactly, with zero surprise regressions.
  See Decisions for the three things that didn't go as planned.
- 2026-07-09: #197 slice 0 shipped, moving 145 refs from dead to attaching. The dead total is now **2,197**
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
- **RuleFireLint gaps get allowlisted only when the underlying mechanism is independently proven; a real
  gap gets deferred, never forced.** Two different failure shapes surfaced during authoring, and they got
  opposite treatment:
  - F1's `Shatter`/`Tear`/`Melee Slayer`/`Ranged Slayer` failed the lint because its synthesized contexts
    don't model a Tough-majority target — but `ConditionEvaluationTests.TargetMajorityHasTough_...` and
    `.StatGreaterOrEqualTo_Tough_...` prove the condition fires correctly in the live engine. That's a lint
    *coverage* gap, not a rule defect, so those four are allowlisted in `RuleSupplementLintTests` with a
    reason, per the lint's own documented escape hatch ("the allowlist IS the documented not-covered
    ledger").
  - F15's `Mind Control`/`Fatigue Debuff` failed the lint because `Effect.MoraleTestThen.Apply()` is a
    genuine, intentional no-op — `CastSpellStage` special-cases the effect *before* calling `Apply()`, and
    none of the five generic ability-offering stages RuleFireLint checks against do the same. Authored as
    plain activated abilities (this item's whole shape), they would silently do nothing in play — exactly
    the failure mode the lint exists to catch. This one could not be allowlisted away; it moved to #197.
    F15's whole premise ("already-built one-offs... needing no engine work") was wrong for 2 of its 3 names.
- **A capability a hook's context doesn't carry can silently gate out a whole rule, not just fail loudly.**
  `SaveRollCompleteContext` has no `DistanceInches` and `CoverIgnoreContext` has no combat-kind flag.
  The first (F5's Warbound Boost/Infected Boost, needing ">9in away" at the save-roll stage where
  `AddExtraWound` reads its own histogram) is unbuildable as data — deferred to #197. The second
  (F11/F14's "when Shooting" cover/LoS-ignore variants) turned out to be harmless: cover and LoS-ignoring
  are never consulted for a melee attack in this engine regardless, so the missing gate is redundant, not
  broken — those shipped as data with the gate dropped and a comment explaining why. Same missing
  capability, opposite consequence; each needed checking against what the effect actually does, not just
  against whether the condition would evaluate.
- **`RerollCondition.OnUnmodifiedValue` has no threshold parameter** (unlike `AddExtraHit`/`AddExtraWound`,
  which take a real per-entry `OnRollValue`) — `RerollSink.cs` hardcodes it to the unmodified max face (6).
  F10's Mischievous Boost/Scrapper Boost need "reroll on 5 *or* 6", which has no expression here; deferred
  to #197 rather than approximated.
- **Two support-base names have no rule text anywhere in the source material**: `Entrenched` (F13) and
  `Precision Attacks` (F13). Every other name in this item — including the 14 other support-only bases that
  are never referenced directly by the corpus — was checked against real rule text first. These two were
  authored from naming convention alone (the same plain, ungated single-stat shape every other support base
  in this item uses), because the convention across ~20 comparable bases is strong enough to be a reasonable
  default, but it is a genuinely weaker basis than the rest of this item and is called out here so it's easy
  to find if either one ever misbehaves.

## Outcome

**Closed 2026-07-09** for everything except F16 (blocked on owner input on wargear mechanics — see the F16
row above; can reopen as a small follow-up once that's answered).

- Slice 1 (`--rule-coverage`) shipped: `edb2f70`.
- 13 families authored as data across 13 commits (`bc3c2e3` F1, `c016d02` F4, `3160835` F2, `0ff680c` F3,
  `10c2dcf` F7, `90a5fa7` F5 partial, `0e21373` F6, `5523789` F8, `11e58aa` F12, `8934d3b` F10 partial,
  `b862606` F9, `b1ad168` F14, `6eb488b` F11, `db9e6e0` F13). 107 rule names authored (95 real family
  members + 12 support-only bases needed for Aura/Mark/Buff grants but never referenced directly by the
  corpus: `Ranged Slayer`, `Sturdy Boost`, `Machine-Fog Boost`, `Self-Repair Boost`, `Predator Shooter`,
  `Clan Warrior Boost`, `Primal Boost`, `Scurry Boost`, `Point-Blank Piercing`, `Havocbound Boost`,
  `Targeting Visor Boost`, `Hold the Line Boost`, plus the 6 F14/F12/F13 hit-modifier bases already listed
  above).
- **1,169 of 1,243 refs (94%) now resolve.** Corpus-wide dead references: 2,197 -> 1,028.
- **26 refs moved to #197** (not authorable as data — see Decisions): Warbound Boost/Infected Boost + Auras
  (10, distance unavailable at the save-roll hook), Mischievous Boost/Scrapper Boost + Auras (6, no
  reroll-threshold parameter), Mind Control/Fatigue Debuff/Vengeance (10, `moraleTestThen` is spell-only;
  Vengeance also needs a model-count magnitude source).
- **F16 (48 refs) still blocked** on what `Banner`/`Sergeant`/`Musician`/`Armor` actually do.
- Every commit passed the full verification loop (validate-rules, fire-lint, engine suite, all-books
  rebake, full build, headless smoke, coverage delta) before landing; no rule was authored and left
  unverified. Engine test count never moved (1338 throughout) — this item touched no submodule file except
  the intentional `RuleSupplementLintTests.cs` allowlist entries, which are app-side.
