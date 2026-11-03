# 190 — Networked clients never receive mid-game token updates

**Status**: implemented 2026-07-26 (Option A); awaiting GUI/live hand-verify
**Related**: #186-#189 (networking batch), NetworkingHandoff-2026-07-08.md

## Goal
Token state (Fatigued, Shaken, spell tokens, embarked markers, ...) only reaches a remote client via the
join-time full-state snapshot. Mid-game, every token change is an in-place `TokenContainer` mutation
(`AddToken` / `RemoveTokens`) on a `UnitData`/`ModelData` that is never re-`Set` through the store, so
`GameDataStore.OnDataUpdatedAsJson` never fires and `GameDataUpdateSender` never broadcasts it. Nothing
subscribes to `TokenContainer.OnTokenAdded/OnTokenRemoved/OnTokenCountChanged` anywhere (engine or app).
Done = a remote client's token chips track the host's truth during play (add, count change, and removal —
removal matters: a stale Fatigued chip reads as "fatigue never clears").

## Notes
- 2026-07-08: Found while investigating a "fatigue never clears" field report. Engine-side lifecycle is
  verified correct (FatigueTests.StrikeBackFatigue_ClearsAtRoundEnd_AndReappliesSinglyNextRound, engine
  38c5aa5); the reporter was playing host-side, so this gap was not that sighting's cause — but any client
  observer would see frozen token state. Candidate fixes: have the store's containers subscribe to token
  events and re-broadcast the owning object, or a dedicated token-delta message. Watch save/load echo:
  a broadcast triggered from inside deserialization must not loop.

## Decisions
- 2026-07-26: Option A (re-broadcast the owning object on token change) over Option B (dedicated
  token-delta message). Rides the existing, tested update path (OnDataUpdatedAsJson ->
  UpdateSingleDataMessage -> client SetValueWithJson); no new message type, no client changes. Tradeoff:
  a token change re-sends the whole UnitData/ModelData, incl. UnitData's `_ruleDefinitionsJson` blob (a
  few KB) even though rules don't change mid-game. Acceptable at beta scale; token changes aren't
  per-frame. If token bandwidth ever profiles hot, swap in B. Owner signed off.

## Outcome
2026-07-26 (Option A). New host-side `TokenChangeBroadcaster`
(`Network/Synchronization/TokenChangeBroadcaster.cs`) subscribes to every unit's/model's
`TokenContainer` events (OnTokenAdded/Removed/CountChanged) and, on any change, re-Sets the owning store
entry with its own unchanged instance. Because `_tokens` is `[JsonProperty]` on UnitData/ModelData, that
fires the ordinary `OnAnyUpdatedTyped -> OnDataUpdatedAsJson` broadcast and the client's SetValueWithJson
resyncs the tokens (add, count-change, and removal all covered by full-object resend).

Wiring in `FDGServer` both ctors: new-game path constructs it BEFORE CreateArmies (hooks each unit/model
on creation via SubscribeToOnCreated); resume path constructs it after the loaded store is populated (its
enumerate-existing pass over GetAllDataBindings hooks everything, since OnCreated already fired during
load). A HashSet<ITokenContainer> guards against double-hooking. Re-Set is guarded by IsValid so a token
clear during object destruction doesn't throw on a dead reference.

Echo-safe (the ledger's stated risk): host-only (only the host runs FDGServer / a GameDataUpdateSender),
the re-Set fires exactly one broadcast and never touches the container again, and client deserialization
sets the token list directly (no AddToken) so it raises no events there.

Regression test: `NetworkedFullStateSyncTests.TokenChanges_AfterJoin_ReplicateToClient` (loopback
host+client) - unit add + count-change, model add + removal (the "Fatigued never clears" case). Full
suite 2161/2161 green; headless smoke exits 0.

Deferred: none. Bandwidth optimization (Option B) explicitly declined for now, recorded above.
