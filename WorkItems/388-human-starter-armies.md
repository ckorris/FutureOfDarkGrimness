# 388 — Human slots get a starter army too (extends #372 past the bots)

**Status**: Implemented + tested 2026-08-30; awaiting GUI hand-verify (host row, added local player,
and a connected client's own row)
**Related**: #372 (bot starter armies - `ArmyCatalog` + `BotArmyPicker`, the machinery this reuses),
#153 (launch gate), #310 (per-user config)

## Goal
A human slot should arrive with a real army from `armies/` the same way a bot does, instead of an empty
Army cell. Done = opening a lobby fills the host's own row, an added local player's row fills as it is
added, and a client's own row fills on the client's machine - while a remote player's row is never
written by anyone else.

## Notes

- 2026-08-30: **Implemented app-side, entirely inside `LobbyScreen`.** `AutoArmyNewBots` ->
  `AutoArmyNewSlots`, `_autoArmiedBots` -> `_autoArmiedSlots`, and the per-row test moved out into a
  pure `LobbyScreen.NeedsStarterArmy(playerType, armyAssigned, canModify, alreadyServed)` so the rule is
  unit-testable away from ImGui. The pass no longer returns early for a non-host: permission is decided
  per row by `CheckCanModifyPlayerIDInfo`, the same gate Load Army and Random Army already use, so the
  host serves its own row + its local humans + the bots, and a client serves only its own row. The
  `IsResumeMode` skip and the leaver-prune are unchanged.
- 2026-08-30: Tests - `FdgRaylib.Tests/LobbyStarterArmyTests.cs` (5, mirroring `MixedSystemWarningTests`
  in shape: a pure static on `LobbyScreen`, exercised directly). App suite 1554 green, engine suite 3070
  green (1 skipped, pre-existing), headless smoke exit 0.
- 2026-08-30: Also in this session, unrelated: the README's Discord bug-report `[LINK]` placeholder now
  points at the real channel.

## Decisions

- **`canModify` is the whole permission rule**, exactly as #372 settled it for the Random Army button.
  A second "who may be auto-armied" rule would be a second thing to keep in sync with the lobby's
  ownership model, and it would get the client case wrong: a client's own row is the one row it may
  write, and its machine is the only one that can read its armies folder.
- **A human keeps an army it already has; a bot cannot be judged that way.** `AddAiPlayer` stamps every
  bot with the 100-pt "Test Army" stub, so a bot row is always `IsAssigned` and only the served-set can
  spot a fresh one. A human row starts genuinely unassigned, so `IsAssigned` is a real signal there and
  is used as a second guard - it protects a saved-slot army and an army loaded in the window before the
  folder scan lands.
- **The served-set still matters for humans**, even with that guard: a client's `UpdateArmyListFile`
  goes to the host and comes back on the next roster broadcast, so its own row reads unassigned for a
  round trip after it rolls. Without the set it would roll again every frame until the reply arrived.
- **Clients seed themselves** rather than the host seeding them (owner's call, 2026-08-30). The host
  cannot write a Network row, and the armies folder that would be rolled from is the client's own.

## Outcome

_(open)_
