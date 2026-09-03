# 372 — Bot starter armies (auto-pick near the points limit + re-roll)

**Status**: CLOSED 2026-08-12 - hand-verified in the running app by Chris
**Related**: #153 (launch gate / BuiltArmyFile), #191 A6 (the two bot profiles), #241/#219 (UnattributedPoints)

## Goal
A bot added in the lobby should arrive with a real army near the lobby's points limit instead of the
hard-coded 100-pt "Test Army" stub, and the host should be able to cycle it onto a different one. Done =
adding a bot gives it a playable list from `armies/`; a per-row button swaps it for another, skipping
armies other players hold and never repeating one until that bot has seen them all.

## Notes

- 2026-08-12: **Two fixes and a rename, from Chris playing it.**
  - Bug: after a few re-rolls the picker started offering armies OVER the points limit. Ranking put them
    last but the no-repeat rotation walked straight off the end of the legal ones into them. Over-limit
    is now a genuine last resort - a separate pool used only when the folder holds nothing legal at all -
    and exhausting the legal armies restarts that cycle instead. Two existing tests encoded the old
    walk-the-whole-catalog behaviour and were rewritten (the behaviour they pinned is the reported bug).
  - The rotation now resets when the points limit changes: both "which armies are legal" and "which is
    closest" are answers to that number, so every recorded rotation is stale the moment it moves.
  - "New Army" -> "Random Army", and it is on EVERY player row rather than bots only. Permission is
    entirely `CheckCanModifyPlayerIDInfo`, the same gate Load Army uses - the host owns its own and the
    bots' rows but not a connected client's, and a client owns only its own. So nobody can roll another
    player's army and a client cannot roll for a bot, without a second permission rule to keep in sync.
  - The Actions column went 0.17f -> 0.22f (from Army and Faction) now that every row carries two
    buttons; the disabled-state tooltip says whose army it is.
- 2026-08-11: Implemented app-side in three pieces.
  - `FdgRaylib/ArmyCatalog.cs` - streaming index of the armies folder. `ArmyCatalogEntry`
    (path/name/faction/points) + a `Utf8JsonReader` scan that `Skip()`s every top-level property it
    doesn't need. Runs on a background task; `IsLoaded` gates the lobby instead of blocking the draw
    thread. Process-wide (`LobbyScreen.SharedArmyCatalog`), since the folder doesn't change under us.
  - `FdgRaylib/Rendering/BotArmyPicker.cs` - pure ranking + rotation, no IO and no ImGui.
  - `LobbyScreen` - `AutoArmyNewBots()` (called at the top of `DrawPlayerList`) and a "New Army" button
    on AI rows only.
- 2026-08-11: Tests - `ArmyCatalogTests` (8) and `BotArmyPickerTests` (11). App suite 1171 green, engine
  suite 2948 green.
- 2026-08-11: Not yet hand-verified in the GUI. Two things to eyeball: the Actions column (0.17f
  stretch) now carries two small buttons and may be cramped (widened to 0.22f on 2026-08-12); and the
  first frames of a lobby show the bot's 100-pt stub until the background scan lands. Both checked out
  on 2026-08-12.

## Decisions

- **App-side, not engine-side.** The engine is usually the right home, but the armies *folder* is an app
  concept (`ArmyPaths`) and the engine has no business reading the user's disk. The engine's
  `AddAiPlayer` stub army is deliberately left in place as the fallback for when there is no armies
  folder at all (a bare `dotnet run` from an odd cwd, or a stripped install).
- **Rotation keyed on PlayerID, not on "has an army".** `AddAiPlayer` gives every bot the 100-pt stub, so
  there is no unassigned state to detect. `_autoArmiedBots` records which slots have been served; a
  hand-picked Load Army also marks the slot, so the auto-picker can never stomp a deliberate choice on a
  later frame.
- **Identity is `name|faction|points`, not the file path.** "Is another player already using this army?"
  has to work for a REMOTE client too, and their army reaches the host as an `ArmyListSummary` with no
  path in it. Two files agreeing on all three fields are the same list for this purpose.
- **An over-limit army sorts behind every legal one**, however far out. Ranking purely by distance from
  the limit would hand a bot a 2200-pt list in a 2000-pt lobby ahead of a 1000-pt one, and the #153
  launch gate flags exactly that. Over-limit is the last resort, not the second choice.
- **A unit-less army file is not indexed.** It parses fine but is unplayable, and at 0 points it would
  otherwise rank first in a low-points lobby.
- **Points are summed by streaming, and a test pins that to the real thing.** The bundled lists are
  Forge-built and carry a full book snapshot each (~12 MB across the folder), so a full deserialize per
  file is too slow to do on the UI thread and wasteful even off it. `IndexedPointsMatchAFullDeserializeOf
  EveryShippedArmy` asserts the streaming reader agrees with `ArmyListFile.TotalPoints` on every shipped
  army, so the shortcut can't silently drift (`UnattributedPoints` was the easy thing to miss).

## Outcome

Shipped and hand-verified in the running app. A new bot arrives with a real list from `armies/` near the
lobby's points limit instead of the 100-pt stub, and every player row carries a "Random Army" button that
rolls another one - closest to the limit first, skipping armies other players hold, never repeating until
the slot has seen them all, and never offering one over the limit unless the folder has nothing legal.
Permission is `CheckCanModifyPlayerIDInfo` alone, the same gate Load Army uses, so a client can roll only
its own row and never a bot's.

Two follow-up fixes from Chris playing it landed the same week: the rotation used to walk off the end of
the legal armies into the over-limit ones, and it did not reset when the points limit moved. Both are
pinned, and the two tests that had encoded the old walk-the-whole-catalog behaviour were rewritten - that
behaviour was the bug.

App suite 1184 green.

Superproject `5b32462`, `e182bcf`.
