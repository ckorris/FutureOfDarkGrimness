# 243 — Objective placement mode (Auto-Placed / Player-Placed)

**Status**: done (GUI hand-verified 2026-08-23; archived)
**Related**: mirrors #002 terrain-placement modes; consolidates the #001 debug auto-placer onto the DerpBot algorithm

## Goal
Give objective placement a lobby-selectable mode, the way terrain has one, replacing the single
`GameSettings.AutoPlaceObjectivesDebug` boolean. Two modes: **Auto-Placed** (engine places every
marker, no interaction; default) and **Player-Placed** (alternating player/AI placement). Auto-Placed
uses the solo-rules AI's balanced algorithm, extracted to a shared helper so the two are identical.
In Release builds the debug-shortcut options (objective Auto-Placed, terrain AutoFromLayout) sort to
the *end* of their lobby dropdowns instead of the front; in Debug builds they stay first for fast
iteration. The enum is not build-conditional — only the lobby display order is — so saves, scenarios,
and the network wire are identical across build types.

## Notes
- 2026-07-18: Follow-up per user: in Auto-Placed mode the objective roll-off is now skipped entirely
  (no dice roll-off, no "places first" announcement beat) - `RollForFirstObjectivePlacementStage`
  short-circuits on `AutoPlaced`, setting team declaration order so the placement cursor still runs.
  Mirrors how `RollForFirstTerrainPlacementStage` skips for auto terrain. The per-marker "Team X will
  place objective N" lines are plain `context.Log` info (not beats), left as-is. Re-verified green.
- 2026-07-18: Implemented and verified. Engine 1671 tests green (incl. determinism/benchmark, which
  pins that the AI-resolver refactor preserved RNG draw order), app 376 green, full Debug + Release
  builds clean, headless smoke exits 0 with balanced auto-placement (obj Z mirrors across table center).

## Decisions
- **Replaced the bool outright** (user sign-off) rather than layering the enum on top: one source of
  truth, mirrors terrain. Old saves' `AutoPlaceObjectivesDebug` is ignored on load; the enum defaults
  to `AutoPlaced` (same effective default the CLI relied on).
- **Shared `ObjectiveAutoPlacer`** (engine, `StateMachine/MapSetupStage/PlaceObjectivesStage/`) holds
  the balanced placement (random X, Z mirrored across center, first validator-legal spot nearest the
  target). Both `PlaceOneObjectiveStage` (Auto-Placed path) and `AiPlaceObjectiveResolver` call it,
  passing their own seeded `Random`. The old 3"-grid+shuffle debug placer in `PlaceOneObjectiveStage`
  is gone. The extraction is faithful to the resolver's `_rng` draw order, so #193 determinism holds
  (benchmark tests confirm). An `ObjectiveAutoPlacerTests` pins legality + helper==resolver-for-same-seed.
- **Build-conditional ordering is app-only**, via a `debugLast` param on `LobbyScreen.DrawEnumCombo`
  (+ a `#if DEBUG` `OrderComboValues`). Applied to both the objective combo (AutoPlaced) and the
  terrain combo (AutoFromLayout). `displayName` param gives the objective combo friendly
  "Auto-Placed"/"Player-Placed" labels; terrain keeps its raw enum names (unchanged).

## Outcome
Implemented; suite green + headless-verified, held for GUI hand-verify. Remaining checks in the
running app: (1) lobby shows an "Objective Mode" combo reading "Auto-Placed"/"Player-Placed", host-only;
(2) selecting Player-Placed makes the human place markers by click in a GUI game (Auto-Placed places
them all instantly); (3) the mode syncs host->client; (4) in a Release build the debug options
(objective Auto-Placed, terrain AutoFromLayout) appear LAST in their dropdowns, first in a Debug build.

Engine: `EObjectivePlacementMode` + `GameSettings.ObjectivePlacementMode` (default AutoPlaced),
shared `ObjectiveAutoPlacer`, mode branch in `PlaceOneObjectiveStage`, `AiPlaceObjectiveResolver`
delegates to the helper, lobby view-model plumbing (interface/host/client) mirroring terrain, 3 tests
migrated off the old bool + 1 new test file. App: lobby "Objective Mode" combo, `DrawEnumCombo`
debug-last ordering + friendly labels, `CliApp` sets the mode instead of the bool. Docs: EngineNotes
updated. Nothing deferred.
