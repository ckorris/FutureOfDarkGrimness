# 446 — Units that activate in Dangerous terrain take the test

**Status**: todo (filed 2026-10-29 from #420's D5; Chris agreed via plan Q10 that it gets its own number)
**Related**: #420 (dice count; landed), #415, #002 (terrain), #426 (deployment placements into terrain?)
**Branch**: `feature/opr-3.6`. Model/effort: Opus 5.5 / medium. Schedule after phase 2.

## Goal

Core Rules v3.6.0, Dangerous Terrain: "Units with models moving across Dangerous terrain, OR THAT ACTIVATE
IN IT, must roll as many dice as the number of wounds it would take to fully destroy them, and for each roll
of 1 the unit takes one wound." The Beginner's Guide: "If any model in a unit moves into or through Dangerous
terrain, or activates in it, then the entire unit must take a Dangerous Terrain Test." Today only a move
whose path crosses Dangerous terrain (and the "counts as Dangerous" debuff) rolls; a unit that Holds, or
shoots in place, while any model stands in Dangerous terrain never tests. The clause was in 3.5 too, so this
is a pre-existing gap surfaced by #420.

"Done" = at activation, if any living model of the activated unit is inside Dangerous terrain, the unit takes
one Dangerous Terrain Test (the #420 pool) once, before its action; a unit that then also moves through
Dangerous terrain does not test twice for the same activation (decide: one test per activation, or one for
activating-in plus one for the move? Read the text: "moving across ... or that activate in it" reads as one
test per activation on either trigger - record the ruling); Flying / Strider scopes apply as they do for the
move test; a unit deployed into Dangerous terrain (Ambush/Scout/teleport) is covered by its first activation.

## What changes

1. Engine: a start-of-activation hook (the #408 activation-start path or `ChooseActionStage` entry) that checks
   model footprints against Dangerous terrain and runs `DangerousTerrainBatch` once; a per-activation marker so
   the movement test is skipped if the activation test already fired (or vice versa, per the ruling).
2. Presentation: the dice row and log line from #420 ("dangerous terrain test - N dice (wounds to destroy)")
   with the trigger named ("activated in").
3. Tests: Hold in Dangerous terrain tests once; move out of it tests once (not twice); a unit adjacent but not
   inside does not test; Flying ignores; a seeded headless game with a unit parked in Dangerous terrain.

## Code anchors

- `MovementStage/MovementExecutor.cs` (`RollDangerousTerrain`, `RollForcedDangerousTerrain`), `Rules/Dispatch/
  DangerousTerrainBatch.cs` (#420's shared roll), `ApplyNonMovementTerrainEffectsStage.cs` (rolls for the move's
  path only), `IUnitExtensions.GetWoundsToDestroy()`, `TerrainData.cs:78-82` (point-in-terrain), `MovementRuleQueries.
  IgnoresAllTerrain`.

## Notes

- 2026-10-29: filed. See `WorkItems/420-dangerous-terrain-dice.md` D5 for the discovery.

## Decisions

(none yet)

## Outcome

(open)

## Rules for the agent (plan section 1, restated)

- Branch `feature/opr-3.6` in BOTH repos; never commit, merge or push `master`. Submodule-first cadence;
  subjects start with `446:`. 3.6 text from `~/Projects/GDF Armies/Rules 3.6.0/` only; no 3.5 fallbacks.
- Vertical slice (engine + CLI/GUI presentation + AI legality + integration test); verify per CLAUDE.md plus
  `--rule-coverage` non-regression; never commit red; keep this file and the plan's status table current.
