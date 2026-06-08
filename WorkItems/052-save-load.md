# 052 — Save / Load a game in progress

**Status**: todo
**Related**: subsumes #039 (`GameDataStore.CreateFromTypeMap`); touches #036 (server readiness handshake) for client resume

## Goal
Let a host save an in-progress game to a file and later load it: the load flow drops into a **lobby** showing the saved game's player slots, the host assigns current participants (local / connected client / AI) to each saved slot, and on launch the game resumes from where it left off — same board, wounds, objectives, round number, turn order, and next-to-activate unit. Works in single-machine and networked play. Ships with a solid engine test suite. "Done" = a real game can be saved mid-round, reloaded into a lobby, re-crewed, and continued to a correct end state.

## Decisions
Locked with the user up front (2026-06-08):

- **Flow state lives in the store.** Round/turn progress is promoted into `GameDataStore` as a new `GameProgressData` component rather than a separate save-header blob. Rationale: it then serializes *and* replicates to clients for free, and it fixes a latent bug — round/turn state isn't replicated to clients today at all.
- **Save "any time," restore to the current activation's start.** The async/await stage call stack is not serializable, so true mid-stage save would impose a per-stage serialization tax forever (worst case: the 10-stage combat pipeline). Instead the engine keeps a **rolling in-memory snapshot taken at each activation boundary** (start of a unit's activation, at `DeterminePlayerTurnStage`); the Save button is always enabled and writes the latest clean snapshot. On load you resume at the start of the activation you were in and replay just that one unit. Cost: lose the half-finished activation only. No per-stage tax.
- **Durable save format.** Finish the stubbed `GameDataStore.CreateFromTypeMap` (this is #039) so saves survive future changes to the registered-type list, instead of only reusing `GameDataStoreBuilder.GetDefault()`. Embed a type-map fingerprint in the file and reject mismatches loudly.
- **Engine changes land on a submodule branch + bump**, matching the existing `Bump submodule` workflow. Ask before each submodule edit.
- **`GameProgressData` is a normalized mirror, not the raw contexts.** Considered serializing `MainPhaseContext`/`SingleRoundContext` directly, but they hold non-serializable `IGameContext` service refs and key off `ITeam` *object identity* (dict keys + identity comparisons against the store's teams), so raw JSON of them creates duplicate team instances that break lookups. The mirror stores teams by `TeamNumber` and units by `DataReference`, rehydrating against the store's canonical instances. The deeper "make the contexts themselves the serializable source of truth and delete the mirror" refactor is logged as low-priority **#053** (more invasive for the same result). Note: serializing contexts would not have made resume simpler regardless — the unavoidable hard part is the missing re-entry path (Phase 3), since the machine has no "enter stage X with restored context Y" hook today.

### Why this is mostly tractable
The entire mutable game world already lives in one ECS-style `GameDataStore` that round-trips to JSON via `GetAllDataReferencesAsJson()` / `CreateFromReferenceAndJson()` — this is the same path that syncs a joining client (`AddAllDataMessage`). A save file ≈ "dump the store"; load ≈ "replay it into a fresh store." Host is authoritative; clients rebuild via the existing `RequestAllDataMessage` round-trip, so multiplayer resume is nearly free once the host path works.

### The hard parts (ranked)
1. Suspended async stage call stacks at a pending request are unserializable → handled by the rolling-snapshot-at-activation-boundary decision above (only ever save quiescent state).
2. No resume entry point — `StateMachine.Enter` always starts at `MapSetupStage` with fresh contexts; `FDGServer` always builds the world via `CreateArmies`/`AddTeamDataToGameDataStore`. Needs a resume mode.
3. Flow state not in the store today (`MainPhaseContext`, `SingleRoundContext`, `TeamPlayerAlternationCursor`) → fixed by `GameProgressData`.
4. PlayerIDs are regenerated every lobby session → load requires a saved-slot → new-participant remap applied across all restored data.
5. `UnitData`'s `[JsonConstructor]` does NOT re-hook `OnModelWoundsDealt`; post-load subscription re-wiring required (also objective/renderer listeners, `GameDataUpdateSender`).

## Notes

- 2026-06-08: Item created from a deep five-front codebase investigation (state inventory, state machine, lobby/launch, serialization/networking, test conventions). Plan below.

### Plan of record

**Phase 1 — Engine: serializable game-flow progress (submodule)**
- Add `GameProgressData` component: `RoundCount`, current-stage marker enum, `TeamActivateOrder`, `CurrentRoundTeamFinishOrder`, cursor fields (`CurrentTeamIndex`, `CurrentPlayerIndexPerTeam`), already-activated unit refs.
- Register it in `GameDataStoreBuilder.GetDefault()` — **append at the end**; TypeID order is baked into every saved `DataReference`, never reorder.
- Have `MainPhaseContext` / `SingleRoundContext` mirror their state into `GameProgressData` at activation boundaries.
- Persist `GameSettings` (currently only on `GameContext`).

**Phase 2 — Engine: save & restore API (submodule)**
- Save: serialize `GetAllDataReferencesAsJson()` + type-map fingerprint + header via `GameDataStore.GetJsonSettings()` (only settings that handle `DataBinding<T>`).
- Restore: build store, replay each `ReferenceJsonValuePair` through `CreateFromReferenceAndJson` (preserve exact index+generation).
- Finish `CreateFromTypeMap` (#039); reject fingerprint mismatch loudly.
- Re-wire post-load subscriptions (`UnitData.OnModelWoundsDealt`, objective/renderer listeners, `GameDataUpdateSender`).
- Capture rolling snapshot at each activation boundary; expose latest-clean-snapshot to the save path. Quiescence signal (no pending entries in `RequestMessageSender._pendingTaskAndResolvers`).

**Phase 3 — Engine: resume path (submodule)**
- `FDGServer` resume mode: skip `CreateArmies` / `AddTeamDataToGameDataStore` when loading a pre-populated store.
- State machine re-enters at the saved stage with `MainPhaseContext`/`SingleRoundContext` rebuilt from `GameProgressData`, jumping to the start-of-activation stage.

**Phase 4 — Engine: PlayerID remap (submodule)**
- Apply `Dictionary<oldPlayerID,newPlayerID>` across `ArmyData.PlayerID`, `UnitData` owner, `TeamData` members, `PlayerSlotInfo`, objective owners, `GameProgressData`.

**Phase 5 — App: save UX (FdgRaylib)**
- In-game Save button/hotkey (always enabled; writes latest clean snapshot). `.fdgsave` extension + writer, `TinyDialogs` save dialog. Host-only.

**Phase 6 — App: load-into-lobby + slot assignment (FdgRaylib)**
- Main-menu "Load Game" button (`MainMenuScreen` + `Program.cs`). Load ⇒ become host: pre-seed a `LobbyViewModel_Host` from the save (settings + restored store).
- Slot-assignment UI: one row per saved slot (army name/faction/team) → assign to local / client / AI → produces the remap dict.
- Launch via the resume constructor + remap; reuse `HandleLaunch` → `AssignInterfaces` → `TransitionToGame`. Reassign player colors.

**Phase 7 — Multiplayer resume (mostly free)**
- Clients rebuild via existing `RequestAllDataMessage` / `AddAllDataMessage`; verify flow state (now in store) replicates and client-side subscriptions re-wire.

### Save-file format (proposed)
```
{ version, typeMapFingerprint[], gameSettings, slots[{SlotID,TeamNumber,savedPlayerID,ArmyName,Faction}], store[{reference,json}] }
```

### Test plan
NUnit, `dotnet test FutureOfDarkGrimness/FutureOfDarkGrimness.csproj`. Model on `MessageSerializationTests` (cross-store round-trip), `ConsolidateStageTests.BuildBattlefield`, `TerrainTestHelpers` (`TestGameContext`/`FixedDiceRoller`/`CapturingRequester`), `ObjectiveOwnershipTests` (run a stage, assert mutated state).
- `GameProgressData` round-trips (same-store + cross-store): cursor/round/activation-set preserved.
- Full-store snapshot → restore into fresh store: every `DataReference` (index+generation) identical; all `DataBinding`s re-resolve.
- Mutated state survives: deal wounds, move models, seize objectives → snapshot/restore → exact match.
- `GameSettings` round-trips. Type-map fingerprint mismatch rejected.
- PlayerID remap rewrites IDs everywhere; no stray old IDs; team membership/ownership consistent.
- Resume integration: advance mid-round (some units activated) → snapshot → restore into new `FDGServer` resume path → round count, activation order, remaining-unactivated set match; next activation picks the right unit; world not duplicated.
- Post-load subscription wiring: wound a restored unit → `UnitData.OnWoundsDealt` still aggregates (guards the `[JsonConstructor]` bug).
- Quiescence gate reports safe vs unsafe correctly.

## Outcome
_(written when the item closes)_
