# 443 — Terrain 3.6: Dense (build), Unstable (optional); Steep and Climbable out of scope

**Status**: scoped by Chris 2026-11-02 (post-v0.4.0): build **Dense**; **Unstable** optional, after Dense; **Steep** and **Climbable** are out of scope for good (they model elevation, which this game does not have). Not started.
**Related**: #415, #002 (terrain placement), #167 (scenario terrain), #268/#301 (terrain palette/modes), #201 (cover rules), #253
**Branch**: `feature/opr-3.6`. Model/effort: Opus 5.5 / high (after sign-off).

## Goal

Beginner's Guide v3.6.0 (p.11-12) terrain types, verbatim:
- Dense: "Terrain features that are densely pack[ed] and that would be Difficult to see through count as dense
  terrain. Units may draw line of sight into and out of dense terrain, but not through it to targets on the
  other side."
- Unstable: "If any model in a unit moves over 6" when moving into or through unstable terrain, then it counts
  as Dangerous terrain."
- Steep: "Units moving onto steep terrain count it as being in Difficult terrain, but units moving off it count
  it as open terrain."
- Climbable (was Elevated): "Terrain features that are over 3" tall count as Impassable terrain, but any terrain
  piece that is up to 3" tall may be climbed as part of a unit's move (units may not end their move
  mid-climb). If not all models in a unit can fit at the top, then a coherency of 3" is allowed to models at
  different elevations. Note that short terrain up to 1" tall may be ignored for the purposes of movement".
- Unchanged: Open, Impassable, Sight Blocking, Cover, Difficult, Dangerous. Guideline pairings: Forests =
  Difficult + Cover + Dense; Hills = Cover + Steep; Rivers = Difficult / Unstable; Ruins = Cover + Unstable;
  Walls = Climbable; Buildings = Impassable + Sight Blocking.

## Decision (Chris, 2026-11-02) - the fork is closed

The game has no model elevation: terrain is a flat footprint with a height used only for line of sight. Judged per
type:

- **Climbable** (3.6's rename of Elevated): going up and over pieces up to 3" tall with a looser coherency at
  different heights. Nothing to climb on a flat table; pieces over 3" already play as Impassable. **Out of scope.**
  Do NOT rename the `Elevated` flag: saves and scenarios store terrain types by flag name, nothing reads
  `Elevated`, so a rename only breaks old files for no gain. Retire it quietly if it ever gets in the way.
- **Steep** (hills: Difficult onto, open off): needs "onto" vs "off" a slope, i.e. elevation. **Out of scope.**
- **Unstable** (rivers, ruins: a model moving over 6" into or through the piece counts it as Dangerous): a flat
  per-model move-distance check the movement validator can do. **Optional, after Dense**, if a playtest misses it.
- **Dense** (forests: line of sight into and out of the piece, never through it to the far side): not a 3D rule,
  and the one that changes play for everyone - today a forest is Cover + Difficult and units shoot straight
  through woods. **Build this.** The LoS code already distinguishes a target that ends inside a piece
  (`LineOfSightUtilities`), so the new case is "both ends outside, the line crosses the piece" -> blocked.

Scope of the Dense slice: `ETerrainType.Dense` flag; LoS rule + shooting preview parity (the forecast and the
canvas hover must agree with the resolution); palette entry and `TerrainEffectText`; `Scenarios/README.md`
schema line; `DefaultTerrainPool` forests gain Dense (3.6 guideline: Forests = Difficult + Cover + Dense);
movement untouched. Integration tests: into, out of, through, and a piece that only touches the line's edge.
AI: the Tactician's shot pricing reads the same LoS seam, so it follows for free - confirm with one test.

## Acceptance

Dense: a unit inside a Dense forest can be shot and can shoot out; two units on opposite sides of it cannot see each other; the shooting forecast, the canvas hover and the resolution agree; the palette offers it and the default forests carry it. Unstable (if built): a model that moves over 6" into or through the piece triggers the Dangerous test, one that moves 6" or less does not.

## Code anchors (survey 2026-10-29)

- Terrain enum + `TerrainLayoutFile`, LoS evaluation, movement validator's difficult/dangerous handling,
  `Scenarios/README.md` schema. See the appended survey rows below.

### Survey rows (code survey 2026-10-29)

Findings that shape the fork: `Elevated` is declared but no rule reads it ("Elevated - no rules effect yet.");
saves and scenarios store terrain types by FLAG NAME (e.g. `"TerrainType":"Impassible, Blocking"`), so a rename
needs a JSON alias or it breaks every save/scenario that uses it. Climbable, Dense, Unstable, Steep appear nowhere.

- `TableState/Zones/Terrain/ETerrainType.cs:5-14` ([Flags] None, Cover, Impassible, Difficult, Dangerous, Blocking,
  Elevated 1<<5); `DefaultTerrainPool.cs:222-223`, `FdgRaylib/Rendering/TerrainEffectText.cs:34-37`,
  `Rendering/Resolvers/TerrainTypeFilter.cs:19, :32-37`; `Rules/Definitions/ECountAsTerrain.cs:9-17`,
  `ETerrainIgnoreScope.cs:10-17`.
- Layouts: `SaveLoad/TerrainLayoutFile.cs:7-10` (`.fdgterrain`, none in repo), `TerrainLayoutLoader.cs:22-34`,
  `FdgRaylib/Cli/TerrainLoader.cs:17-54`, `DefaultTerrainPool.cs:29-30, :53, :86-110, :225+`; scenario parse
  `SaveLoad/ScenarioCompiler.cs:510-537`; `Scenarios/README.md:38-39` (lists Elevated).
- Engine use: `MovementUtilities.cs:616-617, :788, :798, :844, :1013`; `MovementExecutor.cs:75`; `PileInUtilities.cs:91`;
  `LineOfSightUtilities.cs:87, :134, :167`; `TerrainData.cs:78-82`; `PlacementUtilities.cs:112, :123, :147`;
  `PlaceOneObjectiveStage.cs:93`; `GameWideConstants.cs:34` (Difficult cap); `RuleFireLint.cs:58`; AI
  `CombatMath.cs:402`, `GridPathfinder`, `RouteMetrics`, `MovementPlanner`, `MacroActionGenerator`.
- UI: `TerrainColors.cs:13-49`, `TerrainPatternRenderer.cs:33-42`, `GuiPlaceOneTerrainResolver.cs`,
  `TableTooltipOverlay.cs`, `TacticalOverlay/FieldHarness.cs`; test `DefaultTerrainPoolTests.cs`.

## Notes

- 2026-10-30 (from #442): the Tactician now charges a candidate that ENDS in Dangerous terrain the next activation's
  test (`TacticalAnalysis.EndsInDangerousTerrain`, the engine's swept-base overlap; WoundsToDestroy / 6 as a value
  fraction, Flying exempt). Path cost through Dangerous pieces is still unpriced. When #443's new terrain types land
  (Dense, Unstable, Steep, Climbable), check whether any carries the Dangerous flag or a test of its own the planner
  should see - the term keys on `ETerrainType.Dangerous` only.
- 2026-10-30 (from #446): Dangerous terrain now also tests a unit that ACTIVATES in it
  (`ActivatedInDangerousTerrainStage` -> `MovementExecutor.RollActivationDangerousTerrain`, any living model's base
  overlapping a piece flagged `ETerrainType.Dangerous`, via `MovementUtilities.IsModelInDangerousTerrain`), with one
  test per activation shared with the move trigger (`Rules/Dispatch/DangerousTerrainTestedThisActivation`, read and
  set by `MovementExecutor.RollDangerousTerrain` too). For this item: (1) Unstable ("if any model in a unit moves
  over 6" when moving into or through unstable terrain, then it counts as Dangerous terrain") is a MOVE trigger only -
  do NOT model it by giving the piece the `Dangerous` flag, or every unit standing in it would take the activation
  test; route it through the move path (the `countsAsInDangerousTerrain` argument of `RollDangerousTerrain`, which
  already obeys the one-test-per-activation marker). (2) Steep's "count it as being in Difficult terrain" moving onto
  it is likewise move-only. (3) Any new "in terrain" footprint test should reuse `IsModelInDangerousTerrain`'s shape
  (static swept-base pose, base overlap counts - #446 D3) so standing-in and crossing agree.
- 2026-10-29: filed with the plan; blocked on Q3.

## Decisions

(fork pending)

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
