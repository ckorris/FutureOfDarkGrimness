# Replay system plan

**Status:** planning complete 2026-10-30; implementation not started. Umbrella work item: **#449**.
**Branch:** `feature/replay` in BOTH repos (superproject `fdg-raylib` and the engine submodule
`FutureOfDarkGrimness`), branched from `master` (NOT from `feature/opr-3.6`). Nothing lands on `master`
until #457 says the whole plan is green.
**Owner:** Chris. **Decisions already taken (2026-10-30, Chris):** see section 2. **Open forks:** section 7.

A modern-strategy-game replay: every game is recorded as it is played, on the host AND on remote clients,
auto-saved when the game ends (however it ends), carried inside `.fdgsave` files so a saved-then-resumed
game keeps one continuous replay, playable in the GUI with pause / step-by-decision, and readable from the
command line so an agent can inspect what happened when a bug is reported.

---

## 1. Ground rules for every agent working on this plan

These apply on top of `CLAUDE.md`, which still governs (no AI attribution in commits, verify before
commit, submodule-first cadence, ASCII-only game text, one vertical slice at a time, never silently cut
scope, surface design forks before building).

1. **Branch.** Work on `feature/replay` in both repos. `git -C FutureOfDarkGrimness branch --show-current`
   and `git branch --show-current` must both print `feature/replay` before you commit. Never commit to
   `master`, never merge to `master`, never push `master`. Only #457 merges the feature branch.
2. **Commit cadence.** Engine change -> commit in the submodule on its `feature/replay` -> bump the
   submodule pointer together with the app-side change in a superproject commit on `feature/replay`.
   Commit subjects start with the work-item number (`450: ReplayRecorder ...`). Push with
   `git push origin feature/replay` in each repo (set upstream on first push). Do not force-push.
3. **Picking up master.** Fixes keep landing on `master`, and the OPR 3.6 upgrade (`feature/opr-3.6`,
   `docs/opr-3.6-upgrade-plan.md`) is running in parallel. The #449 owner merges `master` into
   `feature/replay` (both repos, submodule first) after each ticket closes. Do not rebase. Never merge
   `feature/opr-3.6` into this branch; it reaches us through `master` when #444 lands.
4. **Numbers.** Items #449-#457 are reserved in `WorkItemsList.md` (section "Replay system") and in
   `WorkItems/Reconciliations.md`. A new sub-item needs a fresh number: `git fetch origin` first and take
   max(origin/master index + archive) + 1, per `WorkItems/README.md`. The 3.6 upgrade files numbers the
   same way, so expect gaps.
5. **No compatibility promises.** Replay files carry a `FormatVersion` and a build stamp; a mismatch is
   refused with a clear message. Bump `FormatVersion` freely whenever the shape changes. Never write a
   migration. `.fdgsave` files without a replay block must still load (that is the one tolerance).
6. **Record what happened; never re-run the engine to reconstruct a replay.** Playback applies recorded
   state deltas; it never calls a stage, a resolver or the dice. (Decision D1.)
7. **The recorder must be invisible to the game.** It observes; it never blocks the engine thread for
   long, never throws into the engine (every recorder failure is caught, logged once, and disables
   recording for that game), and never attaches to AI simulations (`SimulationService` builds its own
   `FDGServer`s - the recorder is injected only by the real-game paths).
8. **Tests.** Engine tests are NUnit in `FutureOfDarkGrimness/Tests/` and run with
   `dotnet test FutureOfDarkGrimness/FutureOfDarkGrimness.csproj`; app tests live in `FdgRaylib.Tests/`.
   Do NOT write whole-game golden-file tests (a replay of seed 42 compared byte-for-byte) - the 3.6
   upgrade changes rules under you and would turn them red for reasons unrelated to replay. Pin
   behaviour with small scripted sequences (section 4.3) and with "live final state == played-back final
   state" equalities on a real in-process game.
9. **Ledger discipline.** Each item's detail file (`WorkItems/NNN-*.md`) is the running record: dated
   notes newest-first, decisions, deferred facets named explicitly. Update section 6 of this plan when an
   item changes state.
10. **Model / effort.** Opus, effort high, for every item (Chris's call; this plan was written for it).

---

## 2. Decisions taken (Chris, 2026-10-30)

- **D1 - Record what happened.** The replay is the ordered stream of state deltas, decisions, beats and
  log lines the game actually produced, plus periodic full snapshots. Not "seed + decisions, re-run".
  Why: #209/#210 are open nondeterminism items, a resume restarts the dice stream, AI search drifts
  between builds, and a re-run of a buggy game can quietly take a different path and hide the bug. The
  seed is still recorded (and always materialised, D5) so re-running stays available as a debugging aid.
- **D2 - Broadcast decision payloads to clients.** `StageTaskNotifyResolvedMessage` grows to carry the
  request type, request JSON and reply JSON. This partially reverses #088's "only the target player sees
  the payload", but only AFTER the decision has been acted on, when nothing is hidden any more.
- **D3 - Both containers.** A standalone `.fdgreplay` file is written at game end; the same bytes are
  embedded in `.fdgsave` so a resumed game continues the replay.
- **D4 - AI decisions are steps.** Every request/reply pair is a step, whoever answered it, except
  requests with at most one option, which fold into the previous step (section 4.4).
- **D5 - Every game has a concrete seed.** When `GameSettings.DiceSeed` is null at launch the host picks
  one and stores it, so a replay can always say which seed produced it (ticket #450).

---

## 3. What exists already (read these before starting any ticket)

| Need | Already there | Where |
|------|---------------|-------|
| Every store mutation as (reference, JSON) | `GameDataStore.OnDataAddedAsJson / OnDataUpdatedAsJson / OnDataRemoved`; host forwards them to clients | `Data/Containers/GameDataStore.cs`, `Network/Synchronization/GameDataUpdateSender.cs` |
| Applying such a delta to a store | `GameDataUpdateReceiver` (client), `StoreReplay.Rebuild` (full state) | `Network/Synchronization/GameDataUpdateReceiver.cs`, `SaveLoad/StoreReplay.cs` |
| Full snapshot of a store | `GameSaveSerializer.Save/Load`, `StoreSnapshot` (typed clone) | `SaveLoad/GameSaveSerializer.cs`, `Data/StoreSnapshot.cs` |
| Every player decision, as JSON, one chokepoint | `RequestMessageSender.RequestDecision` (request) and `OnReceivedReplyMessage` (reply) | `StageResolution/RequestMessageSender.cs` |
| Animation events ("beats"), already serializable and already relayed to clients | `PresentationBeat` subclasses, `PresentationRelayer`, `NetworkedPresentationSink`, `PresentBeatMessage` | `Presentation/` |
| Log lines relayed to clients | `LogChatNetworkMessage` / `ITextOutput` | `Network/Messages/` |
| Save / resume / recovery save / scenario-from-save | #052, #187, #167 | `SaveLoad/`, `FdgRaylib/SaveLoad/RecoverySave.cs`, `FdgRaylib/Cli/ScenarioLauncher.cs` |
| Seeded dice | #193 `GameRandom`, `GameSettings.DiceSeed`, `FDGServer.GetDiceRoller` | `GameModel/FDGServer.cs` |
| Game end on every path | `FDGServer.OnGameCompleted/OnGameEnded`, `LobbyViewModel_*.OnGameEnded`, `RaylibRenderer.ExitGame` + `OnGameExited`, `GameEndedMessage` on clients | see ticket #452 |
| Wire framing limit | 16 MB per message post-join (`CommandProtocol.MAX_PAYLOAD_BYTES`) | `Network/Connection/CommandProtocol.cs` |

---

## 4. Design

### 4.1 Replay container (`.fdgreplay`)

One gzip stream of UTF-8 JSON Lines. Line 1 is the header; every later line is one event. Written whole
at game end (the recorder buffers in memory - a 2k 1v1 game is ~3.5 MB of strings, section 5), and the
identical bytes go base64 into `GameSaveFile.Replay` (#453).

```
header : { FormatVersion, BuildVersion, EngineVersion, GameId, Seed, RandomnessType, Settings,
           Slots:[{SlotId, PlayerId, Name, ArmyName, Team, Controller: Human|Ai|Remote}],
           RecordedBy: Host|Client, RecordedByPlayerId, StartedUtc }
event  : { Seq, T (ms since start), Kind, ...payload }
```

Event kinds (one C# record per kind under `FDG.Replay`; `Kind` is the discriminator):

| Kind | Payload | Source (host) | Source (client) |
|------|---------|---------------|-----------------|
| `StoreAdd` / `StoreUpdate` / `StoreRemove` | `Ref` (DataReference), `Json` | store events | `Add/Update/RemoveSingleDataMessage` |
| `Snapshot` | `Index`, `SaveJson` (a full `GameSaveSerializer.Save`) | recorder, at `GameStart`, every `RoundStart`, every `Resumed` | the `AddAllDataMessage` on join, then the host's `Snapshot` marker (section 4.2) |
| `Request` | `TaskId`, `PlayerId`, `RequestType`, `ReplyType`, `DisplayName`, `RequestJson` | `RequestMessageSender.RequestDecision` observer | own: `StageTaskRequestMessage`; others: the enlarged `StageTaskNotifyResolvedMessage` (D2) |
| `Reply` | `TaskId`, `ReplyJson` | `RequestMessageSender.OnReceivedReplyMessage` observer | `StageTaskNotifyResolvedMessage` |
| `RequestError` | `TaskId`, `Message` | `OnReceivedErrorMessage` | `StageTaskNotifyResolvedMessage` (error flag) |
| `Beat` | `BeatJson` (`$type`-tagged `PresentationBeat`) | `PresentationRelayer.Present` observer | `PresentBeatMessage` |
| `Log` | `Line` | `ITextOutput` wrapper in `FDGServer` | `LogChatNetworkMessage` |
| `Marker` | `Marker: GameStart / RoundStart(Round) / ActivationStart(Round, Activation, PlayerId, UnitRef) / Saved / Resumed / GameEnded(Outcome, Message)` | engine (`ReplayMarkerMessage` broadcast by the host so clients get the same markers) | `ReplayMarkerMessage` |

Rules:
- `Seq` is assigned under the recorder's lock and is dense per recording; host and client recordings of
  the same game do NOT share `Seq` (they see different message interleavings) - `TaskId` and
  `GameId` are the cross-recording keys.
- `Snapshot` events are the seek points. Per-round, not per-activation: section 5 shows a per-activation
  policy costs ~1.4 MB gz per game while per-round costs ~130 KB; seeking inside a round applies at most
  ~15 activations of deltas, which the client already applies live.
- `Resumed` is a hard boundary: the following `Snapshot` REPLACES state (the half-finished activation
  before `Saved` is re-played by the engine after a resume, so the deltas before the boundary describe
  work that was undone). Playback must never apply deltas across a `Resumed` marker.
- Request JSON is recorded as sent (with `WireJsonSettings`), reply JSON as received. They embed
  `DataReference`s into the game's store; the playback store (4.3) has identical references, so a reply
  can be deserialized against it for display.

### 4.2 Recording

**Host** (`ReplayRecorder : IDisposable`, engine, `FDG.Replay`): constructed by the real-game launch
paths (`LobbyViewModel_Host.Launch*`, `CliApp`, `ScenarioLauncher`/`Program --scenario`) and passed into
`FDGServer` as an optional parameter (null = no recording; `SimulationService`, FdgLab and tests pass
nothing). Attaches to: the store's three events; `RequestMessageSender` via a new observer interface
`IRequestObserver { OnRequested(...); OnReplied(...); OnFailed(...) }`; `PresentationRelayer` via an
`IBeatObserver`; the server's `ITextOutput` via a decorating wrapper; and the state machine's round /
activation boundaries (`SingleRoundStage.Enter`, `DeterminePlayerTurnStage.Enter`, where the #052 rolling
snapshot is taken) via `IGameContext.ReplayMarkers` (a small sink the stages call; default no-op). Each
marker the host records it also broadcasts as `ReplayMarkerMessage` so clients record the same marker.

**Client** (`ClientReplayRecorder`, engine): constructed in `FDGGame_AsClient` next to
`GameDataUpdateReceiver`, registering for the message types in the table above. The join-time
`AddAllDataMessage` becomes `Snapshot` #0. Own-player requests arrive twice (the `StageTaskRequestMessage`
and the broadcast `StageTaskNotifyResolvedMessage`); dedupe by `TaskId`.

**Exposure:** `IFDGGame.Replay` (`IReplaySource? { bool IsRecording; byte[] ExportGzip(); ReplaySummary Summary; }`)
on both `FDGGame_AsLocal` and `FDGGame_AsClient`; `ILobbyViewModel.Replay` forwards to the launched game
(host) or the client game. The app never touches the recorder's internals.

**Thread safety:** store events and markers fire on the engine thread; request observers fire on the
engine thread (request) and the bus/network read thread (reply); client messages arrive on the network
read thread; `ExportGzip()` is called from the UI thread. One lock around the event list and `Seq`.
`ExportGzip()` copies the list under the lock, then compresses outside it.

### 4.3 Playback (engine, `FDG.Replay`)

`ReplayReader.Read(Stream) -> ReplayDocument { Header, Events }` (refuses `FormatVersion` mismatch; warns
on `BuildVersion` mismatch - dev builds all report `-dev`).

`ReplayTimeline(ReplayDocument)` indexes the events: snapshots by `Seq`, requests joined to replies by
`TaskId`, markers, and the **step list** (4.4). Pure, testable, no store.

`ReplayPlayer(ReplayTimeline)` owns a `GameDataStore` + `TableState` and a cursor:
- `SeekToStep(n)`: pick the last `Snapshot` with `Seq <= step.RequestSeq` that is not separated from the
  step by a `Resumed` marker; `GameSaveSerializer.Load` it into a NEW store (fires `OnStoreReplaced`);
  then apply every `Store*` event in `(snapshot.Seq, step.RequestSeq]` through a shared
  `StoreDeltaApplier` (extract the apply code from `GameDataUpdateReceiver` so client sync and playback
  use one implementation). Returns `PlaybackSpan { Beats, LogLines, Markers }` for the events applied.
- `StepForward()`: apply `(cursor, nextStep.RequestSeq]` in place (no store replacement); returns the span.
- `StepBack()`: `SeekToStep(cursor - 1)`.
- `Step` metadata for the UI: `Index, Round, Activation, PlayerId, Controller, RequestType, DisplayName,
  Summary` where `Summary` comes from `ReplayDecisionDescriber` (one ASCII line per request type: unit
  name + action / target / path length / wounds assigned / yes-no; generic fallback = `DisplayName`).

### 4.4 Steps (D4)

A step is a `Request` event that has a matching `Reply`. Folded (not a step, shown inside the previous
step's span): requests whose option list has <= 1 entries (`SelectionRequest<T>.Options.Count <= 1`,
`StringSelectionRequest` likewise), and `RequestError`s. Everything else - human, AI or remote - is a
step. The filter is one class (`DefaultStepFilter`) so the viewer can offer "show folded steps" later.

### 4.5 Files on disk

- `Replays/` beside the executable (same policy as `RecoverySave`'s `Saves/`): `replay-<UTC
  yyyyMMdd-HHmmss>-<armyA>-vs-<armyB>.fdgreplay`, newest `KeepNewest` kept (default 30; `UserConfig`
  setting), written by host and client alike.
- `.fdgsave` gains `Replay` (base64 gzip of the container as of the save) - #453.

---

## 5. Size estimate (measured 2026-10-30)

Method: a temporary probe in FdgLab (archived at `~/Projects/fdg-lab-scratch/replay-probe/`, not in the
repo) tallied every store event, request, reply and log line of two seeded 2k 1v1 AI games
(`smoke --seed 42` Battle Brothers vs Orks; `--seed 7` Alien Hives vs HDF), 4 rounds, 54-56 activations,
312-349 decisions each. Beats were NOT measured (FdgLab wires no presenter); their row is an estimate.

| Stream | Events | Raw bytes | Notes |
|--------|--------|-----------|-------|
| `StoreUpdate:UnitData` | 285-288 | 1.95-2.0 MB | whole-unit JSON (~7 KB) rewritten on every token/flag change; the bulk of the file, but near-identical repeats compress ~60x |
| `Request` | 312-349 | 520-660 KB | `ChooseRangedAttackRequest` 7-8 KB each (targets + forecasts), `DefineMovementPathRequest` 2.5-3 KB, the rest < 1.5 KB |
| `StoreAdd` (armies, models, terrain) | 333-375 | 245-270 KB | setup only |
| `Reply` | 312-349 | 150-170 KB | |
| `StoreUpdate:GameProgressData` | 165-171 | 163-171 KB | the #052 rolling snapshot, ~1 KB each |
| `StoreUpdate:Position/Float2` | 400-500 | 65-80 KB | movement is cheap |
| `Log` | 1500-1800 lines | 67-82 KB | |
| `Beat` (estimate) | 500-1500 | 0.3-1 MB | ~300-800 B each; measure in #450 and update this table |
| **Event stream total** | | **3.2-3.4 MB raw -> 166-192 KB gzip** (+ est. 30-60 KB gzip for beats) | |
| `Snapshot` (one full save) | | 400 KB raw -> 21-27 KB gzip | per round (5 incl. start) = ~130 KB gz; per activation (55) = ~1.4 MB gz, hence per-round |

**Per 2k 1v1 game: ~0.35-0.5 MB `.fdgreplay`** (stream ~0.2-0.25 MB + snapshots ~0.13 MB). Scales
linearly with decisions and unit count: a 4k or 2v2 game ~2x, a 1k game ~0.5x. A 1 MB uncompressed
`.fdgsave` from the GUI (the two in the repo root have 201 models) will gain ~0.5-0.7 MB of base64
(~1.5-1.7 MB total); a lab-sized 400 KB save roughly doubles. Embedding the stream UNcompressed would add
3.5-4.5 MB (4-10x the save) - do not. Memory: the recorder holds ~3.5 MB of strings per game; fine.
If size ever matters, a field-level diff for `UnitData` updates would cut the raw stream ~10x; out of
scope here (gzip already does most of that work).

---

## 6. Items

| # | Title | Depends on | Status |
|---|-------|------------|--------|
| 449 | Umbrella: plan, ground rules, master merges, release gate | - | planning done |
| 450 | Engine: replay format + host-side `ReplayRecorder` (store, requests, beats, log, markers, snapshots, seed materialisation) | - | todo |
| 451 | Engine: decision payloads to clients (D2) + `ClientReplayRecorder` + `ReplayMarkerMessage` | 450 | todo |
| 452 | App: auto-write `.fdgreplay` on every game exit (host, client, headless, scenario, crash best-effort) | 450, 451 | todo |
| 453 | Replay inside `.fdgsave`: save embeds, resume continues, clients receive history | 450, 451 | todo |
| 454 | Engine: playback (`ReplayReader`, `ReplayTimeline`, `ReplayPlayer`, `StoreDeltaApplier`, step filter, describer) | 450 | todo |
| 455 | Headless tooling: `--replay` dump / stats / verify / export / make-save | 454 | todo |
| 456 | GUI replay viewer: browser screen, playback screen, controls, decision list | 454, 452 | todo |
| 457 | Verification + merge: networked hand-verify, size check, docs, merge `feature/replay` -> `master` | all | todo |

Suggested order: 450 -> 454 -> 455 (an agent can now debug from a replay) -> 451 -> 452 -> 453 -> 456 -> 457.
450/454 and 451 can run in parallel sessions once 450's event records exist.

---

## 7. Open forks (need Chris before the ticket that depends on them)

- **F1 (#456)** Viewer autoplay pacing: advance when the span's beats finish + a fixed dwell (recommended,
  default 0.75 s), or a fixed interval per step regardless of beats.
- **F2 (#456)** Where the viewer lives: a new `ReplayScreen` that reuses the in-game canvas via
  `RaylibRenderer.TransitionToGame`-style wiring (recommended), or a mode flag inside the in-game screen.
- **F3 (#452)** Keep count for `Replays/` (30 recommended) and whether a replay is also written when a
  game is abandoned before deployment finished (recommended: yes, anything with >= 1 step).

---

## 8. Working in parallel with the OPR 3.6 upgrade

Checked 2026-10-30 against `origin/feature/opr-3.6` (both repos):

- The 3.6 engine branch touches `Rules/`, `StateMachine/` stages, `Ai/Tactician`, `GameObjects/Core`
  (4 files), `StageResolution/Requests` (2 files), tests. It does NOT touch `GameModel/`, `Network/`,
  `SaveLoad/`, `Presentation/`, `StageResolution/RequestMessageSender.cs` or `Data/` - which is where
  almost all replay engine work lands. The 3.6 app branch touches GUI/CLI resolvers, `LobbyScreen` (41
  lines), `RaylibRenderer` (3 lines), Army Forge, Combat Calculator; replay touches `Program.cs`,
  `MainMenuScreen`, `RaylibRenderer` (new screen + exit hook), `LobbyScreen.HandleLaunch`, new files.
  Expected conflicts: the submodule pointer (always, mechanical), a few lines in `LobbyScreen` and
  `RaylibRenderer`. Low.
- Touch points to be careful with: `GameProgressData` (3.6 may edit `GameObjects/Core`; #450 adds
  `GameId` there - keep it one appended field), `StageResolution/Requests` (3.6 changes request shapes;
  replay records them opaquely, no coupling), `DeterminePlayerTurnStage` / `SingleRoundStage` (#450 adds
  one marker call each; 3.6 melee/fatigue work is in other stages).
- Semantics: a replay recorded on one branch will not play on the other (request/store shapes differ) -
  that is what the build stamp is for; no compatibility is owed.
- Tests: follow rule 8 (no whole-game golden files) or 3.6 rule changes will break replay tests.
- Merge order: whichever finishes first merges to `master` first; the other merges `master` in. Replay is
  the shorter plan (9 items vs 31), so it will probably land first and reach 3.6 through the #415 owner's
  next `master` -> `feature/opr-3.6` merge.
- Machine: the lab box has crashed under heavy load; do not run a 3.6 bench and a replay GUI hand-verify
  (or the #450 beat measurement) at the same time.

Verdict: safe to run in parallel on `feature/replay`.
