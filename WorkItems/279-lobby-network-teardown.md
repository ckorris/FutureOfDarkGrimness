# 279 — Lobby network teardown (zombie host lobby / ghost roster slots)

**Status**: implemented + engine-tested; awaiting GUI hand-verify (owner testing in a few hours)
**Related**: #271 (server browser — the flow that surfaced it), #189 (broadcast gating), #266 (pre-auth caps), #075 (join handshake)

## Symptoms (playtest 2026-07-25, via the server browser)

Host opened a room, client joined via the browser: client saw both players, host saw nobody join.
Host chat reached no one (host saw own echo only); client chat appeared nowhere. Client re-joined
after disconnecting and saw THREE occupied slots.

## Root cause

Leaving a lobby never tore the networking down, and a failed re-bind was silent:

1. `LobbyScreen.OnBack` only stopped the #271 listing heartbeat - `FDGHost.Stop()` was never called
   anywhere in the app, so an abandoned host lobby kept listening on the port forever.
2. Re-hosting created a new `FDGHost` whose `_listener.Start()` threw "address in use" into a
   discarded task (`_ = host.StartAsync()`) - completely silent. The new lobby UI was bound to a
   dead server.
3. `LobbyScreen.SetViewModel` disposed the old VM, but `LobbyViewModel_Host.Dispose` only
   deregistered the client-chat relay + the connect/disconnect events. The `NewLobbyClientGreeting`
   handler stayed registered, so the OLD lobby serviced joins as a half-alive zombie: roster updates
   broadcast from a lobby no UI was bound to (client sees players, host UI doesn't), client chat
   dropped (relay was the one thing deregistered), disconnects never removed (ghost slots -> the
   three-slot roster).
4. Client side: backing out never called `FDGClient.Disconnect()` (only failed-join paths did), so
   a departed client stayed on the roster as a ghost even in a healthy lobby.

## Fix

- **Engine** (`LobbyViewModel_Host.Dispose` / `LobbyViewModel_Client.Dispose`): idempotent full
  teardown - deregister every handler, dispose the message bus (detaches from the transport's
  events; `MessageBusHost_Networked` now also detaches its disconnect forwarder), then
  `_host.Stop()` / `_networkClient.Disconnect()`. Client unhooks `OnDisconnected` before
  disconnecting so its own teardown never surfaces as "connection to the host was lost" (QF8).
- **App** (`Program.cs`): new `activeLobby` + `TeardownLobby()` beside the #271 listing state;
  called on lobby Back, game exit (`OnGameExited`), and before adopting a new lobby
  (HostModal.OnCreated / ClientModal.OnConnected / LoadGameFlow).
- **App** (`HostModal.CreateServer`, `LoadGameFlow`): a bind failure faults `StartAsync`'s task
  before its first await, so both now check `IsFaulted` instead of discarding the task - HostModal
  shows "Could not listen on port N - is it already in use?" instead of navigating to a dead lobby.

## Verification

- Engine suite 2129/2129 green (4 new pins in `Tests/LobbyTeardownTests.cs`: disposed host lobby
  ignores late greetings; dispose stops the listener exactly once; client dispose closes the
  connection exactly once without raising game-end; disposed client ignores late broadcasts).
- Full `dotnet build` green; headless smoke exit 0.
- **Remaining - GUI hand-verify** (no display in this session): host -> back -> host again -> client
  join must show the joiner on the host, chat both ways, and rejoin after client-back must show 2
  slots, not 3. Also: hosting twice concurrently (second app instance) must show the port-in-use
  error in HostModal.

## Notes

- 2026-07-25 — investigated, root-caused, implemented, tested (this file). Engine changes
  authorized by owner. Server browser itself was innocent - it just made the host->back->host-again
  path likely.
