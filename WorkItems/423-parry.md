# 423 — Parry(X): new melee hit-ignore step with a defender pick

**Status**: signed off 2026-10-30 (plan Q11; Chris): always auto-resolve (worst hits first), NO new request or UI; Parry is a visible dice beat that names the parried groups; B1 budget. Build after #433.
**Related**: #415, #042 (rules architecture), #286 (assign-wounds dialog, nearest GUI pattern), #435 (AoF hand weapons carry Parry), #442
**Branch**: `feature/opr-3.6`. Model/effort: Fable 5.1 / xhigh. Read `docs/ResolverGuide.md` before touching resolvers.

## Goal

Core Rules v3.6.0, Parry(X): "When this unit takes hits in melee, roll X dice per model with this rule in it,
up to as many dice as hits taken. For each 6+ one hit is ignored (defender picks which)." In the books it is
a weapon rule (AoF Hand Weapons: 252 references across 35 books; GDF 12 references in the four Daemons
books), so "model with this rule" = a model carrying a Parry weapon. It is new in 3.6.

"Done" = after the attacker's hits are known and before saves, a Parry step rolls min(X x parry models, hits)
dice, and for each 6+ the defender removes one hit of their choice - the choice matters only when hits differ
(AP, Deadly, Rending-6 hits, Blast groups), so the engine offers a pick only when the incoming hits are not
all equivalent and otherwise auto-resolves; works for strike-backs and Counter strikes alike (any hits the
unit takes in melee); not against Impact hits? - Impact hits are hits taken in melee during a charge: the
text says "when this unit takes hits in melee", so Impact hits ARE parryable; record the ruling in Decisions.

## What changes (as first written; items 1-2's request / resolvers are DROPPED by the signed-off design)

1. Engine: a `ParryStage` (or a sink in the existing hit->save pipeline) between hit resolution and
   `RollSaves`, Subject seat, per-model count from weapon-scoped Parry carriers alive at that moment; a
   `ChooseParriedHitsRequest` (new `IStageTaskRequest`) only when a pick is meaningful; EOF default = ignore
   the most dangerous hits (highest AP, then Deadly). Catalog entry `CoreRuleCatalog.Parry` with
   `engineArgumentCount 1`, weapon AND unit scope.
2. Resolvers: CLI resolver (numbered list of hit groups, default = best); GUI resolver (a small panel listing
   hit groups with counts, click to allocate ignores, mirrors the assign-wounds dialog); AI resolver = the
   EOF default. Register in both `ResolverRegistryFactory` builds; add the request to the network message
   catalogue (serialization test like `RequestSystemTests`).
3. Tests: 5 models with Parry(1) vs 8 hits -> 5 dice; 2 models vs 1 hit -> 1 die; 6+ removes a hit; the pick
   request appears only for mixed hits; strike-back path; histogram mode run.
4. Presentation: a dice beat "Parry" with the ignored count; combat calculator (#442) expected-hit reduction.

## Signed-off design (2026-10-30, Chris)

- Fork A: A1, always auto-resolve, no new request type. `SuccessfulHitInfo` carries exactly (count, SaveModifier,
  Source), so hits in one swing differ in ONE dimension, the save threshold (the base-AP group plus the natural-6
  group(s) that `PerHitApSplitter` peels for Rending / Crack). Deadly, Blast, Lacerate, Bane are whole-weapon and
  never split a volley. The "best" pick is therefore a total order (highest save penalty first), auto-resolving is
  always correct, and the engine removes hits from the worst group first. Chris first asked for a pick dialog when
  groups differ, then withdrew it: the dialog's only purpose would have been visibility, and the dice beat below
  carries that. No CLI / GUI / AI resolver, no network catalogue entry, nothing in `docs/ResolverGuide.md`.
  - Chris's reconsider trigger: if a second per-hit dimension ever appears (a per-hit wound multiplier, a per-hit
    rule), stop and re-present the pick question; today none exists and none is planned in 3.6.
- VISIBILITY is the point (Chris): "a log message is usually ignored". The Parry roll is a DICE BEAT in the combat
  presentation, the same channel as the hit and save rolls: the dice shown with 6+ highlighted, then a per-group result
  line, worst group first, naming the rule that made the group worse - "Parry: ignored 2 Rending hits, 1 base hit",
  "Parry: 3 dice, one 6+ - ignored 1 Rending hit", "Parry: 3 dice, no 6+". The save-roll beat that follows reads fewer
  dice than hits and is labelled "(after Parry)" so the reduction is attributable. The log line carries the same
  per-group text. Acceptance for this facet: a GUI hand-verify by Chris that a Rending charge against a Hand Weapons
  unit shows which group Parry removed. ASCII only.
- Fork B: B1. One budget per enemy melee attack: X x living Parry carriers when the first hits land, spent across
  every swing of that attack, capped by the hits of each swing as they arrive ("up to as many dice as hits taken" is a
  cap, not a choice - no withholding). A strike-back is a new attack with its own budget. Counter on
  `ICombatActionContext`.
- Rulings: Impact hits ARE parryable (same save chain, "takes hits in melee"); Ravage deals wounds, not hits, so it is
  not; carriers = living models with at least one Parry weapon when the hits land (two Parry weapons on a model would
  use the highest X; none exists in 3.6.0); Parry dice are the defender's own roll, untouched by #418's fatigued-6
  gate; catalog entry `Parry` with `engineArgumentCount 1`, weapon scope plus unit scope. Carrier detection is NOT a
  name check like `LimitedRules`: ask the rule graph, consistent with #424's `WeaponRoleQueries` (Chris prefers
  asked-for capabilities over name checks).

## The fork (written up 2026-10-29; decided above)

Facts from the 3.6.0 snapshots and the engine survey that shape the choice:

- Parry is weapon-scoped everywhere: 254 weapon references in 35 AoF books and the 4 GDF Daemons books
  (Parry(1) x101, Parry(3) x151, Parry(6) x2). No unit-scoped Parry exists, no unit carries two Parry
  weapons, and no model mixes ratings, so "X per model with this rule" never has to add or choose.
- Inside one melee swing the hits differ in exactly one way: their save threshold. `PerHitApSplitter`
  peels the Rending/Crack-style natural-6 group off the base-AP group; Blast overflow and Furious-style
  extra hits share the base threshold; `SuccessfulHitInfo` carries nothing else per hit (`SaveModifier`
  + presentational `Source`). Melee weapons in 3.6.0 base units that create a second group: Rending
  (112). Bane (3.6: "ignores Regeneration ... re-roll unmodified Defense results of 6") is weapon-level,
  so it never splits a volley.
- Pipeline: `SwingMeleeWeaponStage` runs buildTargetList -> determineHitRollNeeded -> rollToHit ->
  determineSaveRollsNeeded -> rollToSave -> assignWounds -> applyWounds, once per weapon profile the
  attacker swings (`ChooseMeleeWeaponStage` / `DetermineCanKeepSwingingStage` loop). Impact hits enter the
  same save chain from `ResolveImpactHitsStage` (starts at determineSaveRollsNeeded). A Parry step sits
  between rollToHit and determineSaveRollsNeeded on the Subject seat and edits
  `RollToHitResults.SuccessfulHitList`.

### Fork A - the defender's pick

- **A1 (recommended): auto-resolve, no new request.** Because hits differ only by save threshold,
  ignoring the highest-threshold hit first is strictly better for the defender; there is no second
  dimension to trade off, so a pick dialog would have one right answer. The engine removes hits from the
  worst group first and the dice beat reads "Parry: 2 of 5 hits ignored (Rending hits first)". Zero new
  resolvers, no network catalogue entry, nothing for the AI to decide. Revisit the day a per-hit
  dimension other than save threshold exists (none is planned in 3.6).
- **A2: full defender pick** as this file's "What changes" first wrote it: a `ChooseParriedHitsRequest`,
  CLI + GUI + AI resolvers, serialization test, `docs/ResolverGuide.md` inventory row. About a day of
  work for a choice the player can never get wrong; keeps the rule's "(defender picks which)" visible as a
  UI moment.
- Recommendation: **A1**, with the ruling recorded in Decisions so the text's "defender picks" is
  accounted for.

### Fork B - the dice budget: per enemy attack or per swing

- **B1 (recommended): one budget per enemy melee attack.** X x (living Parry carriers when the first
  hits land), spent across every swing of that attack, capped by hits as they arrive ("up to as many dice
  as hits taken"). OPR resolves all of a unit's melee attacks together, so one Parry roll per attack is the
  table reading. A strike-back is a new attack (roles flip), so the original attacker's Parry gets its own
  budget then. State: a counter on `ICombatActionContext`, which already lives per attack.
- **B2: per swing.** Each weapon profile's volley gets a fresh min(X x carriers, hits) roll. No cross-swing
  state, but a two-profile attacker hands the defender twice the Parry dice.
- Recommendation: **B1**.

### Rulings to pin as D entries when built

- Impact hits ARE parryable ("takes hits in melee"; they come through the same save chain). Ravage deals
  wounds, not hits, so it is not.
- Carriers = living models with at least one Parry weapon at the moment the hits land; a model with two
  Parry weapons would use the highest X (no such unit exists in 3.6.0).
- Parry dice are the defender's own roll, not hit rolls, so #418's fatigued-6 gate does not touch them.
- Catalog entry `Parry` with `engineArgumentCount 1`, weapon scope (plus unit scope for generality);
  carrier detection by name like `LimitedRules` so an army's embedded copy is recognised.
- Combat Calculator and Tactician: expected-hit reduction = sum over hits of P(6+) with the min() cap;
  handed to #442 with the integration test's numbers.

### Hand-verify (Chris, GUI)

An AoF infantry unit with Hand Weapons (Parry(1)) is charged; the dice beat shows the Parry roll and the
ignored hits, and the save roll shows fewer dice than hits.

## Acceptance

Tests green; network round-trip test for the new request green; hand-verify (Chris): an AoF infantry unit
with hand weapons parries a charge in the GUI.

## Code anchors (survey 2026-10-29)

- Melee pipeline: `MeleeStage` -> `RollToHitStage` -> `RollSaves`/`ApplyWoundsStage`; `WoundIgnoreSink`
  (nearest existing "ignore" sink, but post-save); `HitInjectionSink`; `docs/ResolverGuide.md` resolver
  inventory. See the appended survey rows below for exact lines.

### Survey rows (code survey 2026-10-29)

- Parry is absent everywhere (engine, supplements, books). Pipeline order: see #422's survey rows; hits are
  grouped by AP in `MainUnitActionStage/PerHitApSplitter.cs:47-51` (the natural granularity for "defender
  picks which"). Sinks to model on: `Rules/Dispatch/WoundIgnoreSink.cs` (post-save ignore), `HitInjectionSink.cs`,
  `HitMultiplierSink.cs`. Request/resolver pattern: `docs/ResolverGuide.md`, `ResolverRegistryFactory`,
  `Tests/RequestSystemTests.cs` (serialization).

## Notes

- 2026-10-30 (from #424): the precedent you were pointed at exists now - `Rules/Dispatch/WeaponRoleQueries.cs` asks a
  weapon's own rules for `Effect.EnableMeleeUse` / `EnableRangedUse` at `Lifecycle_OnCapabilityQuery` by effect type
  (the `StrafingRules` structural read), never by the name "Assault". Parry counts "models with this rule" the same way:
  by the attachment's effect, not `LimitedRules`'s name check. One Assault fact for Parry: the parrying unit's melee
  weapons now include Assault pistols (`GetMeleeWeapons`), so a Parry weapon rule on a pistol would count its carrier.
- 2026-10-30: Q11 signed off (Chris): always auto-resolve with a group-naming dice beat, no UI (a pick dialog was
  considered and withdrawn the same day), see "Signed-off design". Queued after #433.
- 2026-10-30 (from #442): the AI / Combat Calculator half of Parry waits on this fork (Q11); nothing was built in
  #442. Once the stage lands: (1) `CombatMath.EstimateVolley` must mirror the new hit-ignore step - expected melee
  hits less Parry's 6+ fraction - by folding the stage's own operation through the evaluator (as Impact / Brace /
  Counter already are), never by rule name; pin it against the real stage in `CombatMathPinTests` (the harness there
  runs DetermineHitRoll -> AssignWounds); (2) the Combat Calculator runs the real stages, so it prices Parry for free -
  add a `CombatCalculatorTests` case; (3) under A1 (auto-resolve, the default) there is no new request; under A2 the
  AI needs a resolver for the defender's pick (Tactician: the hits whose ignore saves the most expected wounds; solo:
  first option) with an AI legality pin (#066 pattern, see `Tests/AiTriggeredRequestLegalityTests.cs`); (4) the
  Tactician's melee threat terms read `CombatMath`, so they follow with no extra change.
- 2026-10-29: fork options written up ("The fork" above); nothing built until sign-off (plan Q11).
- 2026-10-29: filed with the plan.

## Decisions

- D1 (signed off by Chris, 2026-10-30): A1 always auto-resolve (worst hits first), no new request or UI, Parry as a
  visible dice beat naming the parried groups + B1; Impact parryable.

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
