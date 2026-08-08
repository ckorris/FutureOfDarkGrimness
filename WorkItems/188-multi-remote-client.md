# 188 — Multi-remote-client support (3+ players, 2+ remote clients)

**Status**: in progress (lobby-layer coverage landed 2026-08-08; live multi-machine verify still open)
**Related**: QF5 (targeted PlayerID assignment), QF6, NetworkingHandoff-2026-07-08.md

## Goal
Only host + one remote client (1v1) is trustworthy today. QF5 fixed the broadcast-PlayerID-assignment bug
(every client adopting the newest joiner's ID) by targeting the assignment, but the multi-client path has
never been live-tested. Done = a 3+ player game with 2+ remote clients works end-to-end: each client keeps
its own identity, roster order / team numbers are correct, the outstanding-task UI attributes waits to the
right players, and per-player request routing (#088) reaches the right client.

## Notes
- 2026-08-08: **Lobby-layer coverage landed** (engine `59d38e9` fixture, `589fe2c` tests).
  - **The blocker was the test double, not the code.** The in-process loopback the lobby tests ran on
    was single-client and copy-pasted into six files; its `SendCommandToSingleClientAsync` ignored the
    `ConnectionID` argument and delivered to its one client. Every targeted-send path (QF5 assignment,
    #088 routing, #105 log de-dup) therefore passed no matter where it was addressed. Replaced with a
    shared `Tests/LoopbackNetwork.cs` (N clients, own ConnectionID each, targeting honored, per-client
    frame counts, `MarkClientAuthenticated`/`DisconnectClient` recording). Migration was behavior-neutral:
    the six files kept their semantics, including the client-side `Disconnect` NOT notifying the host, on
    which the #279 teardown assertions depend.
  - **New `Tests/MultiClientLobbyTests.cs` (12).** Identity: 3 clients each keep a distinct PlayerID,
    each owns the roster slot bearing its name, each can edit its own slot and refuses the others.
    Transport: per-connection `MarkClientAuthenticated` (#266), distinct roster ConnectionIDs, and
    fixture self-guards (targeted send reaches exactly one client; broadcast reaches all). Roster/teams:
    join order matches on host and every client, four players default to teams 1-4, and one client's
    team or color pick reaches the host and all other clients.
  - **Mutation-checked.** Reintroducing the QF5 broadcast turns 4 of the identity tests red (including a
    client unable to edit the slot it is playing), so they are not vacuous.
  - **Team-number finding, needs a ruling.** The 2026-07-08 note guessed right. `FirstEmptyTeam` returns
    `(ETeamOption)(_playerInfosFull.Count + 1)` with no cap, but `ETeamOption` defines Team1..Team4 and
    nothing in the engine caps the roster at four - so a fifth player defaults to an undefined enum
    value, which `SetPlayerTeam` would then reject as out of range if they tried to re-pick it. Pinned as
    current behavior in `FifthPlayer_DefaultsToATeamOutsideTheDefinedRange`, deliberately NOT fixed:
    the fix is a design choice (cap the lobby at 4, clamp the default, or widen `ETeamOption`).
  - **Still open, unchanged:** everything above is the lobby layer. In-game per-player request routing
    (#088, `RequestMessageSender` + `PlayerSlotManager`), reply attribution, N-client chat/log de-dup
    (#105/#077), and the #076 disconnect lifecycle at 3 players are all now testable on this fixture but
    are NOT yet covered. The outstanding-task UI attribution and real multi-machine play remain
    human-only.
- 2026-07-08: Filed. QF5 is the enabling fix; this is the verification + whatever edge cases it surfaces
  (team-number assignment is currently `_playerInfos.Value.Count + 1`, which may need rework for teams).

## Decisions
- 2026-08-08: Fixture is shared and multi-client by default; the single-client doubles are gone. A client
  calling `Disconnect()` still does not notify the host (the real host learns from its read loop, which the
  double does not model) - use `LoopbackNetworkHost.DropClient` to simulate the host-side notice.
- 2026-08-08: Five-player team default left as-is pending a ruling (see the finding above).

## Outcome
(pending)
