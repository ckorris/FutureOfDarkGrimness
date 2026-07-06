# 052 — Save / Load a game in progress

**Status**: BUILT + MERGED (status corrected 2026-07-05, ledger audit — was stale "todo"). Core is live on master: `GameProgressData` + `GameSaveSerializer` + 8 passing `GameSaveLoadTests` + lobby re-crew-of-saved-slots + `FDGServer` resume path; engine suite 1138/0. NOT unstarted. Remaining before Done: a real save->resume hands-on verify in the running app (shares the hold with #095).
**Related**: subsumes #039 (`GameDataStore.CreateFromTypeMap`); touches #036 (server readiness handshake) for client resume

## Goal
Let a host save an in-progress game to a file and later load it: the load flow drops into a **lobby** showing the saved game's player slots, the host assigns current participants (local / connected client / AI) to each saved slot, and on launch the game resumes from where it left off — same board, wounds, objectives, round number, turn order, and next-to-activate unit. Works in single-machine and networked play. Ships with a solid engine test suite. "Done" = a real game can be saved mid-round, reloaded into a lobby, re-crewed, and continued to a correct end state.

## Decisions
Locked with the user up front (2026-06-08):

- **Flow state lives in the store.** Round/turn progress is promoted into `GameDataStore` as a new `GameProgressData` component rather than a separate save-header blob. Rationale: it then serializes *and* replicates to clients for free, and it fixes a latent bug — round/turn state isn't replicated to clients today at all.
- **Save "any time," restore to the current activation's start.** The async/await stage call stack is not serializable, so true mid-stage save would impose a per-stage serialization tax forever (worst case: the 10-stage combat pipeline). Instead the engine keeps a **rolling in-memory snapshot taken at each activation boundary** (start of a unit's activation, at `DeterminePlayerTurnStage`); the Save button is always enabled and writes the latest clean snapshot. On load you resume at the start of the activation you were in and replay just that one unit. Cost: lose the half-finished activation only. No per-stage tax.
- **Durable save format.** Finish the stubbed `GameDataStore.CreateFromTypeMap` (this is #039) so saves survive future changes to the registered-type list, instead of only reusing `GameDataStoreBuilder.GetDefault()`. Embed a type-map fingerprint in the file and reject mismatches loudly.
- **Engine changes land on a submodule branch + bump**, matching the existing `Bump submodule` workflow. Ask before each submodule edit.
- **`GameProgressData` is a normalized mirror, not the raw contexts.** Considered serializing `MainPhaseContext`/`SingleRoundContext` directly, but they hold non-serializable `IGameContext` service refs and key off `ITeam` *object identity* (dict keys + identity comparisons against the store's teams), so raw JSON of them creates duplicate team instances that break lookups. The mirror stores teams by `TeamNumber` and units by `DataReference`, rehydrating against the store's canonical instances. The deeper "make the contexts themselves the serializable source of truth and delete the mirror" refactor is logged as low-priority **#057** (more invasive for the same result). Note: serializing contexts would not have made resume simpler regardless — the unavoidable hard part is the missing re-entry path (Phase 3), since the machine has no "enter stage X with restored context Y" hook today.

### Why this is mostly tractable
The entire mutable game world already lives in one ECS-style `GameDataStore` that round-trips to JSON via `GetAllDataReferencesAsJson()` / `CreateFromReferenceAndJson()` — this is the same path that syncs a joining client (`AddAllDataMessage`). A save file ≈ "dump the store"; load ≈ "replay it into a fresh store." Host is authoritative; clients rebuild via the existing `RequestAllDataMessage` round-trip, so multiplayer resume is nearly free once the host path works.

### The hard parts (ranked)
1. Suspended async stage call stacks at a pending request are unserializable → handled by the rolling-snapshot-at-activation-boundary decision above (only ever save quiescent state).
2. No resume entry point — `StateMachine.Enter` always starts at `MapSetupStage` with fresh contexts; `FDGServer` always builds the world via `CreateArmies`/`AddTeamDataToGameDataStore`. Needs a resume mode.
3. Flow state not in the store today (`MainPhaseContext`, `SingleRoundContext`, `TeamPlayerAlternationCursor`) → fixed by `GameProgressData`.
4. PlayerIDs are regenerated every lobby session → load requires a saved-slot → new-participant remap applied across all restored data.
5. `UnitData`'s `[JsonConstructor]` does NOT re-hook `OnModelWoundsDealt`; post-load subscription re-wiring required (also objective/renderer listeners, `GameDataUpdateSender`).

## Notes

- 2026-06-08: **Live-test bug #1 (fixed).** First GUI load test: restored models didn't draw (unit labels showed, model discs didn't). Cause: `RaylibRenderer.DrawModels` only draws `_placedModels`, populated solely from `OnPositionChanged` — which never fires for save-loaded models (their positions are set during store replay, not via live `SetPosition`). Fix: seed already-positioned models into `_placedModels` when subscribing (`RaylibRenderer.SubscribeToModel`). Good reminder: renderer seeding that keys off live events misses restored state — terrain/objectives were already seeded directly, models were the gap.

- 2026-06-08: **Phase 6b done — feature implemented end to end (all phases).** Per-slot Local/AI picker in the resume lobby (`SetSavedSlotPlayerType`; add/remove hidden in resume; covers solo + hotseat). Networked re-crew: a client joining a resumed game is auto-assigned to the next AI saved slot and adopts that slot's saved PlayerID — **strictly isolated behind `_isResume`** so the new-game join flow is untouched; correct-by-design for 1v1, not yet live-tested. **Still unverified live:** the single-machine load→resume loop in the GUI, and any networked resume (needs two machines). Remaining refinement: host-chosen connection→slot assignment (vs the current auto-fill).

- 2026-06-08: **Phases 5 + 6a implemented — single-machine save/load works end to end.**
  - Phase 5 (Save): `ILobbyViewModel.CanSaveGame`/`SaveGameToJson()` (host serializes its store, client null — client saving is #054); `GameSaveFile.EXTENSION_WITH_PERIOD = ".fdgsave"`. App: host-only "Save Game" button in the in-game `##tabletools` toolbar → `SaveFileDialog` → writes `.fdgsave`; save hook threaded `LobbyScreen.HandleLaunch` → `OnGameLaunched` → `TransitionToGame` → `TableTooltipOverlay`.
  - Phase 6a (Load → resume, single machine): `LobbyViewModel_Host` resume ctor seeds the slot list from the saved `PlayerSlotInfo`s (reusing saved PlayerIDs; default re-crew = host plays first slot, rest AI), settings from saved `GameProgressData`; `LaunchResume` deletes saved `PlayerSlotInfo` (dedup), rebuilds `PlayerSlot[]`, calls the resume `FDGServer` ctor. `ILobbyViewModel.IsResumeMode`/`TryResumeGame`/`SetSavedSlotPlayerType`. App: main-menu "Load Game" → open `.fdgsave` → `GameSaveSerializer.Load` → `FDGHost` + resume host VM → lobby; launch button shows RESUME in resume mode.
  - **PlayerID remap (old Phase 4) dropped** by decision — resume reuses the saved PlayerIDs and just attaches new controllers, so no store rewrite.
  - **Remaining = Phase 6b**: per-slot controller picker UI in the lobby (Local/AI, and assign a connected client to a saved slot) + networked client adoption protocol (host sends the saved PlayerID to the assigned client via the existing `LobbyPlayerIDAssignment` path; client adopts it). Needs live multi-machine testing. Also: manual GUI verification of the single-machine load→resume loop (builds + engine resume tests green; not yet eyeballed in the running app).

- 2026-06-08: **Phases 1–3 implemented (engine side), suite 293/0.** All on submodule branch `052-save-load`.
  - Phase 1: `GameProgressData` component + `GameProgressUtilities` (Capture/Write/TryGet) + registration; round-trip + capture tests.
  - Phase 2: `GameSaveSerializer`/`GameSaveFile` whole-store save/restore; finished `CreateFromTypeMap` (#039 closed); `ComponentStore.Capacity`; retry-based replay (forward refs); `UnitData.RewireModelWoundSubscriptions` post-load fix.
  - Phase 3a: rolling `GameProgressData` snapshot written at the top of `DeterminePlayerTurnStage.Enter`; `SingleRoundContext.RoundCount` threaded from `MainPhaseContext`.
  - Phase 3b: resume re-entry — `IGameContext.ResumeProgress` (default-interface one-shot token), `ParentStage.GetResumeEntry` hook, `StateMachine.Enter(ctx, stageName)`, `MainPhaseRoundStage`/`SingleRoundStage` resume overrides, `SingleRoundContext`/`MainPhaseContext` restore constructors, `GameProgressUtilities.Restore*`, `FDGServer` resume constructor (skips world creation + creation rules). Reserve re-offer semantics honored (skip already-run setup on the resumed round only; next round re-offers normally). Also persisted `ModelData.TotalWounds` (was `[JsonIgnore]`) so Tough survives load/network.
  - Tests cover: restore rebuilds cursor/unactivated; full save→load→restore→next-pick-is-correct-player. NOT yet covered: full FDGServer/state-machine run (blocks on player-decision requests in a unit test) — thin glue, build-verified; will be exercised in GUI (Phases 5–6).
  - Remaining: Phase 5 (in-game Save UX), Phase 6 (load-into-lobby + slot assignment + PlayerID remap), Phase 7 (multiplayer resume verify).

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

## Phase 3b design — resume re-entry (FOR REVIEW, not yet implemented)

Goal: rebuild the running state machine from a loaded store + its `GameProgressData` so play continues at the start of the activation that was in progress when saved. Phases 1–3a already make every live store carry a correct rolling `GameProgressData` snapshot (written at the top of each `DeterminePlayerTurnStage.Enter`, before the next unit is chosen).

### Activation-flow facts this builds on
- Per-activation cycle: `DeterminePlayerTurnStage.Enter` *(snapshot here)* → `TryAdvanceToNextPlayer` (advances cursor) → `SingleTurnStage` (unit acts) → on leave, `MarkUnitAsActivated`. So the snapshot's cursor is the **pre-advance** value and the unit about to/currently act is **not yet** marked activated. Restoring that exact state and re-entering `DeterminePlayerTurnStage` reproduces the same `TryAdvance` pick → re-plays that activation.
- `ReconcileNewRoundStage`: no-op. `StartOfRoundExtraActionStage`: offers Ambush reserves (round 2+) via player prompts, **once at the start of the round** (before any activation; the rolling snapshot is taken later, inside `SingleRoundStage`).
- `MainPhaseRoundStage.Enter` runs **once**; its children loop among themselves, threading one `IMainPhaseContext` (with `RoundCount`) across all rounds. `SingleRoundStage.Enter` runs **once per round**.
- Hard constraint: `SingleRoundStage.GetNewChildContext` rebuilds the unactivated set from armies (resets activations). On resume it must instead build from the snapshot.

### Reserve (Ambush) re-offer semantics — save/load is transparent
The ambush offer is **once per round**. If a player is offered a reserve at the start of round R, declines, activates a unit, then saves and loads: on resume they are **NOT** re-offered in round R (they already answered) — they are offered again only at the **start of round R+1**, via the normal round loop. We get this for free by **skipping the already-run setup stages (`ReconcileNewRound` + `StartOfRoundExtraAction`) on the resumed round only**; every subsequent round runs them normally. So a declined reserve is never lost (offered next round) and never double-asked (not re-offered in the resumed round). Because no reserve arrives *during* a skipped-setup resume, the snapshot's frozen `UnactivatedUnits` set stays exactly accurate — **Phase 1's representation is unchanged** (no `ActivatedUnits` rework).

### Mechanism: one-shot restored-context injection
1. **Resume token on `GameContext`.** Add `GameProgressData? ResumeProgress { get; }` + `ConsumeResumeProgress()`. On load, `FDGServer` reads the store's `GameProgressData` and seeds it. Consumed after the first `SingleRoundStage` builds its context, so every later round runs fresh.
2. **`ParentStage` resume hook.** Add `protected virtual (StageBase<TContextChild> child, TContextChild context)? GetResumeEntry(TContextSelf ctx) => null;`. In `Enter`: if non-null, `TransitionToChild` into that child+context; else the existing fresh path (`_startingChild` + `GetNewChildContext`). Default null = zero behavior change for every other stage. (Also a useful seam for the #057 refactor.)
3. **`StateMachine.Enter` overload** to start at a named stage (`MainPhaseRoundStage`) instead of `_startingStage` (MapSetup) — look up in the existing `_transitions` dict.
4. **`MainPhaseRoundStage`** overrides `GetResumeEntry`: when `ResumeProgress` present, build an `IMainPhaseContext` from it (`RoundCount`, `TeamActivateOrder`) and return `(singleRoundStage, thatContext)` — skipping ReconcileNewRound + StartOfRoundExtraAction for this (already-set-up) resumed round. (Store the `SingleRoundStage` child as a field.) One-shot naturally, since `Enter` runs once; later rounds loop through setup normally (re-offering reserves at each round start).
5. **`SingleRoundStage`** overrides `GetResumeEntry`: when `ResumeProgress` present, build a restored `SingleRoundContext` and return `(determinePlayerTurnStage, restoredContext)` (same starting child, restored context), then `ConsumeResumeProgress()`.
6. **`SingleRoundContext` restore constructor** that sets cursor (`CurrentTeamIndex`, per-team player index by `TeamNumber`→`ITeam` via `TableState.Teams`), `_currentRoundTeamFinishOrder`, and `_unactivatedUnits` (regrouped from `GameProgressData.UnactivatedUnits` by each unit's `PlayerID`) — **without** calling `SetUnactivatedUnits`.
7. **`GameProgressUtilities`** inverse-of-`Capture` helpers: map team numbers → `ITeam`, rebuild the cursor + unactivated structures from a snapshot.
8. **`FDGServer` resume constructor** (or flag): takes the pre-loaded store; **skips** `AddTeamDataToGameDataStore` + `CreateArmies` + `UnitCreationRules` (data already present, wounds already set); uses `GameProgressData.Settings` for the dice roller etc.; seeds `ResumeProgress`; builds the machine and `Enter`s at `MainPhaseRoundStage`. (Store loaded via `GameSaveSerializer.Load`, which already re-wired unit subscriptions.)

### Edge cases / risks
- Reserve declined in the resumed round is offered again at the start of the **next** round (not re-asked on reload) — see semantics above.
- First `DeterminePlayerTurnStage` after resume re-writes the snapshot (idempotent — same values).
- VictoryCalculation/round-end transitions unaffected (resume only injects the first round's entry).
- Saving during deployment/map-setup is out of scope (`EResumeStage` only handles `MainPhase`; disallow saving before round 1, or extend later).
- Hardest to test: needs the machine driven with stub resolvers. Plan: an integration test using `NullPlayerRequester`/`NoOpLayer`-style doubles that loads a mid-round save and asserts the next activation picks the correct unit and the unactivated set shrinks correctly; plus a `SingleRoundContext` restore unit test (cursor + unactivated round-trip).

## Outcome
Implemented end to end across phases 1–6 (engine suite 293/0; both projects build). Save: host serializes the whole `GameDataStore` (+ flow-state `GameProgressData`, rolling-snapshotted at each activation boundary) to a `.fdgsave` via an in-game button. Load: main-menu "Load Game" rebuilds the store (`GameSaveSerializer` + finished `CreateFromTypeMap`, closing #039), opens a host resume lobby seeded from the saved slots (reusing saved PlayerIDs — no remap), lets the host re-crew each slot (Local/AI; networked clients auto-adopt saved slots), and resumes the state machine mid-round via a one-shot resume path. Phase 4 (PlayerID remap) was dropped by design. **Deferred / not done:** live GUI verification (single-machine) and live multiplayer verification (networked resume) — both need a display / two machines; host-chosen connection→slot assignment; client-initiated save (#054); and the optional context-serialization refactor (#057). Once eyeballed in the running app, move this to Done in the index.
