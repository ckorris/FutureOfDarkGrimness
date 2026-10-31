# 388 — Human slots get a starter army too (extends #372 past the bots)

**Status**: CLOSED 2026-08-30 - hand-verified in the running app by Chris
**Related**: #372 (bot starter armies - `ArmyCatalog` + `BotArmyPicker`, the machinery this reuses),
#153 (launch gate), #310 (per-user config)

## Goal
A human slot should arrive with a real army from `armies/` the same way a bot does, instead of an empty
Army cell. Done = opening a lobby fills the host's own row, an added local player's row fills as it is
added, and a client's own row fills on the client's machine - while a remote player's row is never
written by anyone else.

## Notes

- 2026-08-30: **Chris played it: "it was Alien Hives three times in a row."** Two defects, both fixed.
  - The opening pick of every lobby was a pure function of the folder. `Rank` is closest-to-the-limit
    with a `ThenBy(Path)` tiebreak, and the bundled folder has FIVE armies at exactly 1000 points, so at
    a 1000-pt limit the tiebreak decided outright and `1k - Alien Hives.fdgarmy` sorts first; a fresh
    lobby drops the rotation (`SetViewModel` nulls `_botArmyPicker`), so every lobby opened on it.
    Probed against the real folder: the fixed opener was Alien Hives at 1000, Blessed Sisters at 2000,
    Battle Brothers at 3000. Now `BotArmyPicker.BandPercentUnderLimit` (5%) makes every army within 5%
    of the limit interchangeable and picks among them at RANDOM; below the band the old closest-first
    order still stands, so a 1000-pt list is still never offered in a 2000-pt lobby. Same probe after:
    12 fresh lobbies gave 4 / 6 / 8 distinct armies at 1000 / 2000 / 3000, all inside the band.
  - Rows seeded in the SAME frame could not see each other: `AutoArmyNewSlots` handed every
    `AssignRandomArmy` call one pre-pass roster snapshot, so `inUseByOthers` was stale for the second
    slot onward and two rows could land on the same army. Newly reachable via #388 (before it, only bots
    were seeded, and they are added one at a time). `AssignRandomArmy` now reads `PlayerInfos` itself -
    the host applies an army update to its roster synchronously - and takes only the PlayerID.
  - Tests: `BotArmyPickerTests` grew to 17 (a seeded `Random` is now injectable, and the tests that
    pinned an exact opening army asked for the old deterministic behaviour - they assert band membership
    now, plus a new `FreshLobbiesDoNotAllOpenOnTheSameArmy` pinning the reported fault). App suite 1556
    green, engine 3070 green.

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

- **A 5% band, not "random among everything legal" and not "random among exact ties"** (owner's call,
  2026-08-30). Exact-ties-only would have fixed today's folder by luck - five armies happen to tie at
  1000 - and gone deterministic again the moment someone added a 1998-pt list. Fully random among legal
  armies would hand out a 1000-pt list in a 2000-pt lobby. The band keeps #372's "close to the limit"
  intent as a tolerance rather than a total order.
- **The band is capped at the limit, not just floored.** The last-resort pool (nothing legal in the
  folder at all) is entirely over-limit, and every one of those would otherwise count as "in band" and
  be picked at random - handing out a 5000-pt list in a 2000-pt lobby when a 2200-pt one exists. Capped,
  that pool falls through to closest-first exactly as before.

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

Shipped and verified in the running app the same day it was filed. A lobby now opens with a real army on
every row its machine owns, and consecutive lobbies open on different ones. Not covered, and left for
whenever it comes up: the networked leg was reasoned through rather than played (a connected client
seeding its own row), and nothing remembers a player's LAST army across lobbies - #310's config would be
the place if that is ever wanted over a fresh roll.
