# Networking quick fixes for tonight's build (2026-07-08)

Context: first internet-facing build going to external playtesters (OnePageRules creators). Netcode audit
found the core solid (framing, version handshake, host-side disconnect handling) but with internet-specific
gaps. This file lists the one-pass quick fixes (QF1-QF10) and the deferred work items.

**STATUS: QF1-QF10 all implemented 2026-07-08.** Engine tests green (1319, incl. new LobbyJoinGateTests),
full build clean, headless smoke exits 0. Protocol bumped v1 -> v2 (all players need this build). Remaining
work filed as items #186-189 (see WorkItemsList.md "Networking & infrastructure"). Live two-instance GUI
test (wrong/right password, mid-game host/client kill, host-IP display, DNS host entry) still to be done by
hand before sending.

Nearly all QFs touch the `FutureOfDarkGrimness` submodule — **engine changes authorized for this task**.
Follow CLAUDE.md: submodule-first commit cadence, verify before committing (engine tests + full build +
headless smoke), ASCII-only user-facing strings.

## Quick fixes

### QF1 - Enforce the lobby password (submodule + app)
- Add `string? Password` to `NewLobbyClientGreeting` (`Network/Messages/LobbyMessages/NewLobbyClientGreeting.cs`).
- `LobbyViewModel_Client` ctor: take a password param, include it in the greeting.
- `FdgRaylib/Rendering/ClientModal.cs`: add a password input (`ImGuiInputTextFlags.Password`), pass through.
- `LobbyViewModel_Host` (BOTH ctors): store the `password` ctor param it currently ignores
  (`LobbyViewModel_Host.cs:99`). In `OnReceiveNewClientGreeting`, after `TryValidateJoin`: if host password
  non-empty and greeting.Password mismatched -> `LobbyJoinRejectedMessage("Incorrect password.")` to that
  connection, then disconnect it (QF2). Plain-text compare is acceptable at this trust level; comment it.
- Bump `NetworkProtocol.Version` to 2 (greeting shape changed).

### QF2 - Host can drop a connection (submodule; QF1/QF6 depend on it)
- Add `void DisconnectClient(ConnectionID id)` to `INetworkHost` + `FDGHost`: look up in
  `_connectedClients`, `Client.Close()`. The read loop's `finally` already does removal + OnClientDisconnected.
- Use after awaiting the rejection send in reject paths.
- Greeting timeout: on `OnNewClientConnected` in `LobbyViewModel_Host`, fire-and-forget
  `Task.Delay(10s)` then `DisconnectClient` if the connection still has no `_playerInfosFull` roster entry.
  (Internet-exposed 6389 gets port-scanned; scanners must not linger receiving broadcasts.)

### QF3 - TCP keepalive + NoDelay (submodule)
- Shared helper (e.g. in `CommandProtocol` or a small util): `client.NoDelay = true;` and socket options
  `KeepAlive=true`, `TcpKeepAliveTime=30`, `TcpKeepAliveInterval=5` (.NET 8 supports these on Win/Linux/macOS).
- Apply to accepted clients in `FDGHost.StartAsync` and to the socket in `FDGClient.ConnectAsync`.
- Why: over WAN, dead peers (crash/sleep/NAT expiry) otherwise look alive forever; host waits on a reply
  that never comes and no disconnect event fires. Also keeps NAT mappings warm during long think-pauses.

### QF4 - Single-buffer frame write (submodule, trivial)
- `CommandProtocol.WriteCommandAsync`: build `[magic][length][payload]` in one rented buffer, single
  `WriteAsync`. Read side unchanged. Pairs with NoDelay to remove Nagle/delayed-ACK stalls (~100-200ms/msg).

### QF5 - Target the PlayerID assignment (submodule, two lines)
- `LobbyViewModel_Host.cs:251` (new-game greeting) and `:292` (resume greeting):
  `SendCommandToAllAsync(new LobbyPlayerIDAssignment(...))` -> `SendCommandToSingleAsync(..., connectionID)`.
- Bug: assignment is broadcast and `LobbyViewModel_Client.OnPlayerIDAssignmentReceived`
  (`LobbyViewModel_Client.cs:186`) sets `_thisPlayerID` unconditionally, so with 2+ remote clients every
  earlier client adopts the last joiner's ID. Local dispatch isn't needed for this message.

### QF6 - Reject joins after launch (submodule)
- `_isLaunched` flag in `LobbyViewModel_Host`, set at top of `Launch()` and `LaunchResume()`.
- In `OnReceiveNewClientGreeting`: launched -> `LobbyJoinRejectedMessage("Game already in progress.")` + disconnect.

### QF7 - Port-scan disconnect crash (submodule, one line)
- `LobbyViewModel_Host.OnClientDisconnected` (`LobbyViewModel_Host.cs:300`): `.First(...)` throws
  `InvalidOperationException` for connections that never greeted. Use `FirstOrDefault` pattern
  (KVP.Value null check) and return if no roster entry matched.

### QF8 - Client learns the host is gone (submodule + app)
- `event Action? OnDisconnected` on `INetworkClient`; `FDGClient.Disconnect()` fires it exactly once on the
  true->false transition of `_isConnected`.
- `LobbyViewModel_Client` subscribes; raises its existing `OnGameEnded("Connection to the host was lost.")`
  (reuses the #040 return-to-menu flow). Guard with a bool so a real `GameEndedMessage` that already
  arrived doesn't double-fire. Verify the pre-launch (in-lobby) host-loss case navigates sanely too.

### QF9 - Show the host their IP (app only)
- `LobbyScreen` when `HasHostPrivileges`: display `LAN: <addrs> | Public: <addr> | Port: 6389` + a
  copy-to-clipboard button (`ImGui.SetClipboardText`). ASCII only.
- LAN: `NetworkInterface.GetAllNetworkInterfaces()` -> OperationalStatus.Up, non-loopback, IPv4 unicast.
- Public: async `HttpClient` GET `https://api.ipify.org`, try/catch -> "unavailable". Fetch once, cache.

### QF10 - Hostname entry (app only)
- `ClientModal.cs:60`: drop `ImGuiInputTextFlags.CharsDecimal` (blocks letters AND IPv6 colons), relabel
  "Host Address". In `AttemptConnect`: `IPAddress.TryParse` first, else `Dns.GetHostAddressesAsync` and
  **filter to `AddressFamily.InterNetwork`** - `FDGHost` listens on `IPAddress.Any` (IPv4 only).
  Enables dynamic-DNS names (DuckDNS etc.).

## Verification for tonight
1. `dotnet test FutureOfDarkGrimness/FutureOfDarkGrimness.csproj` green; full `dotnet build`.
2. Headless smoke: `printf "2\n2\n" | dotnet run --project FdgRaylib/FdgRaylib.csproj -- --headless` exits 0.
3. Two GUI instances on 127.0.0.1: wrong password -> rejected with readable reason; right password -> join;
   launch; kill client mid-game -> host ends cleanly (PlayerDisconnectedException path); kill host mid-game
   -> client shows connection-lost and returns to menu.
4. If a second physical machine is available: repeat over LAN (also shakes out firewall prompts).

## Work items to file for the remainder
(Next free numbers: check WorkItemsList.md + WorkItems/Archive.md + WorkItems/Reconciliations.md; 186+ as of 2026-07-08 pull.)

1. **Harden network deserialization** - the wire path uses Newtonsoft `TypeNameHandling.Auto` and
   `StableTypeSerializationBinder` falls back to `DefaultSerializationBinder`
   (`SaveLoad/StableTypeSerializationBinder.cs:43`), which resolves arbitrary assembly-qualified `$type`
   names from the network - a deserialization attack surface on an exposed port. Fix: allowlist binder
   (registry IDs + own-assembly types, incl. generic collection closures) for the NETWORK serializer
   settings only; saves keep the permissive fallback. Fiddly - verify full-state sync + army-list transfer.
   Mitigated meanwhile by QF1 password + recommending Tailscale.
2. **Disconnect recovery** - auto-save a recovery file when the game ends via `PlayerDisconnectedException`;
   live-test the networked resume-rejoin flow (#052 follow-up, currently marked NOT live-tested).
3. **Multi-remote-client support** - after QF5, live-test 3+ players with 2 remote clients (roster order,
   team numbers, outstanding-task UI). Currently only 1v1 (host + one remote) is trustworthy.
4. **Broadcast gating + configurable port** - only roster members should receive broadcasts (pre-greeting
   connections currently receive everything, incl. game data); make `CommandProtocol.TEMP_PORT` configurable.

## Note for the playtesters (include with the build)
Recommend Tailscale for cross-country play: both install it, host shares their Tailscale IP, no router
config. Works behind CGNAT (port forwarding may be impossible for some ISPs), and the encrypted tunnel
covers the deserialization-hardening gap until WI-1 lands.
