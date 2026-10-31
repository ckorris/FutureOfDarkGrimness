# 450 — Engine: replay format + host-side ReplayRecorder

**Status**: todo
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
- (none yet)

## Decisions
- (record here: where exactly `ActivationStart` is emitted; where the first snapshot is taken; the
  serializer settings used for beats and whether `WireJsonSettings.For(store)` is needed for requests)

## Outcome
(open)
