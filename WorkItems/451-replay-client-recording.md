# 451 — Engine: decision payloads to clients + ClientReplayRecorder

**Status**: todo
**Related**: plan `docs/replay-plan.md` section 4.2 (client column); depends on #450; reverses part of #088 (decision D2); #452 writes what this records

## Goal
A remote client that plays a networked game ends up with a `ReplayRecorder` equivalent to the host's: same
`GameId`, same markers, every store delta it was sent, every request/reply pair of EVERY player (not just
its own), every beat and log line, with the join-time full sync as `Snapshot` #0 and a snapshot at each
`RoundStart` marker. Done when a loopback host+client test (`LoopbackNetwork`,
`NetworkedFullStateSyncTests` are the models) plays a short game and the client's recording, played back
by #454's `ReplayPlayer` (or, before #454 exists, re-applied with `StoreReplay`-style logic in the test),
reaches the same final store JSON as the host's.

## Scope
- `StageTaskNotifyResolvedMessage(TaskID)` becomes `StageTaskNotifyResolvedMessage(TaskID, PlayerID,
  string RequestFullTypeName, string ReplyFullTypeName, string DisplayName, string RequestJson,
  string? ReplyJson, string? ErrorMessage)`. Both send sites in `RequestMessageSender` (reply path ~line
  159, fault/disconnect path ~line 225) must fill it - the sender must therefore keep the request JSON
  and type names in its pending entry (`SuccessAndFailActions`) until the reply arrives. Update
  `MessageSerializationTests` / `NetworkProtocolTests` pins. Record in Decisions that this deliberately
  loosens #088's payload privacy AFTER the decision resolved (D2), and keep the awaiting message as is.
- Bandwidth sanity: ~0.7 MB of request JSON + ~0.2 MB of replies per 2k game spread over the whole game
  (plan section 5). Nothing to do unless a message exceeds `CommandProtocol.MAX_PAYLOAD_BYTES` (it will
  not; the largest request measured was 8 KB).
- `ReplayMarkerMessage` (created in #450) -> client records the marker; on `RoundStart` the client also
  snapshots ITS store (`GameSaveSerializer.Save` of the client store - it is a faithful mirror, see
  `StoreReplay`'s doc comment). On `GameEnded` the client records the marker; the existing
  `GameEndedMessage` handling is unchanged.
- `ClientReplayRecorder` (engine, `Replay/`): constructed inside `FDGGame_AsClient` next to
  `GameDataUpdateReceiver`; subscribes to `AddSingleDataMessage`, `UpdateSingleDataMessage`,
  `RemoveSingleDataMessage` (-> `Store*` events), `AddAllDataMessage` (-> `Snapshot` #0, built by loading
  the entries into a scratch store and saving it, OR by recording the entries verbatim and letting the
  reader treat an "entries" snapshot like a save - pick one, prefer the former so every snapshot has one
  shape), `StageTaskRequestMessage` (own requests, -> `Request`), `StageTaskNotifyResolvedMessage` (->
  `Request` if not already recorded for that `TaskId`, then `Reply` or `RequestError`),
  `PresentBeatMessage` (-> `Beat`), `LogChatNetworkMessage` (-> `Log`), `ReplayMarkerMessage`.
  Header: `RecordedBy = Client`, `RecordedByPlayerId = thisPlayerID`, `GameId` from the synced
  `GameProgressData`, slots from the synced `PlayerSlotInfo`s, seed from `GameProgressData.Settings`.
  Start recording the moment the client game is constructed (before the full sync arrives) so nothing
  is missed; the header's late-bound fields are filled when the sync lands.
- `FDGGame_AsClient.Replay` returns it; `LobbyViewModel_Client.Replay` forwards.
- Ordering caveat to handle: on the client, the `StageTaskNotifyResolvedMessage` for a decision may
  arrive before or after the store deltas that decision caused (different send calls, same TCP stream -
  so actually in send order; verify and document). Playback (#454) keys spans on the `Request` event's
  `Seq`, so as long as the client records in arrival order the spans are right.

## Tests (`Tests/ClientReplayRecordingTests.cs`)
- Loopback host + one client, a two-unit game with AI on both slots or scripted resolvers (see
  `ResumeRejoinNetworkTests` for how a client is driven in-process): after `GameEnded`, the client
  recording has the same `GameId`, the same marker sequence, the same set of `TaskId`s as the host
  recording, `Snapshot` count == rounds + 1, and re-applying its `Store*` events from `Snapshot` #0
  yields the host's final `GameSaveSerializer.Save` (compare after normalising `GameProgressData`'s
  rolling fields if they differ by design - document).
- `StageTaskNotifyResolvedMessage` serialization pin with every new field.
- A client that joins a game already in progress (resume rejoin) gets `Snapshot` #0 from the
  `AddAllDataMessage` and records from there (ties into #453's history hand-off; here just assert the
  recording starts cleanly).

## Verification
Engine suite green; `dotnet build`; headless smoke; one real networked game between two processes on
one machine (host GUI + client GUI, or host headless + client GUI) and confirm both `ILobbyViewModel.Replay`
report the same step count at game end (print it to the console for now; #452 writes files).

## Notes
- (none yet)

## Decisions
- (record: snapshot #0 shape; message arrival-order finding; what #088 text was changed)

## Outcome
(open)
