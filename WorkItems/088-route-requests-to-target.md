# 088 — Route decision requests to the target player only

Audit §5. Renumbered from a local #059 (collided with the branch-backed JSON rule loader).

**Status**: done
**Related**: #084/#085/#086 (same RequestMessageSender), audit §5 + §-table row for `StageTaskNotifyAwaitingMessage`

## Goal

Stop broadcasting decision requests to every client and filtering receiver-side by `PlayerID`. Route
each `StageTaskRequestMessage` to just the target player's connection (a remote player) or to in-process
handlers (a local player). This saves bandwidth and stops baking in GDF's open-information assumption —
hidden-info mechanics or lobby spectators would otherwise leak every player's decision payloads to
everyone. Also simplify `StageTaskNotifyAwaitingMessage` to carry plain player info instead of a
`DataBinding<PlayerSlotInfo>`, removing the deserialization-order coupling that required the slot's store
entry to already exist client-side.

## Decisions

- **2026-06-30** — **Awaiting/resolved notifications stay broadcast.** Only the decision *payload*
  (`StageTaskRequestMessage`) is routed; `StageTaskNotifyAwaitingMessage` / `StageTaskNotifyResolvedMessage`
  remain `SendCommandToAllAsync` because every client legitimately shows "waiting on Player N" in its
  outstanding-task UI. The audit's §5 concern is the payload, not the lightweight presence notification.
- **2026-06-30** — **New explicit `SendCommandToLocalAsync` on `IMessageBusHost`** for the local-player
  case, rather than reusing the client-side `SendCommandToHostAsync` (which happens to do the same
  in-process dispatch on `MessageBusHost_Networked`). Clearer intent and mirrors the existing
  `SendCommandToAllAsync` / `SendCommandToSingleAsync`. The networked host's full broadcast already did
  `DispatchToHandlers` + wire-send; local routing wants only the former.
- **2026-06-30** — **Awaiting message carries a `PlayerSlotInfo` value snapshot, not PlayerID+name.**
  The audit suggested "PlayerID + display name," but snapshotting the whole struct is *less* ripple
  (`OutstandingTaskInfo` and `GuiOutstandingTaskDisplay` are unchanged — the GUI shows Name + TeamNumber)
  and fixes the same fragility: a by-value struct has no backing-store dependency at deserialize time.
  `OutstandingTaskLister` reads `.PlayerInfo` directly instead of `.PlayerInfo.GetValue()`.
- **2026-06-30** — **Local routing keeps the receiver-side PlayerID filter.** On a host with multiple
  local players, `SendCommandToLocalAsync` dispatches to every local receiver and they still filter by
  PlayerID (one registrar, shared). That's unavoidable and fine — same machine, same trust boundary. The
  win is purely that no *client* receives another player's payload over the wire.
- **2026-06-30** — Routing discriminator is `slot.Controller is NetworkPlayerController` via the new
  `PlayerSlotManager.TryGetConnectionByPlayerID` (mirror of `TryGetPlayerIDByConnection`). A null or
  non-network controller → local. Reliable: a remote player always carries its `ConnectionID`.

## Notes

- **2026-06-30** — Implemented. Engine commit `0781b2c` (branch `088-route-requests-to-target`);
  superproject pointer bump + app-side `LocalMessageBus` + ledgers in the following superproject commit.
  - `IMessageBusHost`: new `SendCommandToLocalAsync<T>`; implemented in `MessageBusHost_Networked`
    (`DispatchToHandlers` only) and (app-side) `LocalMessageBus` (`Dispatch`).
  - `PlayerSlotManager.TryGetConnectionByPlayerID(PlayerID, out ConnectionID)` — true + connection for a
    `NetworkPlayerController` slot, false (local) otherwise.
  - `RequestMessageSender.RequestDecision`: snapshot `PlayerSlotInfo` by value for the (still-broadcast)
    awaiting message; route the request via `SendCommandToSingleAsync` (remote) or
    `SendCommandToLocalAsync` (local).
  - `StageTaskNotifyAwaitingMessage`: `DataBinding<PlayerSlotInfo>` → `PlayerSlotInfo` (value);
    `OutstandingTaskLister` reads it directly.
  - Tests: `RequestSystemTests` mock gains `SendCommandToLocalAsync` + routing-tracking fields; two new
    routing tests (`RequestDecision_RemotePlayer_RoutesToThatConnectionOnly`,
    `RequestDecision_LocalPlayer_RoutesInProcessOnly`); awaiting-message call sites in
    `RequestSystemTests`/`StageResolutionTests` pass a value (store/binding scaffolding dropped).
  - Verified: engine suite **947/0**, full `dotnet build` clean, headless smoke exits 0 (full game to
    "Game ended: It's a tie!" — exercises the local-routing path end-to-end).
  - Live two-machine networked confirmation (a remote client receiving its request on its own connection)
    is not exercisable headless; the receiver path itself is unchanged (it already filtered by PlayerID
    and now simply receives only its own messages) and is covered by the existing
    `NetworkRequestMessageSender_SendsAndReceivesResponse` flow + the new sender-routing tests.

## Outcome

Decision requests now route to the target player only — remote players over their own connection, local
players in-process — instead of broadcasting to all clients. The awaiting notification dropped its live
`DataBinding<PlayerSlotInfo>` for a value snapshot, removing the deserialization-order coupling. The
second half of the audit §5 line (route only, don't broadcast) and the §-table `StageTaskNotifyAwaitingMessage`
fragility are both resolved. No follow-ups.
