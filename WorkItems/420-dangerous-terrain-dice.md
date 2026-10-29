# 420 — Dangerous terrain 3.6: roll as many dice as wounds needed to destroy the unit

**Status**: todo
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
