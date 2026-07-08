# 167 — Human-testing workflow tools (umbrella)

**Status**: T1 scenario compiler + T4 seeded dice + lobby-skip launch DONE 2026-07-08 (engine
`0de69be`, superproject bump). T3 (ledger generator) and 4.4 (OPR reconciliation report) remain
open under this umbrella; GUI `--scenario` needs one hand-verify pass (headless verified end-to-end).
**Source**: `SpecialRulesAudit.md` §3.2 / §5 Phase 4. T2 (rule trace) shipped as #163.

## Outcome (this pass, 2026-07-08)
Shipped exactly per the locked design below:
- **Engine**: `GameModel/GameBootstrap.cs` (bootstrap extraction, FDGServer delegates),
  `SaveLoad/ScenarioFile.cs` + `SaveLoad/ScenarioCompiler.cs`, `GameSettings.DiceSeed` + seeded
  `RealisticDiceRoller` (unseeded path byte-identical). 7 new tests in `Tests/ScenarioCompilerTests.cs`
  including the end-to-end pin: compile -> save/load round-trip -> real FDGServer resume ctor ->
  AI-vs-AI to completion (~1s). Suite 1315/1315.
- **App**: `--make-scenario <json> <out.fdgsave>` tool mode; `--scenario <json|fdgsave>` direct
  launch in BOTH headless and GUI (slot 0 local human, other slots AI, no menu/lobby);
  `Cli/ScenarioLauncher.cs` (LoadStore/BuildResume, mirrors LaunchResume minus networking);
  `Rendering/GameGuiWiring.cs` (HandleLaunch body extracted; lobby + scenario share one wiring).
- **Examples**: `Scenarios/README.md` (schema + workflow), `Scenarios/example-shootout.json`,
  `Scenarios/armies/{Marksmen,Dummies}.fdgarmy` (signal-stat armies).
- **Verified**: engine 1315/1315; full build clean; `--make-scenario` exit 0; headless `--scenario`
  from both .fdgsave and .json plays rounds 1-4 to an objective victory, exit 0; standard headless
  smoke exit 0. GUI `--scenario` compiles and shares the lobby's tested wiring but needs a hand pass
  (listed under Awaiting verification). Sound cues are not wired in GUI scenario mode (launch happens
  before the audio device opens) - cosmetic, noted.

## Scope of this pass (agreed 2026-07-08: "implement the scenario tool; ideally skip the lobby")

1. **T1 — Scenario compiler**: `--make-scenario <scenario.json> <out.fdgsave>`. Compact JSON ->
   fully populated `GameDataStore` -> `.fdgsave` positioned at the start of the target player's
   activation (the audit's agreed anchor: resume re-enters at `DeterminePlayerTurnStage`, so the
   next decision is the active player picking a unit -> Choose Action).
2. **T4 — Seeded dice**: `GameSettings.DiceSeed` (nullable) -> seeded `RealisticDiceRoller`.
   Rides along because the scenario JSON spec (audit 4.2) includes `diceSeed` and it makes
   Realistic-mode scenarios repeatable.
3. **Lobby-skip launch**: `--scenario <file.json|file.fdgsave>` launches straight into the game
   (GUI or headless), slot 0 = local human, other slots AI. No main menu, no lobby re-crew.

## Design (locked 2026-07-08)

### Where things live
- **Engine** (submodule — authorized by the direct request; audit plan 4.2 explicitly reuses the
  FDGServer bootstrap, which is only reachable engine-side without duplication):
  - `GameModel/GameBootstrap.cs` (new): the world-building trio extracted verbatim from
    `FDGServer` — `AddTeams`, `BuildRuleResolver`, `CreateArmy` (with `AttachRulesFromArmyList`).
    `FDGServer` delegates to it; behavior-preserving refactor.
  - `SaveLoad/ScenarioFile.cs` (new): the scenario JSON DTO (STJ, `RuleJson.Options`).
  - `SaveLoad/ScenarioCompiler.cs` (new): `Compile(ScenarioFile, armies) -> GameDataStore` +
    `CompileToJson(...)` via `GameSaveSerializer.Save`. Never hand-writes save JSON — builds a
    real store so `DataReference`/`$type` integrity is free.
  - `GameSettings.DiceSeed` (int?, default null) + `RealisticDiceRoller(int? seed)` +
    `FDGServer.GetDiceRoller` threads it. Old saves lack the field -> null (compatible).
  - Tests: `Tests/ScenarioCompilerTests.cs` — compile -> `GameSaveSerializer.Load` round-trip
    assertions (progress cursor, positions, wounds, tokens, objectives, rules rehydrated), plus
    the end-to-end proof: compile -> load -> resume `FDGServer` with AI on all slots -> game
    runs to completion.
- **App**:
  - `Program.cs`: `--make-scenario <in.json> <out.fdgsave>` tool mode (compile + exit);
    `--scenario <file>` direct-launch mode (compiles first if given a `.json`).
  - Headless direct launch: `CliApp` resume path (LocalMessageBus, CLI resolvers slot 0, AI rest,
    resume `FDGServer` ctor).
  - GUI direct launch: extract `LobbyScreen.HandleLaunch`'s GUI wiring into a shared helper so
    the no-lobby path and the lobby path stay one implementation; `RealtimePresentationClock`.
  - `Scenarios/` directory: example scenario JSONs + signal-stat armies (`Scenarios/armies/`).

### Scenario JSON shape
```json
{
  "Name": "Rending shoot vs D5",
  "Round": 1,
  "ActivePlayer": 0,
  "Settings": { "Randomness": "Probabilistic", "DiceSeed": null },
  "Objectives": [[18.0, 24.0], [36.0, 24.0]],
  "Players": [
    {
      "Army": "armies/Shooters.fdgarmy",
      "Team": 0,
      "Units": [
        { "Unit": "Warriors", "Models": [[30,10],[31,10]], "WoundsDealt": [0,1],
          "Activated": false, "Tokens": [{ "Type": "Shaken", "Count": 1 }] }
      ]
    },
    { "Army": "armies/Defenders.fdgarmy" }
  ]
}
```
- Army paths resolve relative to the scenario file.
- `Unit` matches by name within that player's army **after** hero joins (a joined hero's models
  belong to the host unit's entry); `UnitIndex` is the disambiguator if names repeat.
- Units without placements auto-deploy in a row in their team's deployment band (mirrors the
  engine's single-turn tester) — only position the units that matter.
- `Objectives` optional; default = 3 markers across the table midline (deterministic, no RNG).
- `Team` optional (default = player index). `WoundsDealt` per model, subtracted from max after
  creation rules (so Tough is respected). `Tokens` unit-level, engine-known clear triggers by
  name (default ManualOnly).

### Cursor construction
One `GameProgressData`: `Stage = MainPhase`, `RoundCount` from JSON, `TeamActivateOrder` with the
active player's team FIRST, `CurrentTeamIndex = teams.Count - 1` (cursor semantics: `TryAdvance`
starts at index+1, so the next team checked is index 0 = the active team),
`CurrentPlayerIndexPerTeam` parked one before the target player, `UnactivatedUnits` = all living
units minus any marked `Activated`, `CurrentRoundTeamFinishOrder` empty, `Settings` from JSON.

### Deliberately deferred (recorded, not silently cut)
- Terrain in scenarios (open table only for now; `TerrainData` synthesis is a follow-up facet).
- Model-level tokens, embarked-at-start transports, mid-activation anchors (deeper than
  DeterminePlayerTurnStage would require extending the resume machinery).
- T3 ledger generator + 4.4 OPR reconciliation report (still open on this umbrella).
- Networked scenario launch (direct launch is local human + AI only; the lobby path still covers
  networked resume).

## Notes
- 2026-07-08: Design locked after mapping FDGServer bootstrap, GameProgressUtilities
  capture/restore, TeamPlayerAlternationCursor semantics, PlaceOneObjectiveStage, PlayerSlot
  (ctor self-registers PlayerSlotInfo — lobby re-crew data comes free from building slots at
  compile time). Implementation starting: engine slice first, then app slice.
