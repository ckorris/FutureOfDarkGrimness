# 450 — Engine: replay format + host-side ReplayRecorder

**Status**: in progress (2026-10-30)
**Related**: plan `docs/replay-plan.md` sections 4.1, 4.2, 5; umbrella #449; consumers #451 (client), #452 (auto-write), #453 (save embedding), #454 (playback)

## Goal
A host that plays a game (local, networked or headless) ends up with an in-memory `ReplayRecorder` holding
every store delta, every request/reply pair, every presentation beat, every log line and the round /
activation / game-end markers, with a full snapshot at game start and at every round start, exportable as
the gzipped JSON-Lines container of plan section 4.1. The recorder is opt-in (null for simulations, FdgLab
and existing tests), never throws into the engine, and costs nothing measurable per decision. Every game
records a concrete dice seed (D5). Done when an in-process game (the `DeterminismTests` assembly) records,
`ExportGzip()` round-trips through `ReplayReader` (written here as the minimal reader; #454 grows it), and
the measured beat volume is written into plan section 5.

## Scope

### Format (`FutureOfDarkGrimness/Replay/`)
- `ReplayHeader`, `ReplayEvent` base + one sealed record per kind (`StoreAddEvent`, `StoreUpdateEvent`,
  `StoreRemoveEvent`, `SnapshotEvent`, `RequestEvent`, `ReplyEvent`, `RequestErrorEvent`, `BeatEvent`,
  `LogEvent`, `MarkerEvent`), `ReplayMarker` enum + payload fields per plan 4.1. `Kind` is a string
  discriminator written explicitly (not Newtonsoft `$type`) so the file is greppable.
- `ReplayFormat.CurrentVersion = 1`; `BuildVersion` = the entry assembly's `InformationalVersion`
  (the same string `Directory.Build.props` drives), `EngineVersion` = the engine assembly's.
- `ReplayWriter.WriteGzip(header, events, Stream)` / `ReplayReader.ReadGzip(Stream)`; the reader refuses a
  `FormatVersion` mismatch with `ReplayFormatException("Replay format N; this build reads M.")`.
- `GameSaveFile.EXTENSION_*` has a sibling `ReplayFile.EXTENSION_NO_PERIOD = "fdgreplay"`.

### Recorder
- `ReplayRecorder : IReplaySource, IDisposable` with `Start(header)`, `Record*` methods per kind,
  `Snapshot(GameDataStore)`, `ExportGzip()`, `Summary` (`Steps`, `Rounds`, `Outcome`, `DurationMs`,
  `SizeBytes`). One lock; `Seq` dense; `T` = ms since `Start`. A failing `Record*` (serialization throw,
  OOM) logs once via the server's `ITextOutput` with `[replay]` prefix and sets `IsRecording = false`.
- `IReplaySource` on `IFDGGame` (`FDGGame_AsLocal` returns the server's recorder; `FDGGame_AsClient`
  returns null until #451) and `ILobbyViewModel.Replay`.
- Attach points, all in the engine:
  - Store: subscribe to `OnDataAddedAsJson/OnDataUpdatedAsJson/OnDataRemoved` exactly as
    `GameDataUpdateSender` does. Snapshot at `GameStart` is taken AFTER `CreateArmies`/terrain (the
    first `RoundStart` marker is the natural point) - record the setup phase as deltas from an initial
    empty-ish snapshot taken right after `FDGServer` finishes world creation. Document which it is.
  - Requests: new `IRequestObserver { OnRequested(TaskID, PlayerID, string requestType, string replyType,
    string displayName, string requestJson); OnReplied(TaskID, string replyJson); OnFailed(TaskID, string
    message); }` field on `RequestMessageSender` (nullable). Call sites: `RequestDecision` after the
    request JSON is built; `OnReceivedReplyMessage` after the `TryRemove` claim; `OnReceivedErrorMessage`
    likewise. Keep the observer call OUTSIDE any lock the sender holds.
  - Beats: `IBeatObserver { void OnBeat(PresentationBeat) }` on `PresentationRelayer` (host) called
    before the beat is relayed. Serialize with the same settings `PresentBeatMessage` uses on the wire
    (see `PresentationBeatSerializationTests`) so `$type` resolves on read.
  - Log: wrap the `ITextOutput` `FDGServer` hands to stages with a decorator that forwards and records.
  - Markers: `IGameContext.ReplayMarkers` (an `IReplayMarkerSink` with a default no-op). Calls:
    `SingleRoundStage.Enter` -> `RoundStart(round)` then `recorder.Snapshot(store)`;
    `DeterminePlayerTurnStage.Enter` (right where #052 writes the rolling `GameProgressData`) ->
    `ActivationStart(round, activationIndex, playerId, unitRef)` - note the unit is not yet chosen at
    the top of that stage; emit the marker when the unit IS known (after `TryAdvanceToNextPlayer` picks,
    or at `SingleTurnStage.Enter`) and say which in Decisions; `FDGServer.RaiseGameCompleted` ->
    `GameEnded(outcome, message)`. `Saved`/`Resumed` are #453's.
  - Each marker is also broadcast to clients as `ReplayMarkerMessage` (new message in
    `Network/Messages/`; register it in `MessageRegistrar` and the allowlist the way `GameEndedMessage`
    is) so #451 records identical markers. Snapshot markers carry only the index, never the save JSON
    (clients snapshot their own store).
- `FDGServer`: both constructors take `ReplayRecorder? replay = null`. Real-game callers pass one:
  `LobbyViewModel_Host` (both launch paths, lines ~771 and ~923), `CliApp` (both), `Program.cs`
  `--scenario`, `ScenarioLauncher.BuildResume` if it owns construction. `SimulationService`, FdgLab,
  tests: untouched (null).
- D5: in `FDGServer` before `GetDiceRoller`, `if (settings.DiceSeed == null) settings.DiceSeed =
  Random.Shared.Next()` - verify the materialised seed is the one that lands in
  `GameProgressData.Settings` (the save) and in the replay header, on both constructors. Check
  `GameProgressUtilities.Capture` copies it.
- `GameProgressData.GameId` (Guid, appended LAST - TypeID order and field order both matter to saves;
  see #052's note on appending) set once at new-game creation, preserved on resume (`Capture` must copy
  it). Header `GameId` comes from here so host and client recordings of one game share it.

### Tests (`FutureOfDarkGrimness/Tests/ReplayRecorderTests.cs`, `ReplayFormatTests.cs`)
- Header + each event kind round-trips through writer/reader (gzip) with `Seq`/`T` intact.
- Reader refuses `FormatVersion + 1` with the message above.
- Scripted recorder: mutate a `GameDataStoreBuilder.GetDefault()` store (add a unit, move a model, update
  a token), call the request observer by hand with a `YesNoRequest` JSON and a reply, record two beats and
  a log line, snapshot; export; read back; assert event order, counts and that applying the `Store*`
  events onto a store loaded from the snapshot... (leave the apply assertion to #454 - here assert the
  recorded JSON equals what `GameSaveSerializer.Save` of the live store contains for that reference).
- Real game: mirror `DeterminismTests`' in-process game with a recorder attached: >= 1 `RoundStart` per
  round played, exactly one `GameEnded`, every `RequestEvent` has a `ReplyEvent` or `RequestErrorEvent`,
  `Snapshot` count == rounds + 1, header `Seed` non-null even when the spec passed none.
- Negative: a recorder whose `Record*` throws (inject a failing serializer) disables itself and the
  game still completes.

### Measurement (writes into `docs/replay-plan.md` section 5)
- Headless app smoke with the recorder (`printf "2\n2\n" | dotnet run ... --headless`) plus a temporary
  `--replay-stats` print (or the FdgLab probe at `~/Projects/fdg-lab-scratch/replay-probe/` with a
  presenter wired): beat count and bytes for a 2k game. Replace the "estimate" row.

## Verification
Engine suite green; `dotnet build`; headless smoke exits 0; a GUI local game still plays (nothing
user-visible changes in this ticket). Commit on `feature/replay` (engine first, then bump).

## Notes
- 2026-10-30 - Slice 4 (app wiring): `CliApp.RunAsync` (seed materialised before the AI player is built,
  recorder attached to the human game and passed to the server), `CliApp.RunScenarioAsync` and the GUI
  `--scenario` path in `Program.cs` (recorder on the resume constructor, attached to the human game),
  `ScenarioLauncher.BuildResume` materialises a scenario's missing seed before building its AI players.
  `ScenarioLauncher` does not construct the server, so it takes no recorder. The seed helper moved from
  `FDGServer` (internal) to a public `GameSettings.WithConcreteSeed()` so the app can call it; +1 engine
  test for it. Engine suite 3422 passed, 1 skipped; app suite 3059 passed; `dotnet build` clean; headless
  smoke exit 0 (`Game result: outcome=Tie winner=none rounds=4`); headless `--scenario
  Scenarios/example-shootout.json` exit 0 (`outcome=Win winner="Player 1" rounds=4`); no `[replay]` lines.
- 2026-10-30 - Slice 3 (engine wiring): `RequestMessageSender.Observer` (request before any send, so a
  synchronous AI reply can't be recorded first; reply / error / disconnect-failure after the claim and
  before the stage resumes), `PresentationRelayer.Observer`, `IGameContext.GameId` + `ReplayMarkers`
  (`IReplayMarkerSink`, default `NullReplayMarkerSink`), `ReplayMarkerRelay` (FDGServer's sink: numbers
  activations, snapshots after GameStart/RoundStart, broadcasts every marker and snapshot cue as the new
  `ReplayMarkerMessage`), marker calls in `ReconcileNewRoundStage` / `DeterminePlayerTurnStage` /
  `MainUnitActionStage`, `FDGServer(..., ReplayRecorder? replay = null)` on both constructors (header from
  the slots + store armies, `ReplayTextOutput` around the players' log, GameStart + snapshot 0 just before
  the state machine launches, GameEnded then close in `RaiseGameCompleted`), `GameProgressData.GameId`
  (appended last; `Capture` copies it from the context, `WriteProgress` never clears it, `StoreClone`
  copies it), D5 seed materialisation on both constructors (`FDGServer.WithConcreteSeed`),
  `IFDGGame.Replay` (`FDGGame_AsLocal.AttachReplay`; client null until #451), `ILobbyViewModel.Replay`,
  both `LobbyViewModel_Host` launch paths build a recorder (new-game path also materialises the seed for
  its bots), host lobby `Dispose` stops recording, client lobby registers a no-op `ReplayMarkerMessage`
  handler. Tests: `Tests/ReplayRecorderGameTests.cs` (6) - an unseeded fresh in-process AI game records a
  structurally complete replay (header seed == save seed, GameId == progress == server, dense Seq, GameStart
  first with no round, exactly one GameEnded and it is last, RoundStart 1..N each followed by a snapshot,
  snapshots == rounds + 1, every request closed exactly once and after itself, activations numbered 1..n
  per round with each unit-naming repeat matching its boundary and naming the actor's unit, beats / logs /
  updates present, every marker and snapshot cue broadcast in order, export == recorded); a seeded game
  keeps its seed; a recorder with a failing beat serializer disables itself and the game still plays all
  four rounds (clients still get the markers); a resumed scenario mints a GameId and seed, starts the
  recording in round 2 with no RoundStart of its own and activation numbering from 1; a preset GameId
  survives the resume; `ReplayMarkerMessage` round-trips the wire. `GameProgressTests` +4 (Capture
  carries the context's GameId, a rewrite keeps it, save/load keeps it, a pre-#450 record loads as
  Empty). First full run caught four `SimulationServiceTests` / `StoreCloneTests` byte-identity failures:
  the resume constructor minted a fresh GameId per simulation run; fixed by never minting for a
  simulation (see Decisions). Engine suite 3421 passed, 1 skipped; `dotnet build` clean; headless smoke
  exit 0, `Game result: outcome=Tie winner=none rounds=4`.
- 2026-10-30 - Slice 2 (recorder, scripted): `ReplayRecorder` (one lock, dense Seq, T from a stopwatch,
  `Start`/`AttachStore`/`Record*`/`Snapshot`/`ExportGzip`/`Summary`/`Dispose`), `IReplaySource` +
  `ReplaySummary`, `IRequestObserver`, `IBeatObserver`, and `ReplayTextOutput` (the log decorator; named so
  it does not collide with the tests' `RecordingTextOutput` double). `Tests/ReplayRecorderTests.cs` (10):
  the ticket's scripted sequence (army adds, model move, token update, YesNo request/reply, two beats, a
  log line, a snapshot) exported and read back - order, counts, dense Seq, monotonic T, every setup add and
  both updates byte-equal to the store's own save, beats read back as their concrete types, snapshot ==
  `GameSaveSerializer.Save` and loadable; markers stamped and summarized; nothing before Start; Start twice
  refused; failing snapshot / failing beat serializer / throwing failure sink all disable once and never
  throw; Dispose detaches but keeps the data; the decorator keeps the Debug category; 4000 concurrent
  appends keep Seq dense. Engine suite 3411 passed, 1 skipped.
  Found on the way, for #454: `ModelData` serializes a read-only `Position` derived from its
  `PositionBinding`, so a ModelData entry's JSON goes stale when only its Position entry is rewritten (no
  ModelData update fires). Load ignores the field, so playback is unaffected - but never diff ModelData
  JSON against a live save to judge playback; compare whole re-saved stores.
- 2026-10-30 - Slice 1 (format): `FutureOfDarkGrimness/Replay/` gains `ReplayFormat` (version 1, build/engine
  stamps, the container's JSON settings), `ReplayFile` (extension), `ReplayFormatException`, `ReplayHeader` +
  `ReplaySlot`, the ten event records, `ReplayWriter.WriteGzip` and the minimal `ReplayReader.ReadGzip`.
  `Tests/ReplayFormatTests.cs` (11): header and every kind round-trip with Seq/T, a guard that the fixture
  covers every registered Kind, line shape (Kind right after Seq/T, no CLR type names, enums by name, nulls
  omitted), verbatim embedded JSON and date-shaped strings, refusal of FormatVersion+1 (exact message), of
  non-gzip, of an empty file, and of unknown/missing Kinds. Engine suite 3401 passed, 1 skipped.

## Decisions
- **Where `ActivationStart` is emitted - twice per activation.** Neither suggested point knows the unit
  (`TryAdvanceToNextPlayer` picks the PLAYER; `SingleTurnStage.Enter` runs before `ChooseUnitToActivate`),
  and the only point that does (`MainUnitActionStage.Enter`) comes after the activation's first decisions
  (Martial Prowess reactivation offers, the unit pick, the Delayed Action offer), which would then be
  attributed to the previous activation. So: (1) at the activation boundary in `DeterminePlayerTurnStage`,
  right after the #052 rolling snapshot and the simulation stop check, with `Round`, `Activation`,
  `PlayerId` and `UnitRef` null; (2) REPEATED from `MainUnitActionStage.Enter` with the same
  Round/Activation/PlayerId and the committed `UnitRef` - again after a #248 back-out re-pick. Append-only,
  every decision of an activation follows its boundary, and the unit is recorded the moment it is known. A
  Delayed Action turn has no repeat (no unit activated). Rule for #451/#454: an ActivationStart repeating
  the open (Round, Activation) is not a new activation; it names (or re-names) its unit.
- **Activation numbering** is the recording side's count of boundaries since the last RoundStart (the
  relay's counter, not engine state), so after a resume it restarts at 1 within the resumed round.
  Persisting a counter for exact numbering across a resume is #453's call if it wants it.
- **`RoundStart` is emitted from `ReconcileNewRoundStage.Enter`, not `SingleRoundStage.Enter`** (a
  deviation from the ticket text): `RoundCount` is already advanced there, and the round banner,
  round-start token grants and reserve arrivals - decisions included - run in
  `StartOfRoundExtraActionStage` BEFORE `SingleRoundStage`. Marking at `SingleRoundStage` would put those
  under the previous round and snapshot after them. A resume enters `SingleRoundStage` directly, so the
  resumed round gets no RoundStart; `GameStart` carries its `Round` instead.
- **Where the first snapshot is taken:** `GameStart` + snapshot 0 at the end of `FDGServer`'s world
  creation (`BuildContextAndLaunch`, after `CreateArmies` and the creation rules, just before the state
  machine launches). The store subscription starts at the same moment, so map setup (terrain, objectives)
  and deployment are recorded as deltas on snapshot 0, and round 1's RoundStart snapshot follows them. On
  resume / scenario launch snapshot 0 is the loaded store and `GameStart.Round` is the resumed round. #453
  will add `Resumed` for a resume that continues an embedded replay.
- **`GameEnded` closes the recording.** `RaiseGameCompleted` records it (once, however many paths report
  the end), broadcasts it ahead of the lobby's `GameEndedMessage`, then disposes the recorder, before
  `OnGameCompleted` subscribers (recovery save, teardown) run - so GameEnded is always the last event.
- **`GameId` vs the lobby's `LaunchedGameId` (#412):** separate. The launch ID is per launch (a resume
  gets a new one); the replay needs an identity that survives save/resume. `GameProgressData.GameId` is
  minted by `FDGServer`'s new-game constructor, carried through `GameContext.GameId` into every
  `Capture`, never cleared by `WriteProgress`, and minted by the resume constructor only when the store
  has none (pre-#450 save, compiled scenario). **Never minted for a simulation** - a simulation is a
  throwaway copy of a game, and its runs must stay byte-identical (`SimulationServiceTests`).
- **D5 seed:** `GameSettings.WithConcreteSeed()` on both `FDGServer` constructors, before anything reads the settings,
  written back to the progress record on resume. Additionally the lobby's new-game launch, `CliApp.RunAsync` and
  `ScenarioLauncher.BuildResume` materialise it BEFORE building the AI controllers, so the bots derive their streams from the
  same seed the dice use - otherwise a re-run with the recorded seed would replay the dice but not the AI.
  Launch-local copy: the lobby's own setting stays "random" for the next game. (Observation, not changed:
  `LaunchResume` seeds resumed bots from the LOBBY's seed, not the save's - pre-existing.)
- **`ReplayMarkerMessage`** carries the marker's fields flat plus `SnapshotIndex` for a snapshot cue
  (never the save JSON). It is registered on clients (a no-op handler in `LobbyViewModel_Client`, next to
  `GameEndedMessage`) so the type is known; #451 replaces the no-op with the client recorder. The first
  markers go out during `FDGServer` construction, i.e. before `LaunchGameMessage` - #451 must not rely on
  receiving GameStart / snapshot cue 0 (its own join snapshot is its snapshot 0 anyway).
- **Requests closed on disconnect:** `FailPendingRequestsForPlayer` also reports `OnFailed`, so every
  recorded request ends in a Reply or a RequestError even when a player drops.
- **Kind discriminator.** `ReplayEvent.Kind` is a computed property serialized right after `Seq`/`T`
  (`JsonProperty(Order)`), so every line starts `{"Seq":n,"T":ms,"Kind":"..."`. The reader resolves Kind
  through a fixed table (`ReplayEventKinds`); the container's settings have NO `TypeNameHandling`, because a
  replay file is untrusted input. The embedded payload strings stay opaque here and are deserialized later
  through the store's / wire's allowlist binder.
- **Payload JSON stays an escaped string**, not a nested raw object: a string round-trips byte-exact (raw
  embedding would re-format floats on read). Costs some escaping; gzip absorbs most of it.
- **Container JSON settings:** enums by name, nulls omitted (marker lines stay short), dates UTC, and
  `DateParseHandling.None` so a date-shaped log line is not re-formatted by the Kind converter's JObject.
- **Failure policy.** The first failing `Record*`/`Snapshot` logs ONE `[replay] Recording stopped: ...`
  line (ASCII-folded) to the failure sink `FDGServer` passes to `Start` - the players' log, never the
  recording decorator - and turns recording off. Events recorded before the failure stay exportable; the
  stream just ends without a GameEnded marker. `ExportGzip` before `Start` throws (a wiring error, and it
  is only ever called from the app, never the engine thread).
- **Snapshot index counts calls**, recorded or not, so the index a marker broadcast carries to clients
  stays aligned with what they count even after the host recorder disabled itself.
- **`ReplaySummary.Steps`** counts answered requests; #454's step filter (fold requests with <= 1 option)
  is NOT applied here. **`SizeBytes`** is the approximate UNcompressed stream size (payload characters + a
  per-line allowance), cheap enough to read every frame; the file size is the exported byte count.
- **Beat serializer settings:** `WireJsonSettings.For(store)` (adopted at `AttachStore`), serialized
  declared as `PresentationBeat` so the root carries `$type` - the same bytes `PresentBeatMessage` puts on
  the wire. **Requests need no settings of their own:** the recorder stores the body string
  `RequestMessageSender` already built with `WireJsonSettings.For(store)`, so it is byte-identical to the
  wire payload.
- **`LogEvent.Debug`** (omitted when false) is one field beyond plan 4.1's `Line`: the live log hides
  Debug-category lines unless the toggle is on, and the viewer needs the same flag to do likewise.

## Outcome
(open)
