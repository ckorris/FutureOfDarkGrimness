# 447 — Impact(X) is rolled per unit; the rule is per model

**Status**: done 2026-10-30 (engine `5fd93128` + `26d9e71e`; the superproject commit `447: ...` bumps them with the
app shipped-data test, docs and this ledger) - awaiting Chris's GUI hand-verify (list below). Owner review asked on
D1 and D3. Filed 2026-10-29 from #422's D8.
**Related**: #422 (Impact Fatigue gate, Brace; landed), #355 (Impact unreachable on vehicles - 3.6 replaced vehicle Impact with Overrun weapons), #442 (AI parity), #438 (re-bake brings the 3.6 Impact carriers)
**Branch**: `feature/opr-3.6`. Model/effort: Opus 5.5 / medium. Schedule in phase 2, after #422.

## Goal

Core Rules v3.6.0, Impact(X): "When charging, before either unit attacks, roll X dice, unless this unit is
Fatigued. For each 2+ the target takes 1 hit." Like every "this model" rule, Impact is carried per model, so a
unit of 5 models with Impact(1) rolls 5 dice. The engine's `ImpactSink` pools the rule once per unit, so that
unit rolls 1 die. #422's census of the 3.6.0 snapshots found 42 multi-model units carrying Impact (e.g. Orcs
Iron Tusk-Beast Riders [5] Impact(1)), all under-rolling today. Pre-existing bug, not a 3.6 change.

"Done" = Impact dice = sum over living, charging models that carry the rule of their X (a joined hero with
Impact(3) in a unit without it adds 3; a unit-scoped Impact granted by an aura counts every model); Brace(X)
(#422) still subtracts from that total; Heavy Impact and the Impact-granting supplement rules follow;
`CombatMath` and the Combat Calculator mirror it; the impact dice beat shows the real count.

## What changes

1. Engine: `Rules/Dispatch/ImpactSink.cs` / `Effect.ChargeImpactHits` evaluation per carrying model (the
   `RuleInvocation.Attachment` field #422 added distinguishes per-model copies from unit-wide grants), summed in
   `ResolveImpactHitsStage`; keep the AP-max fold.
2. Tests: 5 models Impact(1) -> 5 dice; 3 models of which 2 carry Impact(2) -> 4 dice; hero Impact(3) joined to a
   5-model Impact(1) unit -> 8; casualties before the charge reduce it; Brace subtracts from the sum; Fatigued
   charger still 0; a `CombatMathPinTests` case.
3. Presentation: dice beat count; log line "X's Impact rolled N dice".

## Code anchors

- `Rules/Dispatch/ImpactSink.cs` (AddDice :13, TotalDice :47), `CoreRuleCatalog.Impact` (~:557), `Effect.cs`
  `ChargeImpactHits` (~:961), `MeleeStage/ResolveImpactHitsStage.cs` (:50-56 seats, :69-70 roll),
  `Ai/Tactician/CombatMath.cs` (~:240), `Tests/ImpactRuleIntegrationTests.cs`, `BraceRuleIntegrationTests.cs`.

## Notes

- 2026-10-30: **Landed.** Engine commits `5fd93128` (the rule, presentation, parity, compiler routing) and `26d9e71e`
  (the compiler guard, D3) on the submodule's `feature/opr-3.6`; the superproject commit bumps them with the app
  shipped-data test, `docs/EngineNotes.md` and this ledger.
  - **The rule.** `Effect.ChargeImpactHits` now emits X x the living models carrying the firing attachment
    (`RuleInvocation.Attachment`, #422), and the op carries that carrier count (`RuleOperation.ChargeImpactHits.Models`).
    Who carries (D2): a unit-attached copy counts the living NATIVE models (a joined hero does not inherit host
    rules), a model-attached copy (a joined hero's own Impact, a #407 per-model grant) its living carriers, a
    unit-wide grant every living model (hero included), an argument-less copy every carrier of the definition. Heavy
    Impact rides the same effect (AP fold unchanged, max across sources). Brace's negative op still folds into the one
    `ImpactSink`, so it subtracts from the per-model TOTAL and the stage still skips at zero or below; the Fatigue
    gate is still each entry's condition, so a Fatigued charger emits nothing. Brace's op now also carries its carrier
    count, for the narration only.
  - **Where it is summed.** The charger joins the charge-contact evaluation WITH its living models
    (`ResolveImpactHitsStage.ChargerParticipant`; `ChargeContactParticipants` adds the defender's side). Before, the
    Actor had no models, so a joined hero's Impact (relocated onto its model by the #006 merge) never fired at all -
    a second pre-existing gap, closed here. The evaluator's per-attachment dedup keeps one firing per attachment; a
    fresh army's shared model-grant attachment fires once and counts both carriers, a resumed game's per-model
    attachments fire once each and count one (pinned through a real save/load). Casualties before the charge (the
    #446 activation test, anything else) shrink the count because only living models count.
  - **Narration filter (D4).** Walking the charger's models also reaches a joined hero's Ravage, which the impact
    stage does not resolve; new `RuleEvaluator.EvaluateAllNamedLive(context, narrate, participants)` narrates only the
    ops the stage asks for (impact-sink ops), with grants, trace and the returned queue unchanged.
  - **Presentation (D5).** Log line: "Iron Tusk-Beast Riders' Impact rolled 5 dice (5 models x Impact(1)) -> 5 hit(s)
    on the charge." (possessive, sources merged per rule and X, " + " between sources, " - N Brace" per cancelling rule,
    the authored "(X)" stripped from "Impact(1)"); when Brace cancels everything: "Riders' Impact rolls no dice (2
    models x Impact(1) - 3 Brace)." The evaluator's op line gained the sum: "Warriors's Impact(2) rolled 10 impact dice
    on the charge (5 models x 2)." The "Impact Hits" dice beat rolls the real count and now carries
    `Context` "Riders: 5 models x Impact(1)" (the DiceOverlay draws it under the dice, widening the panel to fit) and
    category Offense (Ravage's pool colour). CLI prints the log line (no CLI code involved). The Combat Calculator's
    Impact row gains the pool as a hit chip.
  - **Parity.** `CombatMath.EstimateMelee` and the calculator's read-only pre-check use the stage's participants, so
    the AI and the calculator sum per model through the same effect (the calculator then runs the real stage).
  - **Compiler (D3).** `ListCompiler.ModelPersonalRuleNames` gains Impact and Heavy Impact, so a one-model item
    ("Replace one CCW -> Rotor Blade Attack (Impact(3))" on 3 Hover Bikers) compiles to a #407 model grant instead of a
    unit-wide Impact(3) that per-model counting would turn into 9 dice. `PlaceModelGrants` skips an item every model
    still holds after the purchase (3.6's "Replace one Lance and Trample -> Trample (Impact(1))" would otherwise grant
    one model and strip the profile copy from the rest). The OPR share-link import already routes through
    `TryAddModelGrant`, so a loadout item on fewer models than the unit follows. `GameBootstrap.AttachModelGrants`
    already accepts any unit-scoped rule.
  - **Census.** Bundled (3.5) books: 73 multi-model units carry Impact / Heavy Impact on the profile (all under-rolled),
    and 2 per-model items do (OrcMarauders Hover Bikers, DarkElfRaiders Jetbike Riders - both now one-model grants;
    no bundled `armies/` list fields either). 3.6.0 snapshots: 42 multi-model profile carriers (#422's figure,
    re-counted), 23 more multi-model units whose profile ITEM gives every model Impact (mounts / Trample - they fold
    unit-wide, so they roll per model too), and 4 per-model items (Orc Marauders Hover Bikers, Dragon Empire
    Dragonkin Horsemen, Ghostly Undead Dread Knights, Ratmen Storm Ogres) - they arrive with #438.
  - **Tests.** Engine: new `ImpactPerModelRuleIntegrationTests` (19, through the real `ResolveImpactHitsStage`: 5 x
    Impact(1) = 5 dice + log line; 2 of 3 models with Impact(2) = 4; the same after a real save/load; hero Impact(3) +
    5 x Impact(1) = 8 + log line; a hero without Impact adds nothing; a hero's Impact in a unit without it = 3;
    casualties before the charge reduce it; only the hero left, without Impact = no roll; Brace subtracts from the
    total (10 - 3 = 7) and floors at 0 with the explaining line; Fatigued 5-model charger = no roll; unit-wide grant
    counts every model incl. the hero; Heavy Impact per model keeps AP(1); the dice beat's total, context and
    category; a joined hero's Ravage is not narrated by the impact stage; `DescribeDicePool` merging/cancels/stripping;
    compile of a one-model item = model grant; army load of it = 3 dice; a replaced profile item stays unit-wide = 5
    dice). Re-pinned old one-die expectations: `CombatMathPinTests` (ImpactHits 2 x Impact(4) = 8 dice; Brace case
    now Impact(2) x 2 - 3) + 1 new (joined hero adds); `SpecialRuleTests.Impact_OnCharge_RollsArgumentImpactDicePerModel`
    (3 x Impact(2) = 6); `CombatCalculatorTests` + 2 (5-model unit = 5 dice with the pool chip; joined hero = 6);
    `RuleOperationDescribeTests` + 3 cases (the per-model suffix). `ImpactRuleIntegrationTests` and
    `BraceRuleIntegrationTests` needed no change: every charger there is a single model (their Brace counts are
    per-defender-model already). App: new `PerModelImpactShippedDataTests` (3: every bundled multi-model profile
    carrier rolls X x models through compile + army load + the real stage; the per-model item population is the two
    bikers; each compiles to a one-model grant and rolls only that model's dice, or stays unit-wide when the profile
    already gives every model the item - the re-baked Trample swap's case).
  - **Negative controls run by hand** (each broke, the named tests failed, then restored): one firing = X dice (16
    engine + the app profile test fail); charger without its models (7 fail: hero, model-grant, resume, army-load);
    no narration filter (the Ravage test); a joined hero counted for the host's static copy (6); argumented copies
    matched by definition instead of attachment (4, incl. the save/load one); grants not counted (1); Impact not
    model-personal in the compiler (2 engine + 1 app); authored "(X)" not stripped (2); the zero-dice line removed (1);
    the beat context dropped (1); the calculator or CombatMath back to unit-only participants (1 each); the held-by-
    every-model guard removed (1).
  - **Counts.** Engine 3973 -> 4004 passed (1 skipped before and after). FdgRaylib.Tests 3104 -> 3107.
  - **Verification.** Engine suite green; FdgRaylib.Tests green; full `dotnet build` 0 errors (no new warnings);
    `--validate-rules GdfRuleSupplement.json AofRuleSupplement.json` "OK: 294 definitions, no problems.";
    `--rule-coverage FdgRaylib/Assets/Books` "Total references: 22079 / Dead: 36 (36 no-definition across 2 names)"
    (unchanged); headless smoke `printf "2\n2\n" | ... --headless` exit 0 with a `Game result:` line. The built-in
    Warriors (5 models, Impact(2)) now charge with 10 dice, and the count falls with their casualties: the smoke log
    shows "Warriors' Impact rolled 10 dice (5 models x Impact(2)) -> 9 hit(s) on the charge.", later "(3 models x
    Impact(2))" and "(2 models x Impact(2))". Built-in-army charges hit much harder than before; that is the rule.
  - **GUI hand-verify (Chris).** No unit test covers ImGui drawing. With the built-in armies (Warriors: 5 models,
    Impact(2)):
    1. *Count.* Charge any unit with full-strength Warriors: the "Impact Hits" dice beat throws 10 dice, and under the
       dice the context line reads "Warriors: 5 models x Impact(2)" (the panel should widen to fit it; the beat is now
       in the Offense colour, like Ravage's pool). The console line matches: "Warriors' Impact rolled 10 dice (5 models
       x Impact(2)) -> N hit(s) on the charge."
    2. *Casualties.* Shoot the Warriors down to 3 models, then charge with them: 6 dice, "3 models x Impact(2)".
    3. *Joined hero (Forge army).* Field a hero with Impact (e.g. an AoF hero on a mount with Impact(X)) joined to a
       multi-model Impact unit and charge: the beat's context lists both sources ("5 models x Impact(1) + 1 model x
       Impact(3)").
    4. *Brace.* As in #422's item 4 (an `.fdgarmy` hand-edited to give a melee weapon Brace(1)): charge that unit with
       the Warriors - the dice shrink by one per living spear model, and the context reads e.g. "Warriors: 5 models x
       Impact(2) - 3 Brace"; if Brace cancels everything, no dice beat and the console says "Warriors' Impact rolls no
       dice (...)".
    5. *Combat Calculator.* Melee, charging, a multi-model Impact unit: the Impact row's dice equal X x models and its
       chips include "5 models x Impact(1)"-style text.
  - **Deferred / handed on, named.** (1) D1 is an owner question (which models count). (2) A joined hero's Ravage
    never fires (`ResolveRavageWoundsStage`'s Actor has no models; `DealAutoWounds` also counts every living model for
    a unit-attached Ravage, a joined hero included, and per definition rather than per attachment for a weapon-carried
    one) - pre-existing, a different rule, and 3.6 rewords Ravage ("Once per round, when it's this model's turn to
    strike in melee, ...", a book rule in 13 GDF + 9 AoF 3.6.0 books): handed to #434 with this item's carrier counter
    and participants as the template. (3) #432's D2 (impact hits carry no charge distance, so Guarded / Knightborn get
    nothing against them) is NOT changed: it needs the charge origin on the synthetic impact attack plus a "from
    weapons" flag and the owner's call - not a one-line pass-through. (4) The evaluator's Impact/Brace op lines still
    print twice per charge (the Ravage stage re-evaluates the hook, #422 D7, unchanged); the impact stage no longer
    narrates Ravage ops, so the Ravage op line now prints once. (5) User `.fdgarmy` files compiled before this change
    that bought a one-model Impact item keep the unit-wide copy they saved (it now rolls X x models) until re-saved
    from the Forge; no bundled list is affected. (6) AI pricing follow-ups -> #442; re-bake pins -> #438; the Ravage
    template -> #434 (all three noted there). (7) #422's D8 (this question) is answered by this item; its row in the
    plan still lists D8 for the owner.
- 2026-10-29: filed. See `WorkItems/422-impact-counter-brace.md` D8.

## Decisions

- **D1 - which models count (2026-10-30) - OWNER REVIEW.** ALL living models of the charging unit that carry the
  rule, wherever they stand. The 3.6 text ("When charging, before either unit attacks, roll X dice, unless this unit is
  Fatigued") has no reach condition, and every charging model moves into contact if it can. Alternative: only the
  models that end the charge in melee range (the #320 Limited-style subset, i.e. the models within 3" of the target
  at charge contact). It would make a strung-out unit roll fewer dice, but it reads a condition the text does not
  state, and the stage runs before pile-in, so the subset would be "within 3" when the charge lands" rather than the
  strike set. Switching is contained: `ChargerParticipant` would pass only those models and
  `Effect.ChargeImpactHits` would count carriers among them; `CombatMath` (which does not model positions) would need
  its own approximation (#442).
- **D2 - who carries a copy (2026-10-30).** Per attachment, as #422 D3 does for Brace: a unit-attached copy counts the
  living native models (joined hero excluded - the #006 merge relocated the hero's own rules and OPR heroes do not
  inherit the host's, the same accounting as `Condition.CountRuleCarriers`); a model-attached copy counts the living
  models carrying that attachment; a copy found on no carrier (a unit-wide grant) counts every living model, hero
  included (#183's grant rule); an argument-less copy (a literal X, the only shape a grant can carry, since grants
  carry no arguments) fires once per bearer whatever the attachment count, so it counts every carrier of the
  definition. Weapon-carried copies (none today; the stage passes no charger weapons) would count the models wielding
  them.
- **D3 - one-model Impact items become model grants (2026-10-30) - OWNER REVIEW.** #407's owner call routed only Tough
  through `ModelPersonalRuleNames` and left the rest as follow-up work. Per-model counting makes Impact need the same:
  without it, "Replace one CCW -> Rotor Blade Attack (Impact(3))" on 3 Hover Bikers folds unit-wide and would roll 9
  dice instead of the 3 it rolled before this item (2 bundled sites, 4 in 3.6.0). Impact and Heavy Impact are added;
  a model grant attaches to the unit's last model(s), which the automatic wound order reaches last, so the item's dice
  leave with that model. Guard: an item every model still holds after the purchase stays unit-wide (3.6's "Replace
  one Lance and Trample -> Trample (Impact(1))" hands the one model back the Trample its profile gave everyone; a grant
  there would have stripped the other four). Alternative: leave the compiler alone and accept the over-count on those
  items. Consequence: older user lists that saved the folded copy keep it until re-saved (Notes, deferred 5).
- **D4 - a narration filter instead of touching Ravage (2026-10-30).** Passing the charger's models reaches every
  Actor-seat charge-contact rule a model carries, including a joined hero's Ravage, whose op the impact stage would
  narrate ("Knights's Ravage rolled 12 dice at ..., each 6+ a direct wound") although it never rolls it here (and the
  Ravage stage, which has no models, never rolls it at all). Fixing Ravage's participants and counting is a different
  rule that 3.6 rewords (#434), so the impact stage narrates only its own ops via a new
  `EvaluateAllNamedLive(context, narrate, participants)` overload; one-shot grants, tracing and the returned queue are
  unchanged.
- **D5 - presentation (2026-10-30).** The sum goes on the stage's log line, the dice beat's `Context` (the #446
  precedent: "who / why these dice") and the calculator's hit chips, built by one formatter
  (`ResolveImpactHitsStage.DescribeDicePool`): sources merged per rule and per-model X (so a resumed game's
  one-model attachments read like a fresh one's), the authored "(X)" stripped from the attached name, cancels as
  " - N Brace". The beat moves to the Offense category (Ravage's pool already uses it). A charge whose dice Brace
  cancels completely now says so in one line instead of skipping silently. ASCII only.

## Ledger

| Facet | Code anchor | Test |
|-------|-------------|------|
| X per living carrier, unit-attached = native models | `Effect.ChargeImpactHits` (`CountCarriers`) | `ImpactPerModelRuleIntegrationTests.FiveModels_WithImpactOne_RollFiveDice`, `CasualtiesBeforeTheCharge_ReduceTheDice` |
| model-attached copies count their carriers | same; `ResolveImpactHitsStage.ChargerParticipant` | `ThreeModels_TwoCarryingImpactTwo_RollFourDice` |
| resume-safe (per-model attachments after a load) | `Matches` on the attachment, evaluator dedup | `ThreeModels_TwoCarryingImpactTwo_StillRollFourDiceAfterASaveAndLoad` |
| joined hero's own Impact adds; host copy skips the hero | `ChargerParticipant`; `IsJoinedHero` | `AJoinedHerosImpactThree_AddsToAFiveModelImpactOneUnit_EightDice`, `AJoinedHeroWithoutImpact_AddsNothing_...`, `AJoinedHerosImpact_InAUnitWithoutImpact_...`, `OnlyTheJoinedHeroLeft_WithoutImpact_RollsNoDice` |
| unit-wide grant counts every model | `RuleGrantQueries.UnitHasGrantedRule` branch | `AUnitWideGrant_CountsEveryLivingModel_TheJoinedHeroIncluded` |
| Heavy Impact per model, AP kept | same effect, `ImpactSink` AP max | `HeavyImpact_IsPerModelToo_AndKeepsItsAP` |
| Brace subtracts from the total, floor 0 | `ImpactSink`, stage skip | `Brace_SubtractsFromThePerModelTotal`, `Brace_FloorsAtZero_AndTheLogSaysWhy` |
| Fatigue gate still zeroes it | `CoreRuleCatalog.Impact` condition | `AFatiguedMultiModelCharger_StillRollsNoDice` |
| log line / dice beat / op text | `ResolveImpactHitsStage.Enter`, `DescribeDicePool`; `RuleOperation.ChargeImpactHits.Describe` | `TheDiceBeat_ShowsTheRealCount_AndThePoolAsItsContext`, `DescribeDicePool_...`, `RuleOperationDescribeTests` |
| impact stage narrates only impact ops | `RuleEvaluator.EvaluateAllNamedLive(context, narrate, ...)` | `AJoinedHerosRavage_IsNotNarratedByTheImpactStage` |
| AI parity | `CombatMath.EstimateMelee` (`ChargerParticipant`) | `CombatMathPinTests.EstimateMelee_ImpactDice_SumPerModel_AJoinedHerosImpactAdds` (+ 2 re-pinned) |
| calculator parity | `CombatCalculator.ResolveImpact` (`ChargeContactParticipants`, pool chip) | `CombatCalculatorTests.AMultiModelImpactUnit_RollsOneDiePerModel`, `AJoinedHerosImpact_AddsItsDiceToTheHosts` |
| one-model items -> model grants (D3) | `ListCompiler.ModelPersonalRuleNames`, `PlaceModelGrants` guard | `Compile_OneModelImpactItem_IsAModelGrant_NotUnitWide`, `ArmyLoad_OneModelImpactItem_RollsThatModelsDiceOnly`, `Compile_ReplacingOneProfileItemWithTheSameItem_KeepsItUnitWide` |
| shipped data | bundled `.fdgbook` files | app `PerModelImpactShippedDataTests` (3) |

## Outcome

Done 2026-10-30 on `feature/opr-3.6`. Impact(X) and Heavy Impact roll X dice per living model carrying them: the
charger's unit copy counts its native models, a joined hero's own Impact and per-model item grants count their
carriers, a unit-wide grant counts every model; Brace(X) subtracts from that total (floor 0) and the Fatigue gate still
zeroes it. The impact log line, the dice beat's context and the calculator's Impact row show the sum ("5 models x
Impact(1)"); CombatMath and the calculator share the stage's participants. One-model Impact items compile to model
grants. Engine `5fd93128`, `26d9e71e`. Open: Chris's GUI hand-verify; D1 and D3 for the owner; a joined hero's
Ravage (handed to #434).

## Rules for the agent (plan section 1, restated)

- Branch `feature/opr-3.6` in BOTH repos; never commit, merge or push `master`. Submodule-first cadence;
  subjects start with `447:`. 3.6 text from `~/Projects/GDF Armies/Rules 3.6.0/` only; no 3.5 fallbacks.
- Vertical slice (engine + presentation + AI parity + integration test); verify per CLAUDE.md plus
  `--rule-coverage` non-regression; never commit red; keep this file and the plan's status table current.
