# 077 — In-game chat: finish

**Status**: done (engine; awaiting GUI hand-verification of a real two-player chat)
**Related**: audit §13.20; branch `082-network-robustness` (fourth of #082/#075/#076/#077)

## Goal

Network clients already send `NetworkPlayerSubmitChatMessage` to the host after launch, and the relay (`LogAndChatMessageRelayer`) + per-client receive path (`LogChatMessageEndpoint`) are fully wired — but the host's handler registration in `NetworkPlayerController` was commented out, so in-game chat from network players vanished (lobby chat already worked). Finish it: re-register the handler and relay through to all clients.

## Decisions

- **2026-06-25** — **Finish, not remove** (user-chosen). The whole loop already exists and is instantiated during the game: `FDGServer` builds `LogAndChatMessageRelayer` (subscribes to every controller's `OnMessageSentByPlayer` and broadcasts `PlayerChatNetworkMessage`), and both `FDGGame_AsLocal`/`FDGGame_AsClient` build a `LogChatMessageEndpoint` (sends submissions, displays broadcasts). Only the host-side `NetworkPlayerController` handler was missing — a ~2-line re-enable.
- **2026-06-25** — **Added a sender filter the commented-out code lacked.** Every network player's `NetworkPlayerController` is registered for `NetworkPlayerSubmitChatMessage`, and the old handler raised `OnMessageSentByPlayer` with the controller's *own* `ID`. With 2+ network players that would re-raise (and the relayer re-broadcast) the same chat once per controller, each misattributed. The handler now early-returns unless `message.PlayerID == ID`, so exactly the sender's controller relays it, attributed correctly. (Single-network-player play happened to work before only by coincidence of there being one controller.)

## Notes

- **2026-06-25** — Implemented + verified. Engine suite **782/0**, full `dotnet build` clean, headless exit 0.
  - `Players/NetworkPlayerController.cs`: uncommented the `RegisterForMessageEvent<NetworkPlayerSubmitChatMessage>` + handler; added the `message.PlayerID != ID` guard. (Also clears the prior CS0067 "OnMessageSentByPlayer never used" warning — it's now raised.)
  - `Tests/InGameChatTests.cs`: 2 tests (relays own player's chat with correct attribution; ignores another player's message so there's no misattributed re-broadcast).
  - No app change. End-to-end two-client chat over a socket would be covered by the #065 loopback fixture.

## Outcome

In-game chat from network players now relays to all clients, attributed to the sender and de-duplicated across controllers. The fix is the missing handler registration plus a sender filter the dead code lacked. **Awaiting GUI hand-verification** of a real two-player in-game chat exchange.
