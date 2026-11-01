# 422 — Impact before either unit attacks + Fatigue gate; Counter = strike-first only; new Brace(X)

**Status**: done 2026-10-29 (engine `4f23f9b2`; superproject commit `422: ...` bumps it with the supplement data, app
tests and docs) - awaiting Chris's GUI hand-verify (list in Notes). Owner review asked on D1, D4
and D8 (an Impact per-model question found on the way).
**Related**: #415, #030 (Impact/Counter), #184 (Counter strike sequencing), #355 (Impact unreachable on vehicles - 3.6 replaces vehicle Impact with Overrun weapons, so #355 may close), #435 (Brace in Melee), #442
**Branch**: `feature/opr-3.6`. Model/effort: Opus 5.5 / high.

## Goal

Core Rules v3.6.0:
- Impact(X): "When charging, before either unit attacks, roll X dice, unless this unit is Fatigued. For each
  2+ the target takes 1 hit."
- Counter: "May strike first with this weapon when charged, unless this unit is Fatigued."
- Brace(X) (new): "Enemies charging this unit get -X total Impact rolls, unless this unit is Fatigued."
- Book grants: "Counter in Melee" = "This model gets Counter on its weapons in melee"; "Brace in Melee" (AoF)
  = "This model gets Brace(1) on its weapons in melee"; plus Aura/Buff variants of both.

In 3.5.1 Counter carried both facets (strike first + "-1 total Impact rolls per model with Counter") and
Impact was rolled "when attacking after charging". "Done" = Impact hits resolve (hit -> save -> wounds) before
any strike, including before Counter strikes; a Fatigued charger rolls no Impact; Counter only reorders the
strike (and only when the countering unit is not Fatigued); Brace(X) reduces the charger's Impact dice by X
total per Brace rule instance on the defending side (sum over models carrying it, floor 0), not when the
Brace unit is Fatigued; Brace is a unit-or-weapon-scoped rule (AoF puts Brace(1) on weapons via "Brace in
Melee"), so the catalog entry must accept both attachment scopes like Counter does.

## What changes

1. Engine: `ImpactSink` / Impact catalog entry: move the hook to a pre-strike point in the melee stage
   (before `PreAttackStage` resolves Counter ordering); gate on not-Fatigued. Counter: delete the
   `ReduceImpactDicePerModel` facet. New `CoreRuleCatalog.Brace` with the reduction, Subject seat, Fatigue
   gate. Supplement: `Counter in Melee`, `Counter in Melee Aura/Buff`, `Brace in Melee` (+Aura) as grants.
   Remove `CounterAttack` (3.5-only, dropped by 3.6) - coordinate with #436.
2. Tests: Impact resolves before Counter strikes and its wounds count for #419; Fatigued charger = 0 Impact
   dice; Brace(2) on a 3-model unit vs Impact(6) = 4 dice (or confirm "total" means per rule instance - read
   the AoF Brace in Melee text: "Brace(1) on its weapons", i.e. per model-weapon, so total = sum); Fatigued
   Brace unit = no reduction; Counter unaffected by Impact.
3. Resolvers/presentation: the impact dice beat plays before the strike beats; the melee panel shows Brace on
   weapon rows (#336 structured rules); AI expected-damage (#442) told.

## Acceptance

Tests green; a hand-verified charge shows Impact dice first, then Counter strikes, then charger strikes.

## Code anchors (survey 2026-10-29)

- `Rules/Dispatch/ImpactSink.cs`, `CoreRuleCatalog.Impact` / `.Counter` / `.CounterAttack`, `PreAttackStage`,
  melee strike ordering in `MeleeStage`. See the appended survey rows below for exact lines.

### Survey rows (code survey 2026-10-29)

Findings that shape the item: Impact ALREADY resolves before the Counter swap and all strikes (pipeline
`MeleeStage.cs:53-58, :81-133`: ChooseMeleeDefender -> ResolveImpactHits -> DetermineStrikeOrder -> ResolveRavage
-> PileIn -> in-range sets -> swings -> DetermineMeleeWinner -> morale -> ApplyFatigue -> Consolidate). What is
left: the Impact Fatigue gate, deleting Counter's impact facet, adding Brace(X), and re-authoring AoF's
"Counter in Melee" which today carries `reduceImpactDicePerModel`.

- Impact: `CoreRuleCatalog.cs:557-567` (doc :555 says the not-fatigued gate is deferred); `Effect.cs:961-967`;
  `Rules/Dispatch/ImpactSink.cs` (AddDice :13, TotalDice :47, AP max :21); `ResolveImpactHitsStage.cs` (:23 no
  gate, :50-56 seats, :69-70 roll, :93 synthetic AP-0 weapon); AI `CombatMath.cs:~240`.
- Counter: `CoreRuleCatalog.cs:599-616` (:602-606 StrikeFirst on `Melee_OnCounterTrigger`; :607-611
  `ReduceImpactDicePerModel` on `Melee_OnChargeContact`; doc :590-598); Counter-Attack :624-635 (dropped by 3.6);
  `Effect.cs:979-993` `ReduceImpactDicePerModel`; `StrikeFirst` `Effect.cs:1013-1019`, `RuleOperation.cs:825-833`;
  swap `DetermineStrikeOrderStage.cs:42-66`; AI `CombatMath.cs:244-253`.
- Data: `AofRuleSupplement.json:2332` "Counter in Melee" (uses `reduceImpactDicePerModel` at :2351). Brace: absent.
- Tests: `CounterRuleIntegrationTests.cs`, `ImpactRuleIntegrationTests.cs` (CounterDefender_ReducesImpactDice_*),
  `WeaponScopedCounterAndSightTests.cs`, `FatigueTests.cs` (CounterSwap_*), `ImpactOnlyChargeRuleIntegrationTests.cs`.

## Notes

- 2026-10-29: **Landed.** Engine commit `4f23f9b2` (submodule, `feature/opr-3.6`); the superproject commit bumps it
  with the supplement data, app tests and docs.
  - **Survey finding confirmed.** Impact already resolved before either unit attacks: `MeleeStage` runs
    `ResolveImpactHitsStage` before `DetermineStrikeOrderStage`, so impact hits land before any swing, a Counter
    swing included. No pipeline change; the stage and catalog docs now say so in 3.6 words.
  - **Fatigue gate.** New `Condition.CountsAsFatiguedInMelee` (JSON `countsAsFatiguedInMelee`): true when the
    bearer is Fatigued or Shaken. Its static `Holds(IUnit)` is now THE predicate - `FatigueUtilities.
    CountsAsFatiguedInMelee` (the 6+ threshold, #418's special-rule sixes, CombatMath) delegates to it, so rule
    data and stage code cannot disagree (it lives in the Rules layer, which does not reference the stage layer).
    Impact, Counter and Brace gate on `Not(CountsAsFatiguedInMelee)`; it reads each rule's own bearer, so the
    charger's fatigue switches off its Impact and the charged unit's switches off its Counter / Brace (D1).
  - **Counter split.** `CoreRuleCatalog.Counter` is one Subject-seat `StrikeFirst` entry with the gate; the
    `ReduceImpactDicePerModel` facet is gone. The Effect kind `reduceImpactDicePerModel` still parses, as
    `Effect.LegacyReduceImpactDicePerModel`, a no-op - the bundled AoF-ChivalrousKingdoms book and pre-3.6 army
    files embed it inside "Counter in Melee" (D5).
  - **Brace(X).** New `CoreRuleCatalog.Brace` (weapon scope, re-homed from a unit-level mention like Counter;
    `EngineArgumentCount: 1`; Subject seat at `Melee_OnChargeContact`; the gate; the 3.6 text). Its effect,
    new `Effect.CancelImpactDice(DicePerModel)` (JSON `cancelImpactDice`), emits a negative
    `RuleOperation.ChargeImpactHits` of X per living model whose MELEE weapon carries it (every living model when
    unit-attached), folded into the existing `ImpactSink`; the stage already skips at zero or below (the floor).
    "Total" = the sum over models (D2). Counting is per attachment, via a new `RuleInvocation.Attachment` the
    evaluator fills, because a resumed game rehydrates one attachment per weapon (D3).
  - **Counter-Attack (+Aura) deleted** from the catalog (3.6 dropped both). A list naming them loads and reports
    each as "not implemented" (#168); pinned in `CounterLegacyDataTests` and by hand (below).
  - **Supplements.** `GdfRuleSupplement.json`: "Counter in Melee" (moved here from the AoF file - GDF 3.6 books
    name it too, and GDF books bake against the GDF file alone; a Counter clone with the gate, no impact facet),
    "Counter in Melee Aura" (aura granting "Counter"), "Counter in Melee Buff" (Bane in Melee Buff's
    trigger/cost/selector granting "Counter in Melee" NextTrigger, D4); Heavy Impact gains Impact's gate and the
    3.6 text ("Counts as having Impact(X) with hits that have AP(1)."). `AofRuleSupplement.json`: "Counter in
    Melee" removed, "Brace in Melee" added (weapon scope, the gate, `cancelImpactDice` literal 1, the 3.6 text).
    No 3.6 book references a "Brace in Melee Aura" or "Buff" (grepped both snapshot folders), so none was added.
    `.fdgbook` files untouched (#438).
  - **Validator / lint.** `RuleValidator.ValidateAuthoring` refuses the legacy `reduceImpactDicePerModel` kind
    (the load gate still accepts it), mirroring #428 D4. `RuleFireLint` gives a weapon rule whose entries are all
    melee hooks a melee lint weapon (Brace counts melee carriers; D6) and can seed a positive
    `CountsAsFatiguedInMelee` leaf.
  - **AI / calculator / presentation.** `CombatMath.EstimateMelee` reads Impact, Brace and Counter through the
    same evaluation as the stages, so the gate and the split reached it with no code change (comments updated,
    three pins added). The Combat Calculator runs the real `ResolveImpactHitsStage` (confirmed; pin added: a
    fatigued Impact charger gets no Impact row). The impact dice beat already precedes the strike beats. The melee
    weapon menu's #336 `OptionRules` lists every weapon rule with its definition's description, so a Brace(1)
    spear shows "Brace" with the 3.6 text (generic code, no change).
  - **Tests.** Engine: new `BraceRuleIntegrationTests` (12: Brace(2) x 3 models vs Impact(6) = 0 dice, Brace(1)
    x 2 vs Impact(6) = 4, Fatigued / Shaken Brace unit cancels nothing, fatigued charger into Brace rolls nothing,
    weapon-carried Brace counts only its carriers, a ranged-only copy does not count, dead carriers do not count,
    shared vs per-weapon attachments cancel the same 3 dice, two attachments on one model stack, not a reason to
    charge, catalog entry shape) and `CounterLegacyDataTests` (6: Counter-Attack family gone; a legacy list naming
    both loads with two Unimplemented drops and the audit agrees; an embedded 3.5 "Counter in Melee" still strikes
    first but cancels no dice; the legacy kind is a no-op, passes load, is refused at authoring; round-trips under
    its old name; the new kinds round-trip). Re-pinned: `ImpactRuleIntegrationTests` (+ Fatigued / Shaken / gated
    Heavy Impact charger roll no dice; `CounterDefender_*` flipped: Counter no longer reduces, Brace reduces by
    living models, can reach zero, no-Impact charger), `CounterRuleIntegrationTests` (Counter-Attack case retired;
    Fatigued and Shaken defender do not strike first; a Fatigued charger does not stop a fresh defender's
    Counter), `WeaponScopedCounterAndSightTests` (Counter weapon cancels nothing; Brace weapon counts only its
    carriers), `MostModelsRuleGateIntegrationTests` (CounterAttack_* retired - it was the only unit-scoped
    StrikeFirst rule), `ImpactOnlyChargeRuleIntegrationTests` (the defensive charge-contact case uses Brace),
    `CombatMathPinTests` (+3: fatigued Impact charger, Brace defender, fatigued Counter defender),
    `CombatCalculatorTests` (+1). `FatigueTests.CounterSwap_*` needed no change (it pins the role swap, not
    Counter's rule). App: new `CounterBraceShippedDataTests` (8: the four grants and Heavy Impact as authored, and
    Brace in Melee / Counter in Melee driven through the real army-load path and evaluator, fresh and Fatigued).
    Allowlists with reasons that go stale at the re-bake: `BookRuleCensusTests` (Counter-Attack, Counter-Attack
    Aura), `BookSpellCoverageTests` and `BookSpellCastProbeTests` (Counter-Attack, granted by two 3.5 spells - the
    cast probe gained a small known-dead mechanism with a stale check), `BookRuleFireLintTests` (the 3.5 "Counter
    in Melee" in AoF-ChivalrousKingdoms keeps the inert legacy facet).
  - **Negative controls run by hand.** Forcing the three catalog conditions to `Always` fails 11 of the new tests
    (every fatigued/Shaken case; the fresh controls pass). Counting Brace carriers by definition instead of by
    attachment fails `SharedAndPerWeaponAttachments_CancelTheSameDice`.
  - **Counts.** Engine 3486 -> 3513 passed (1 skipped before and after). FdgRaylib.Tests 3064 -> 3075.
  - **Verification.** Engine suite green; FdgRaylib.Tests green; full `dotnet build` 0 errors (no warnings on
    touched files); headless smoke `printf "2\n2\n" | ... --headless` exit 0 on every run (results vary run to run:
    "outcome=Win winner="Player 1" rounds=4" once, "outcome=Tie ... rounds=4" on the final run). The built-in Heavy
    Gunners' Counter no longer cancels the Warriors' Impact(2), so those charges play differently: the log shows
    "Warriors's Impact rolled 2 dice -> 2 hit(s) on the charge." ahead of "Heavy Gunners counters the charge -
    strikes first!"; `--validate-rules
    GdfRuleSupplement.json AofRuleSupplement.json` "OK: 337 definitions, no problems." (each AofBookOverrides file
    on top also OK); `--rule-coverage FdgRaylib/Assets/Books` "Total references: 22079 / Dead: 36 (36
    no-definition across 2 names)" = Counter-Attack Aura 27 + Counter-Attack 9, in 15 bundled 3.5 books (AoF Ogres,
    Orcs, Ratmen, Rift Daemons of Lust, Saurians, Shadow Stalkers, Sky-City Dwarves; GDF DAO Union, Elven Jesters,
    Eternal Dynasty, Rebel Guerrillas, Saurian Starhost, Wolf Brothers, Wolf Prime Brothers, Wormhole Daemons of
    Lust) - the expected 3.6-dropped family only, until #438 re-bakes. Legacy load: `armies/AoF/1k - Ogres.fdgarmy`
    vs `armies/GDF/3k - DAO Union.fdgarmy` played headless to exit 0 (their Counter-Attack refs sit in untaken
    upgrades), and a scratch copy of the Ogres list with Counter-Attack, Counter-Attack Aura and an embedded 3.5
    "Counter in Melee" on a fielded unit printed "[rules] Skipping unimplemented special rule 'Counter-Attack' on
    unit 'Tribe Master'." (and the Aura) and played to exit 0.
  - **Awaiting GUI hand-verify (Chris).** No unit test covers ImGui drawing. In a game with the built-in armies
    (Warriors have Impact(2); Heavy Gunners have Counter on their Fists):
    1. *Order (the item's acceptance).* Charge the Heavy Gunners with fresh Warriors: the "Impact Hits" dice beat
       plays first, then the "counters the charge - strikes first!" banner and the Gunners' strikes, then the
       Warriors' strikes.
    2. *Fatigued charger.* Charge with Warriors that already fought this round (struck back earlier): no "Impact
       Hits" dice beat at all; the melee opens with the swings.
    3. *Fatigued defender.* Charge Heavy Gunners that already fought this round: no "counters the charge" banner;
       the charger swings first and the Gunners are offered the strike-back.
    4. *Brace.* No bundled 3.5 book names Brace until #438; to see it now, hand-edit an `.fdgarmy` to give a melee
       weapon `{"kind": "coreNumeric", "name": "Brace", "numericValue": 1}` and charge that unit with an Impact
       unit: the impact dice beat shows the reduced count (Impact minus one per living spearman; none when the
       spear unit is Fatigued), and the melee weapon menu's spear row underlines "Brace" with the 3.6 text.
    5. *Combat Calculator.* Melee, charging, "attacker fatigued", an Impact unit: no Impact row.
  - **Deferred / handed on, named.** (1) The bundled books still name Counter-Attack (+Aura) and embed the 3.5
    "Counter in Melee" and an ungated Heavy Impact; #438's re-bake fixes all three and must remove the four
    allowlist entries (noted there). (2) Two 3.5 spells grant Counter-Attack; their 3.6 text grants "Counter in
    Melee once" - #439 (noted there). (3) D8 (Impact per model) is an owner question, not built. (4) Old user
    `.fdgarmy` files that embed Heavy Impact / Counter in Melee keep their saved, ungated copies (D1). (5) The
    log observation in D7.
- 2026-10-29: filed with the plan.

## Decisions

- **D1 - where the Fatigue gate lives (2026-10-29) - OWNER REVIEW.** Each rule's own entry condition,
  `Not(CountsAsFatiguedInMelee)`, on Impact, Counter and Brace in the catalog and on the supplement copies that
  count as them (Heavy Impact, Counter in Melee, Brace in Melee). Chosen because the clause is part of each rule's
  own 3.6 text (unlike #418's Fatigue rule, which applies to every rule and needed one seam), it shows in the data
  and in `--trace-rules` ("condition Not(CountsAsFatiguedInMelee) not met"), and it matches #354's owner ruling
  that embedded definitions behave as saved. Consequence: copies embedded BEFORE this change - the bundled books
  until #438 re-bakes (Heavy Impact in 7 bundled books, Counter in Melee in AoF-ChivalrousKingdoms) and old user
  army files - stay ungated (a Fatigued Heavy Impact unit still rolls; a Fatigued "Counter in Melee" unit still
  strikes first). Alternative: gate inside the mechanics instead (`ChargeImpactHits`, `StrikeFirst`,
  `CancelImpactDice` emit nothing for a Fatigued bearer), which also reaches those embedded copies; the cost is a
  hidden gate (traces would read "condition passed but X produced no operations"). Easy to switch if preferred.
- **D2 - "total" in Brace(X) (2026-10-29).** Read as the SUM over the defender's living models carrying it, X
  each: Brace(2) on 3 models cancels 6 (Impact(6) -> 0 dice), Brace(1) on 2 models cancels 2 (Impact(6) -> 4).
  The 3.6 data settles it: Brace(1) is a weapon rule (AoF Spear: "Counter, Brace(1)"; "Brace in Melee" puts
  Brace(1) on a model's weapons), rules apply per model, and "total" refers to the charger's whole Impact pool.
  (The item's Tests line floated "4 dice" for Brace(2) x 3 as the other reading - a flat -X per unit - which would
  let a 10-model spear unit cancel only one die.) Floor at 0: the stage skips at zero or below, as it did for 3.5
  Counter.
- **D3 - counting per attachment (2026-10-29).** An (X) rule fires once per distinct attachment. A fresh army
  shares one attachment across a weapon entry's copies; a resumed game rehydrates one per weapon. Counting every
  carrier on every firing (Ravage's pattern) would multiply Brace after a resume, so `CancelImpactDice` counts the
  living models whose melee weapon carries THIS attachment, via a new `RuleInvocation.Attachment` the evaluator
  fills. An argument-less copy (Brace in Melee's literal 1) fires once per bearer, so it counts every carrier of the
  definition. Melee weapons only ("on its weapons in melee": a unit-level mention is re-homed onto every weapon,
  rifles included). Two different Brace attachments on one model stack, as (X) rules do.
- **D4 - the Counter in Melee family (2026-10-29) - OWNER REVIEW on the Buff duration.** "Counter in Melee" moved
  to the GDF file (GDF 3.6 books name it; GDF bakes against that file alone) and stays the #375 C9 clone shape
  (Counter's entry copied, weapon scope), now with the gate and without the impact facet. "Counter in Melee Aura"
  grants the core rule "Counter" by name, like "Thrust in Melee Aura" grants "Thrust". "Counter in Melee Buff" uses
  Bane in Melee Buff's trigger, cost and 12" selector and grants "Counter in Melee" with **NextTrigger** (spent the
  next time the unit is charged). Its 3.6 text is the only melee Buff WITHOUT "once (next time the effect would
  apply)" - Bane/Thrust in Melee Buff and Melee Evasion Buff all say it, and the two 3.6 spells that grant the same
  rule (Evil Gaze, Asphyxiating Fog) say "gets Counter in Melee once (next time ...)". Options: (a) NextTrigger,
  chosen - consistent with every sibling; (b) ThisRound; (c) UntilEndOfGame, the literal reading of a missing
  duration, which turns a once-per-activation ability into a growing army-wide grant. One JSON field if overruled.
- **D5 - the legacy kind and the dropped names (2026-10-29).** `reduceImpactDicePerModel` keeps parsing as a no-op
  (`LegacyReduceImpactDicePerModel`) so embedded 3.5 copies load with 3.6 semantics (no reduction); the army-load
  gate accepts it, the authoring validator refuses it (#428 D4's pattern, so the supplement sweep is
  self-checking). Counter-Attack and Counter-Attack Aura are simply gone: a reference degrades to the #168
  Unimplemented drop. No name remap to Counter (plan rule 6: no 3.5 fallbacks).
- **D6 - fire-lint carrier (2026-10-29).** `RuleFireLint` built every weapon rule on a 24" lint weapon, so Brace's
  melee-only count produced nothing there. A weapon rule whose entries are all melee hooks now gets a melee lint
  weapon (as in play); every other rule is linted exactly as before.
- **D7 - descriptions and a log observation (2026-10-29).** Impact, Counter and Brace in the catalog, and Heavy
  Impact and the four grants in the supplements, use the 3.6 text verbatim (ASCII), since their behaviour changed
  here; #436's parity pass can skip them. Observation, not changed: every charge into a Brace unit narrates "X's
  Spear's Brace cancelled N impact dice from the charge." even when the charger has no Impact (exactly what 3.5
  Counter did through the same op), and `ResolveRavageWoundsStage` re-evaluates the charge-contact hook, so the
  Impact/Brace op lines print twice (visible in the smoke log for Impact; pre-existing). Candidate small follow-up
  if the owner finds it noisy.
- **D8 - Impact(X) is not per model (2026-10-29) - OWNER QUESTION, not built.** `Effect.ChargeImpactHits` rolls X
  dice once per unit; it is not scaled by the models carrying the rule the way Ravage's `DealAutoWounds` is (whose
  comment wrongly says it mirrors Impact). Rules apply per model in OPR, and 42 multi-model 3.6 units carry Impact
  (e.g. Orcs Iron Tusk-Beast Riders [5] Impact(1), Havoc Dwarves Bull Centaurs [3] Impact(2)), which today roll 1
  and 2 dice instead of 5 and 6. Brace's "-X total Impact rolls" reads naturally against a per-model pool. Pre-dates
  this item (#042) and is outside its scope, so recorded rather than changed; candidate new item in the OPR 3.6
  block (`git fetch origin` before taking the number), touching `ChargeImpactHits`, CombatMath pins and the
  calculator's dice count.

## Outcome

Done 2026-10-29 on `feature/opr-3.6`. Impact rolls no dice for a charger that counts as Fatigued (Fatigued or
Shaken), still before either unit attacks; Counter only strikes first, and not when the countering unit is
Fatigued; new core Brace(X) cancels X of the charger's Impact dice per living model carrying it (floor 0), not when
Fatigued; Counter-Attack (+Aura) are gone and degrade to the "not implemented" diagnostic in old lists. Engine
`4f23f9b2`. GDF/AoF
supplements carry Counter in Melee (+Aura/Buff), Brace in Melee and a gated Heavy Impact. AI and calculator follow
through the shared evaluation. Open: Chris's GUI hand-verify; D1, D4 and D8 for the owner; the bundled books are
stale until #438 (allowlisted).

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
