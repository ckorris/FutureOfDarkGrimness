# 451 — Engine: decision payloads to clients + ClientReplayRecorder

**Status**: in progress (slices 1-2 of 3 done)
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
- **2026-10-31 - slice 2: `ClientReplayRecorder` (engine `bc804b5f`).** `Replay/ClientReplayRecorder.cs`, built in
  `FDGGame_AsClient`'s constructor between the `GameDataUpdateReceiver` and its catch-up request; exposed as
  `FDGGame_AsClient.Replay` and `LobbyViewModel_Client.Replay` (disposed with the lobby). It wraps a
  `ReplayRecorder` (same lock, Seq, tallies, export, failure policy) and translates messages into its `Record*`
  calls; `ReplayRecorder` gained `Snapshot(store, index)` and `RecordBeatJson`, and `Fail` became internal.
  `GameDataUpdateReceiver.OnFullStateApplied` fires after the join rebuild -> snapshot 0. `LaunchGameMessage.Replay`
  carries the host recorder's header (both lobby launch paths). `ReplayHeader.Settings` is now nullable (a client
  without a host header). `LobbyViewModel_Host.PresentationClockFactory` is a new internal test seam: the lobby
  launches with the real-time clock, which made the two-unit networked test take 41 s instead of 2.
  Tests (`ClientReplayRecordingTests`, all over the loopback transport with lobby VMs and the solo-rules AI on both
  sides): a fresh networked game - same GameId/seed, client's markers == host's minus GameStart, same TaskIds and
  step count, the same beats and log lines in order, rounds + 1 snapshots with the host's indexes AND identical
  SaveJson at every index, played-back final save == the host's live final save, `ReplayVerifier` PASS; a resumed
  game a client rejoins - its first event is snapshot 0 and equals the host's resumed snapshot 0, GameId minted
  and shared, final state equal; a scripted message sequence for the bookkeeping (dedupe, error close, cue before
  join, GameEnded stops). No normalisation of `GameProgressData` was needed: the client's snapshots are
  byte-identical to the host's. Engine 3462 passed / 1 skipped, app 3066, build clean, smoke exit 0.- **2026-10-31 - slice 1: the resolved notice carries the decision (engine `f24bdef6`).**
  `StageTaskNotifyResolvedMessage` has the ticket's eight fields; `RequestMessageSender` keeps the request half
  (`ResolvedDecision`: type names, display name, request JSON) in its pending entry and fills the notice on all
  THREE ways a task ends - reply, resolver error, disconnect. `NetworkProtocol.Version` 4 -> 5. Tests:
  `RequestSystemTests` (one per ending, exact-record equality), `ClientReplayRecordingTests` (wire round trip
  of an answered and a failed notice). There were no existing `MessageSerializationTests` /
  `NetworkProtocolTests` pins on this message to update. Engine 3459 passed / 1 skipped, build clean, smoke
  exit 0.
- **Ordering, verified by reading the code (#086 write lock, `RequestMessageSender`):** per connection, frames
  arrive in send order - every send to one connection takes its `WriteLock` (a `SemaphoreSlim`, whose async
  waiters are released FIFO) before the sending call returns. On the reply path the host sends the resolved
  notice BEFORE it completes the awaiting stage (`actions.OnSuccessful`), so the notice always precedes the
  store deltas the decision causes. A broadcast walks connections one after another, so with several clients a
  later broadcast could in theory overtake an earlier one on the second connection - the same property every
  replicated store delta already relies on, so not new here.

## Decisions
- **The error path broadcasts too** (beyond the ticket's two send sites): a resolver error
  (`StageTaskRequestErrorMessage`) used to end the task with NO resolved notice, so the outstanding-task entry
  lingered on every client and a client replay could never close another player's failed request. It now
  sends the notice with `ErrorMessage` set and `ReplyJson` null, in the same place the reply path does (after
  the observer, before the awaiting stage resumes).
- **`PlayerID` in the notice is the host's record of the target** (the pending entry), not the replier's
  claim in `StageTaskReplyMessage.PlayerID`.
- **#088 text changed:** only the routing comment in `RequestMessageSender.RequestDecision` ("no client
  receives another player's request payload while the decision is open; once it resolves the notice carries
  it to everyone"). The message's own doc comment records the D2 loosening. The awaiting message is unchanged.
- **Header source (Chris, 2026-10-31): the launch message.** The ticket said "GameId from the synced
  `GameProgressData`", but in a new game that record only reaches the client at the first activation (after
  setup and deployment), so a game abandoned earlier would have no game ID or seed. `LaunchGameMessage.Replay`
  carries the host recorder's `ReplayHeader` instead; the client copies GameId, Seed, RandomnessType, Settings
  and the slots from it. No host header (its recording failed to start) -> empty GameId, null Settings, no slots.
- **Controllers from the client's point of view: own slot `Human`, every other `Remote`** - no AI distinction
  (Chris: the label is cosmetic, playback never reads it, and a save/load can swap AI and human anyway).
- **Snapshot 0 = a save of the client's own store right after the join rebuild** (one snapshot shape), taken
  on `GameDataUpdateReceiver.OnFullStateApplied` rather than by ordering bus handlers; no scratch-store rebuild.
  Snapshot cues are saves of the client store under the HOST's index (so both recordings number a round's
  snapshot alike); a cue that arrives before the join snapshot is ignored. The host's GameStart and cue 0 go out
  before the launch message, so a client never records them - its join snapshot stands in.
- **Where another player's request lands:** at its resolved notice, next to its reply (the awaiting notice
  carries no payload, D2). Deltas made while that decision was open - only when two decisions are in flight at
  once, as in army setup - therefore precede it in a client recording and follow it in the host's. Own requests
  are recorded on arrival, with the display name from the awaiting notice (`StageTaskRequestMessage` has none).
- **`GameEnded` closes the client recording** (the recorder disposes itself), mirroring the host.
- **Protocol v5** covers both replay wire changes (#450's `ReplayMarkerMessage` never bumped it): an older
  peer would otherwise join and record half-empty replays instead of being refused with a reason.

## Outcome
(open)
