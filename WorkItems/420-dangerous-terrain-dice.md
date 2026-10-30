# 420 — Dangerous terrain 3.6: roll as many dice as wounds needed to destroy the unit

**Status**: done 2026-10-29 (engine `217b4d68`; superproject commit `420: ...` bumps it with the app text/doc sweep). GUI hand-verify asked on the movement-panel hint line (no unit test for ImGui text); open question for Chris on D5 ("or that activate in it" not implemented).
**Related**: #415, #165 (dangerous-terrain deaths and the destruction notifier), #035/#169 (transport spillout uses the same test), #299 (batched wounds presentation)
**Branch**: `feature/opr-3.6`. Model/effort: Sonnet 5.5 / medium.

## Goal

Core Rules v3.6.0: "Units with models moving across Dangerous terrain, or that activate in it, must roll as
many dice as the number of wounds it would take to fully destroy them, and for each roll of 1 the unit takes
one wound." The 3.6 Beginner's Guide repeats it as the Dangerous Terrain Test ("roll as many dice as the
number of wounds it would take to fully destroy the unit"). The dice count is therefore the unit's total
remaining wounds (sum over models of remaining Tough, 1 for a plain model), not one die per model.

"Done" = `ApplyDangerousTerrainEffects` (and the transport-destruction spillout test, which the Transport rule
defines as "a Dangerous terrain test") roll that many dice; the Dangerous Terrain Debuff ability
("counts as being in Dangerous terrain once") inherits it; the No Retreat rule already uses the same
"as many dice as the number of wounds it would take to fully destroy it" phrasing - share the helper.

## What changes

1. Engine: one helper `WoundsToDestroy(unit)` used by the dangerous-terrain test, the spillout test and No
   Retreat; dice count per the helper; the per-1 wound application unchanged.
2. Tests: a 5-model unit of Tough(1) rolls 5 dice (unchanged); a Tough(6) hero with 2 wounds taken rolls 4;
   a unit of 3x Tough(3) rolls 9; transport spillout and No Retreat use the helper.
3. Presentation: the batched dice row (#299) shows the new count; CLI text says "N dice (wounds to destroy)".

## Acceptance

Tests green; headless smoke exit 0; `docs/EngineNotes.md` line on dangerous terrain updated.

## Code anchors (survey 2026-10-29)

- `ApplyDangerousTerrainEffects` (movement), `TransportUtilities.ApplySpilloutEffects`, No Retreat definition
  in `GdfRuleSupplement.json`. See the appended survey rows below for exact lines.

### Survey rows (code survey 2026-10-29)

Findings that shape the item: today one d6 per TESTING MODEL, each 1 wounding that model; 3.6 is one die per
remaining wound of the unit with each 1 = one wound to the unit (normal wound assignment). The transport
spillout copy and the "counts as Dangerous" debuff share the shape.

- `MovementStage/MovementExecutor.cs:65-92` `RollDangerousTerrain` (testers = models whose path crosses;
  "counts as" -> all), `:108-115` `RollForcedDangerousTerrain`, `:122-154` `RollBatch` (:127 `Roll(6,
  testers.Count)`; :146-148 each 1 -> one wound to one model; :135-141 probabilistic spread), `:207`
  `ResolveDangerousTerrain`. Stale doc mentions of `ApplyDangerousTerrainEffects` at `MovementExecutor.cs:15`,
  `TransportUtilities.cs:259`.
- Callers: `ApplyNonMovementTerrainEffectsStage.cs:40`, `StateMachine/GameOperationServices.cs:128` (debuff),
  `MovementExecutor` Commit :186-190. Spillout copy `Rules/Dispatch/TransportUtilities.cs:269-290`.
- Tests: `DangerousTerrainWoundTests.cs`, `DangerousTerrainMoraleTests.cs`, `TerrainDebuffRuleIntegrationTests.cs`,
  `TransportUtilitiesTests.cs` (Spillout_DangerousTest_DealsOneWoundPerModel_OnRollOfOne), `TransportSpilloutTests.cs:325`.
- Docs: `docs/EngineNotes.md:63`.

## Notes

- 2026-10-29: **Landed.**
  - **Helper.** `IUnitExtensions.GetWoundsToDestroy(this IUnit)` (`GameObjects/Core/IUnit.cs`): sum over LIVING
    models of `max(0, TotalWounds - WoundsDealt)`, floored with the float-noise epsilon. A joined hero is in
    `unit.Models`, so its wounds count; a corpse with overkill contributes nothing.
  - **One roll for all three tests.** New `Rules/Dispatch/DangerousTerrainBatch.Roll(unit, roller, randomness)`
    rolls `Roll(6, GetWoundsToDestroy())` once and returns the pending wounds. `MovementExecutor.RollBatch`
    (movement crossing, counts-as grant, forced Debuff via `RollForcedDangerousTerrain`) and
    `TransportUtilities.ApplySpilloutEffects` both call it and only re-wrap the result (`PendingModelWound` /
    `SpilloutWound`). The result structs' `ModelCount` became `DiceCount`. No Retreat's
    `RollSelfWoundPool` now takes its pool from the same `GetWoundsToDestroy()` (was `(int)RemainingWounds`).
  - **Who tests.** Any one model whose path crosses Dangerous terrain (or any moving model under the "counts
    as Dangerous" grant) puts the whole unit to the test, and the unit rolls the full pool once. A grant with
    nobody moving still tests nothing.
  - **Wounds go to the unit.** Realistic: `AssignWoundsResults.AutoAssign(unit, wounds)` (new static; the
    normal assignment run to completion with `AutoFill`, so already-wounded first #023, mid-fill model first
    #024, rank and file in unit order, joined hero last #006 - no copy of those rules). The old
    "one wound apiece to the first N testers, capped at the tester count" is gone (it would have dropped wounds
    once the pool exceeded the model count). `AssignWoundsResults` gained `UnitData` constructor overloads (the
    binding ones delegate) so a Rules-layer caller holding an `IUnit` can use it.
  - **Presentation / text.** The batched `DiceRolledBeat` now carries the larger roll and a context line
    `"<unit>: 9 dice (wounds to destroy)"` (`MovementExecutor.DescribeDice`, shared with the spillout beat); the
    CLI/log line reads `"<unit>: dangerous terrain test - 9 dice (wounds to destroy), 1.5 wound(s) dealt."`.
    App-side: `TerrainEffectText` (terrain picker) and the movement panel hint in `GuiDefineMovementResolver`
    ("the unit rolls N dice on commit (wounds to destroy it), each 1 deals 1 wound"). Stale per-model wording
    fixed in doc comments (`ECountAsTerrain`, `RuleOperation`, `Effect`, `IOperationServices`), `README-WIP.md`,
    `docs/EngineNotes.md` (the `ApplyNonMovementTerrainEffectsStage` bullet), the `OprBookImporter` synthesized
    counts-as rule, and the two Dangerous Terrain Debuff descriptions in `GdfRuleSupplement.json`.
  - **Tests.** New `DangerousTerrainDiceRuleIntegrationTests` (helper: 5xTough(1)=5, Tough(6) hero with 2 taken=4,
    3xTough(3)=9, corpse/overkill, joined hero, fractional floor; movement: one crosser rolls the whole pool,
    hero 4, 3xTough(3) 9, no crosser, counts-as with/without movement; wound assignment: fills the first model,
    already-wounded first, hero last, all-ones wipes the unit, safe roll; probabilistic even and uneven spread;
    forced test and spillout dice counts and unit-level wounds; dice row/log text). Added `FixedOnesDiceRoller`
    (exactly n of the dice are 1s) for tests that used an all-1s roller to mean "one wound". Re-pinned:
    `DangerousTerrainMoraleTests` x2 (4-model unit now rolls 4 dice; the all-1s roller would have killed the
    whole unit), `TransportSurvivingDangerousTerrain_KeepsItsCargo` (Tough(3) transport rolls 3 dice; one 1 not
    three), `Spillout_DangerousTest_...` (renamed; 6 dice, 6 wounds), `TerrainDebuffRuleIntegrationTests`
    (`ModelCount` -> `DiceCount`); added a No Retreat test that a wounded Tough unit's pool is the shared count.
    Engine suite 3417 -> 3443 passed (1 skipped as before); `FdgRaylib.Tests` 3063 green.
  - **Verification.** Engine suite green; full `dotnet build` clean (0 errors, no new warnings); headless smoke
    `printf "2\n2\n" | ... --headless` exit 0; `--rule-coverage FdgRaylib/Assets/Books` Dead: 0.
  - **Deferred, named.** (1) The "or that activate in it" clause - see D5. (2) The baked `.fdgbook` copies of
    the Dangerous Terrain Debuff descriptions stay stale until the #438 re-bake picks up the supplement
    (note filed there). (3) No AI change: the Tactician models no dangerous-terrain cost today.
- 2026-10-29: filed with the plan.

## Decisions

- **D1 - how a 1 is assigned, and the joined hero (2026-10-29).** The item says "each 1 = one wound to the unit,
  normal wound assignment" but there is no owner-choice prompt for dangerous terrain, and the roll is made
  early and landed after the move animation (#299), so an `AssignWoundsRequest` mid-flight would need a new
  interaction in the CLI, GUI and both AIs. Chosen: the engine's own mandatory order, run to completion with no
  choice (`AssignWoundsResults.AutoAssign`). That answers the hero case without a copy of the rule: a joined
  hero takes wounds last, even if already wounded, and an already-wounded model is finished before a fresh one.
  Alternative if the owner wants it: let the unit's owner pick which Tough model absorbs each wound (only
  matters for mixed or Tough rank and file; 1-wound models make no difference), at the cost of the early-roll /
  late-land restructuring above. Recommendation: keep auto; it is what Hazardous and No Retreat do.
- **D2 - No Retreat / Hazardous allocation changed too (2026-10-29).** `CasualtyPresentation.ApplyUnitWounds`
  used a private front-to-back fill that ignored the already-wounded-first and hero-last rules. Leaving it would
  have kept two allocators for "the unit takes N wounds", so it now plans through the same `AutoAssign`. Visible
  difference only when a model is already hurt and not first in the list, or a hero is joined and wounded;
  existing No Retreat / Hazardous tests are unchanged and green.
- **D3 - probabilistic spread (2026-10-29).** "Keep spreading the expected wounds" is kept, not routed through
  `AutoFill`: the fractional expected wounds are split across the living models in proportion to each model's
  remaining wounds (for 1-wound models this is exactly the old even split; the share can never exceed what a
  model holds because the expected 1s <= dice <= the unit's wounds). Filling the first model with the fractional
  total would be the other reading; it is the same expected total, concentrated.
- **D4 - the dice count is floored (2026-10-29).** `GetWoundsToDestroy` floors (with a 1e-4 epsilon so
  8.99999 does not lose a die). That is No Retreat's and Reanimation's existing rule, so the helper is shared
  without changing them, but it means a unit with under one whole wound left (reachable only under the
  probabilistic roller) rolls no dice at all. Ceil would make that unit roll one die; not done because it would
  change No Retreat and Reanimation as well.
- **D5 - "or that activate in it" is NOT implemented (2026-10-29) - OWNER QUESTION.** The 3.6 text (and the
  Beginner's Guide: "If any model in a unit moves into or through Dangerous terrain, or activates in it, then the
  entire unit must take a Dangerous Terrain Test") also tests a unit that activates standing in Dangerous
  terrain. The engine has only ever tested on a move crossing, a counts-as grant, a forced Debuff test and
  transport spillout; a unit that Holds (or shoots without moving) inside a Dangerous piece takes no test. That
  clause is outside this item's filed scope (the plan's C4 row is the dice count only), so it is recorded here
  rather than built. Needs a rules call: which actions count as "activating in it" (Hold, Advance, Rush,
  Charge - and does a unit that starts inside and moves out test twice?). Candidate home: a new item in the
  OPR 3.6 block (`git fetch origin` before taking the number).

## Outcome

Dangerous terrain, the Dangerous Terrain Debuff (both arms), the transport-destruction spillout and No Retreat
now share one pool size, `IUnit.GetWoundsToDestroy()`, and the first three share one roll,
`DangerousTerrainBatch`. Done except the "activates in it" clause (D5). Engine `217b4d68`.

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
