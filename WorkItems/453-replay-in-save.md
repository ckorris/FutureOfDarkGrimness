# 453 — Replay inside .fdgsave: save embeds, resume continues, clients receive history

**Status**: done 2026-10-31 (engine 46ebed39 on `feature/replay`)
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
- **From #452:** `ReplaySave` writes one file per `IReplaySource` instance and rewrites it while that recording's
  `Summary` changes. Escape-menu Load writes the old game's file in `OnGameExited` before the teardown, then loads;
  today the resumed game gets a new recorder and so a new file, and the earlier session's file is kept. Once a
  resumed recording continues the saved history, its file is a superset of the earlier one - both stay (F3); say so
  in the Outcome rather than deduplicating.
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
- 2026-10-31 - Built and verified.
  - Engine: `GameSaveFile.Replay` / `ReplaySummary`; `GameSaveSerializer.Save(store, IReplaySource?)` and
    `LoadWithReplay`; `FDGServer.SaveGame()`; `ReplayRecorder.Continue` / `CutForSave` / `ExportThroughResume`;
    the marker relay's `Resumed` and `Saved`; `LaunchGameMessage.ReplayHistory`; `ClientReplayRecorder` seeded from
    it; verifier and `ReplayTimeline.GameEndedMarker` per session. Replay `FormatVersion` 1 -> 2 (plan rule 5: the
    header gained `ResumedUtc`), so replays recorded before today no longer open.
  - App: `Program.LoadGameFlow`, both `--scenario` paths and `CliApp.RunScenarioAsync` continue a save's replay
    (`ScenarioLauncher.LoadStoreWithReplay`); the `--scenario` GUI save goes through the server. `--replay` reads a
    `.fdgsave`'s replay; `--at STEP --make-save` embeds the cut. The escape menu now serializes only after the file
    is chosen, so a cancelled Save Game leaves no `Saved` marker behind. The `--replay` header's `Rounds` counts any
    marker's round, so a recording that starts mid-game no longer reads "Rounds 0".
  - Tests: engine 3483 passed / 1 skipped (+20: `ReplayResumeTests` 13 - a real game saved at round 2 from inside
    the marker broadcast, loaded, resumed, finished; a save written after the end resumed again; scripted recorder
    and verifier cases - plus 4 `GameSaveLoadTests`, the launch-message wire pin, and 2 in
    `ClientReplayRecordingTests`: a client joining a resumed save over the loopback transport, and a scripted
    seeding case). App 3083 (+1, `ReplayToolTests` extended: the cut save embeds the replay, resumes it, the
    continued file verifies). Full build clean of new warnings.
  - Headless: the smoke exits 0 with `Game result:` and `Replay saved to:`; `--at 35 --make-save` on its replay ->
    `--scenario cut.fdgsave --all-ai` writes one replay whose markers read `GameStart, RoundStart R1, RoundStart R2,
    Saved R2, Resumed R1, Snapshot 3, RoundStart R2, ... GameEnded`, `--verify` PASS. (The cut was at round 2's
    start-of-round Ambush decision, which the #052 save point re-enters from round 1's last activation boundary -
    the replay shows exactly that.) `--make-scenario` -> `--scenario` still starts at `GameStart`.
  - GUI (Xephyr :5, no window manager - zenity dialogs need `xdotool windowfocus` before typing): a round-4 scenario
    waiting on the human -> Esc -> Save Game -> Esc -> Load Game (same file) -> RESUME -> Marksmen -> Pass -> the AI
    finished -> the game-over card named one file whose markers read `GameStart R4, Snapshot 0, Saved R4, Resumed R4,
    Snapshot 1, ... GameEnded`; `--verify` PASS with the two expected notes (the unit pick open at the save, asked
    again; the post-resume snapshot not compared). The first session wrote no file: it had no answered decision
    (#452's rule). Return to Main Menu and the window close did not rewrite. Kit: `~/Projects/fdg-lab-scratch/replay-453/`.

## Decisions
- **History to clients rides `LaunchGameMessage.ReplayHistory`** (Chris, 2026-10-31, over a separate
  `ReplayHistoryMessage`): one frame, so the client has it before it records anything - a separate message could race
  the join sync over a real network. It is the host's recording through its `Resumed` marker (the host records that
  marker before the launch message goes out, so clients would otherwise miss it); the client seeds with it verbatim,
  and its join snapshot is the snapshot after the resume, under the host's index. Left out (logged) above half the frame
  limit, or when no network player is in the game. A seeded client leaves out store deltas that arrive before its join
  snapshot - none should, the host's engine waits on the client's ready - since playback could not place them.
- **A recovery save keeps its `GameEnded` (Disconnect)** (Chris, 2026-10-31): #187 writes it after the game ended,
  when the recording is closed, so the embedded replay ends with that marker and no `Saved`; resumed, the one replay
  reads `... GameEnded (Disconnect), Resumed, Snapshot, ... GameEnded`. `GameEnded` is now at most one per session (a
  `Resumed` starts the next); the verifier notes an ended session that was resumed, `ReplayTimeline.GameEndedMarker`
  is the last session's end, and `Summary.Outcome` resets at `Resumed`.
- `FDGServer.SaveGame()` is the single save path for a real game (host lobby, `--scenario` GUI, recovery save). The
  `Saved` marker is also broadcast, so clients' recordings mark where the host saved; nothing after the game ended.
- A continued recording keeps the first session's header (game ID, seed, slots, names, build) and appends to
  `ResumedUtc`; `T` counts on from the history's last event (play time, not wall time). A replay of a different game
  ID than the resumed store, or bytes that do not read (a format bump, damage), never block the resume: a fresh
  recording starts at `Resumed` with one `[replay]` line in the game log. A save without a replay resumes with
  `GameStart` as before.
- An unreadable `Replay` block in a save loads the game and hands `Continue` empty bytes, so there is one failure path.
- `--at STEP --make-save` cuts before the step's request (the resumed game asks it again); `--at end` cuts before
  `GameEnded`. Both close with `Saved` (round = the last marker's round).
- A request open at the save is a verifier note, not a failure, when a `Resumed` follows before that player answered
  anything; an answer later in the same session still fails it.
- The half-activation semantics are documented in `ReplayRecorder`'s class comment and plan 4.1 (unchanged): deltas
  between the last `ActivationStart` and `Saved` describe work the resume undid; playback never crosses `Resumed`.

## Outcome
Done 2026-10-31 on `feature/replay` (engine 46ebed39). A save carries the replay so far, closed with a `Saved` marker;
loading it - main menu or escape-menu Load, `--scenario`, headless or GUI - continues the same recording from a
`Resumed` marker and a fresh snapshot, so a saved-quit-loaded-finished game ends as one `.fdgreplay` (verified in the
GUI). The clients of a resumed game receive the history with the launch message and continue it too (pinned
in-process; two machines are #457's). Saves without a replay - scenarios, older saves, snapshots - load and resume as
before. Escape-menu Load still writes the old session's file first (#452) and the resumed game writes a new one; the
new file holds the old one's history up to the save, so after a save-and-play-on both are kept and the new one is not
a strict superset (F3: nothing is deduplicated). The replay format is now v2: replays recorded before today, and any
save carrying one, no longer open (their games still resume, with a fresh recording). Not done here: a two-machine
resumed game and a real disconnect -> recovery save -> resume (both in #457's checklist); a cancelled Save Game dialog
was changed but not run in the GUI.
