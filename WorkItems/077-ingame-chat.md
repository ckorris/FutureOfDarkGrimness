# 077 — In-game chat: finish

**Status**: done (engine relay + in-game chat GUI); awaiting hand-verification in the running window
**Related**: audit §13.20; branch `082-network-robustness` (engine relay), then `082-default-answer` (the GUI, 2026-06-25)

## Goal

Network clients already send `NetworkPlayerSubmitChatMessage` to the host after launch, and the relay (`LogAndChatMessageRelayer`) + per-client receive path (`LogChatMessageEndpoint`) are fully wired — but the host's handler registration in `NetworkPlayerController` was commented out, so in-game chat from network players vanished (lobby chat already worked). Finish it: re-register the handler and relay through to all clients.

## Decisions

- **2026-06-25** — **Finish, not remove** (user-chosen). The whole loop already exists and is instantiated during the game: `FDGServer` builds `LogAndChatMessageRelayer` (subscribes to every controller's `OnMessageSentByPlayer` and broadcasts `PlayerChatNetworkMessage`), and both `FDGGame_AsLocal`/`FDGGame_AsClient` build a `LogChatMessageEndpoint` (sends submissions, displays broadcasts). Only the host-side `NetworkPlayerController` handler was missing — a ~2-line re-enable.
- **2026-06-25** — **Added a sender filter the commented-out code lacked.** Every network player's `NetworkPlayerController` is registered for `NetworkPlayerSubmitChatMessage`, and the old handler raised `OnMessageSentByPlayer` with the controller's *own* `ID`. With 2+ network players that would re-raise (and the relayer re-broadcast) the same chat once per controller, each misattributed. The handler now early-returns unless `message.PlayerID == ID`, so exactly the sender's controller relays it, attributed correctly. (Single-network-player play happened to work before only by coincidence of there being one controller.)
- **2026-06-25 (follow-up — GUI added)** — Closing the engine relay revealed there was **no in-game chat GUI at all**: GUI launch passed a `CliPlayerMessageUI` stub (received chat → `Console.WriteLine`, no in-window display; sending only via an unused `SendMessage`), so the relay fix was correct but unreachable by a player. Added a minimal GUI per the user's spec: new `GuiPlayerMessageUI` (received chat → the shared `GameLog` shown on the side, tagged `[sender] message` in light blue) + a thin chat bar across the bottom of the main game area in `RaylibRenderer.DrawChatInput` (an `InputTextWithHint`, Enter-to-send → `Submit` → `OnMessageSentByPlayer`). Wired via the `OnGameLaunched` callback (LobbyScreen → Program → `TransitionToGame`), replacing the CLI stub. Not auto-focused, so game hotkeys keep working until the player clicks in. App-side only (no engine change). Host-vs-AI displays each line once (`ComputerPlayerController.SendPlayerMessage` is a no-op; the host's own line echoes back through the relay → `DisplayPlayerMessage`); a networked host may double-display its own line via the relay's local-dispatch echo — a pre-existing relay quirk, noted, not introduced here.

## Notes

- **2026-06-25** — Implemented + verified. Engine suite **782/0**, full `dotnet build` clean, headless exit 0.
  - `Players/NetworkPlayerController.cs`: uncommented the `RegisterForMessageEvent<NetworkPlayerSubmitChatMessage>` + handler; added the `message.PlayerID != ID` guard. (Also clears the prior CS0067 "OnMessageSentByPlayer never used" warning — it's now raised.)
  - `Tests/InGameChatTests.cs`: 2 tests (relays own player's chat with correct attribution; ignores another player's message so there's no misattributed re-broadcast).
  - No app change. End-to-end two-client chat over a socket would be covered by the #065 loopback fixture.

## Outcome

In-game chat from network players now relays to all clients, attributed to the sender and de-duplicated across controllers (the missing handler registration + a sender filter the dead code lacked). A minimal **in-game chat GUI** was then added (follow-up): received chat shows in the side `GameLog`, sent from a bottom-of-window input bar (`GuiPlayerMessageUI` + `RaylibRenderer.DrawChatInput`), replacing the CLI stub the GUI was launching with. 5 app tests (`GuiPlayerMessageUITests` + the relay/filter coverage in the engine `InGameChatTests`). **Awaiting GUI hand-verification** in the running window (can't be automated — no display in CI; logic + wiring are unit-tested, build clean, headless exit 0). Deferred: per-channel (team) chat UI, history/scrollback beyond the shared log, and the networked host self-echo de-dup.
