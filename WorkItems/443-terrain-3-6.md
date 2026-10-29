# 443 — Terrain 3.6: Dense, Unstable, Steep, Climbable (Elevated renamed) - design fork, blocked on Q3

**Status**: blocked (plan Q3: in this release or deferred? Default: deferred, nothing built)
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

## The fork (write up before building)

- Option A (minimal): rename Elevated -> Climbable in the enum/layout files/UI with a migration for saved
  layouts; add Dense as a LoS-only type (into/out-of but not through - the LoS code already distinguishes
  "ends inside" for cover); defer Unstable and Steep.
- Option B (full): all four, with Unstable needing per-model move-distance tracking through a piece and Steep
  needing move direction relative to the piece's footprint; new palette entries and scenario schema.
- Option C: defer all of it to after the 3.6 release (plan default) and only reserve the names.

Present A/B/C with the effort and the files touched (terrain enum, `TerrainLayoutFile`, scenario schema,
renderer textures, movement validator, LoS) and stop for Chris's answer to Q3.

## Acceptance (after sign-off)

Per the chosen option; every new type has a movement/LoS integration test and a palette entry.

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
