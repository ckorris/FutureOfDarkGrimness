# 453 — Replay inside .fdgsave: save embeds, resume continues, clients receive history

**Status**: todo
**Related**: plan `docs/replay-plan.md` sections 4.1 (`Saved`/`Resumed` markers), 4.5; depends on #450, #451; touches #052 (save/resume), #054 (clients cannot save), #270 (re-save after resume)

## Goal
Saving a game writes the replay-so-far into the `.fdgsave`; loading and resuming it continues the same
replay (same `GameId`, `Seq` continues, a `Saved` marker at the save, a `Resumed` marker + fresh snapshot
at the resume) on the host AND on any client that joins the resumed game; the final `.fdgreplay` of a
saved-quit-loaded-finished game is one continuous file. Saves without a replay block (scenarios, old
files, `StoreSnapshot.ToJson`) still load. Done when the engine tests below pass and a GUI save -> quit ->
load -> finish produces one replay whose dump (#455) shows `... Saved, Resumed, Snapshot, ... GameEnded`.

## Scope
- `GameSaveFile.Replay` (`string?`, base64 of the gzip container; `GameSaveSerializer.CurrentVersion`
  stays 2 - the field is optional on read) + `GameSaveFile.ReplaySummary` (`ReplaySummary?`, so a browser
  can list saves without decompressing).
- `GameSaveSerializer.Save(GameDataStore store, IReplaySource? replay = null)`; `Load(string) ->
  GameDataStore` unchanged; new `LoadWithReplay(string) -> (GameDataStore Store, byte[]? ReplayGzip)`.
  Callers that must NOT embed: `StoreSnapshot.ToJson`, `JsonSnapshot`, `SimulationService.Snapshot`,
  `ScenarioCompiler`, `StoreClone`'s doc comment reference - leave them on the default null.
- Save path: `LobbyViewModel_Host.SaveGameToJson()` records a `Saved` marker and passes the recorder.
  (Keep the #054 caveat comment; the recorder lock makes the replay part of the snapshot consistent
  even if the store part is not.) `Program.cs` `--scenario` `saveGameToJson` lambda: same.
- Resume path: `Program.LoadGameFlow` uses `LoadWithReplay`; `LobbyViewModel_Host`'s resume constructor
  takes `byte[]? priorReplay`; on `LaunchResume` the new `ReplayRecorder` is constructed with
  `ReplayRecorder.Continue(priorReplay)` (parses the prior container, keeps header `GameId`/`Seed`/slots,
  continues `Seq`, records `Resumed`, then the fresh `Snapshot` after `FDGServer` finishes restoring).
  Header `StartedUtc` stays the original; add `ResumedUtc[]`. If the prior bytes fail to parse (format
  bump between save and load), log `[replay]` and start a fresh recording with a `Resumed` marker - never
  block the resume itself.
- Clients of a resumed game: host sends `ReplayHistoryMessage(byte[] Gzip)` to each connection on launch
  (right after the `LaunchGameMessage`, or on `PostLaunchPlayerReadyMessage` - pick the point where the
  client's `ClientReplayRecorder` already exists) so the client's recorder seeds from the same history and
  its `Snapshot` #0 (the `AddAllDataMessage`) becomes the post-`Resumed` snapshot. Size: a resumed 2k
  game carries ~0.5 MB, well under `CommandProtocol.MAX_PAYLOAD_BYTES` (16 MB), but assert that and fall
  back to "start fresh at `Resumed`" with a log line if the history is ever larger.
- **From #451:** the client's `ClientReplayRecorder` is built in `FDGGame_AsClient`'s constructor, i.e. while the
  client handles `LaunchGameMessage`, before it even asks for the join sync - so a history message sent right after
  the launch message finds it. It is started with its header at construction (`ReplayRecorder.Start` runs once), so
  seeding from history needs a new entry point that takes the prior events (and their header) before snapshot 0.
  The launch message already carries the host's header (`LaunchGameMessage.Replay`). A rejoining client's
  recording today simply starts at its join snapshot (pinned by `ClientReplayRecordingTests`).
- Half-activation semantics: document in `docs/replay-plan.md` 4.1 (already there) and in the recorder
  code that deltas between the last `ActivationStart` and `Saved` were undone by the resume; #454's
  player never applies across `Resumed`.
- `CliApp` resume path (`FDGServer` resume constructor at ~line 160) and `ScenarioLauncher.LoadStore`
  take the same `LoadWithReplay` route; a compiled scenario has no replay so it starts fresh.
- Handed over from #455 (2026-10-30): the `--replay` CLI (`FdgRaylib/Cli/ReplayTool.cs`) must accept a
  `.fdgsave` through `LoadWithReplay` (today `Loaded.Open` refuses one, naming #453), and `--at STEP
  --make-save` must embed the replay truncated at that step plus a `Saved` marker, so resuming the cut save
  continues one replay (today it prints "the save carries no replay"). Extend #455's
  `ReplayToolTests.MakeSave_AtStep3_WritesTheStateThere_AndItResumes`: `LoadWithReplay` on the cut save
  returns a replay whose last marker is `Saved`.

## Tests
- `Tests/GameSaveLoadTests.cs` additions: save with a recorder -> file has `Replay` + `ReplaySummary`;
  `Load` still works; `LoadWithReplay` returns the bytes; save with null recorder has `Replay == null`;
  a hand-written v2 save JSON without the field loads.
- `Tests/ReplayResumeTests.cs`: record a short in-process game to mid-round-2 (use the resume test
  fixtures from `ResumeRoundCountTests`/`ResumeSettingsOverrideTests`), save with replay, load, resume
  with `ReplayRecorder.Continue`, finish; assert one `GameId`, `Seq` strictly increasing across the
  boundary, markers `... Saved, Resumed, Snapshot ...`, `Snapshot` count == rounds + 1 + 1, and the
  resumed recording's final store equals the live final store.
- `ReplayHistoryMessage` serialization pin + a loopback test that a joining client's recorder has the
  prior events before its `Snapshot` #0 (`ResumeRejoinNetworkTests` is the model).

## Verification
Engine suite; `dotnet build`; headless: `--make-scenario` -> `--scenario` still works (no replay in a
compiled scenario); GUI: save mid-game, Esc -> Load the save, finish, check `Replays/` has one file and
`--replay FILE --dump` (#455, or a temporary print) shows the marker sequence.

## Notes
- (none yet)

## Decisions
- (record where `ReplayHistoryMessage` is sent and how the client merges it with its own `Snapshot` #0)

## Outcome
(open)
