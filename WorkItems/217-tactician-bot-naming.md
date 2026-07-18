# 217 — Tactician Bot name enumerated by player slot, not bot count

**Status**: todo
**Related**: #191 (Tactician agent umbrella), #216, `LobbyViewModel_Host.cs:859-866`

## Goal
`AddAiPlayer` names each bot `"{botName} {playerNumber}"` where `playerNumber = _playerInfos.Value.Count + 1` — the total player count at add-time, not how many bots of *that profile* already exist. So e.g. the first Tactician Bot added after two human players is "Tactician Bot 3" instead of "Tactician Bot 1", and mixing Tactician/DerpBot adds produces gappy, non-sequential numbering per type. Done = each bot's number reflects its rank among bots of the *same* `EAiProfile` already in the lobby (first Tactician Bot -> "Tactician Bot 1", second -> "Tactician Bot 2", independent of DerpBot count or human players).

## Notes
- 2026-07-18: Implemented. `AddAiPlayer` now numbers the bot by `count(existing AI infos with the same
  EAiProfile) + 1` instead of the total player count; team number still uses the player count as before.
  Test: `LobbyBotNamingTests.BotNames_NumberPerProfile_NotByPlayerCount` (host + Tactician/DerpBot/Tactician
  -> "Tactician Bot 1", "DerpBot 1", "Tactician Bot 2"), using a host-only no-op network double.
  Suite green (1687). Awaiting GUI hand-verify (add bots in a lobby, check the row names).
- 2026-07-15: Filed from user playtest feedback. Not previously tracked.

## Decisions

## Outcome
