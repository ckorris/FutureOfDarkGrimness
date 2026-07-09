# 190 — Networked clients never receive mid-game token updates

**Status**: todo
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
- (none yet)

## Outcome
(pending)
