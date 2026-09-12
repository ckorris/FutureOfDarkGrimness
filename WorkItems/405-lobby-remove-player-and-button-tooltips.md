# 405 — Lobby: remove a player/bot, and tooltips on the add buttons

**Status**: in-progress (implemented + tested; awaiting GUI hand-verify)
**Related**: #191 A6/B5 (the three bot rungs), #372 (Actions column), #402 (bots arrive with no army)

## Goal
The lobby could ADD local players and bots but never drop one: a misclicked bot sat there until the host
tore the whole lobby down. Add a per-row Remove, and give the four add buttons tooltips saying what each
one actually gets you — "Tactician" vs "Strategist" said nothing about which is harder, or that one of
them thinks for a while before each move. Done = host can remove any local player or bot it added,
tooltips carry Chris's wording, and neither is offered where it would be wrong (own row, connected
client, resumed or launched lobby, client's screen).

## Notes

- 2026-09-12: Tweaks off Chris's first look. DerpBot tooltip "dumb luck" -> "sheer luck". Name /
  Army / Faction now shrink text to fit instead of clipping (`FitFontScale`, floor 0.65 of the row's
  scale) - "Human Defense Force" was cut off at his resolution. Pts 0.06 -> 0.051 and Team 0.10 ->
  0.085 (-15% each); stretch-prop hands the freed width to the text columns, which helps the same
  clipping a second way. 8 new tests. App 2990/2990, engine 3361/3361, smoke exit 0.
- 2026-09-12: Implemented. Engine: `CheckCanRemovePlayer` + `RemovePlayer` on `ILobbyViewModel`, host
  implementation, client refuses. App: Remove in the Actions column, four tooltips, Actions widened
  0.22 -> 0.26. 6 new engine tests (`LobbyPlayerRemovalTests`), ASCII guard extended app-side.
  Engine 3361/3361, app 2982/2982, smoke exit 0. Both guards mutation-verified red.
- 2026-09-12: Filed clean off `origin/master` at `8b6f598` — detail files max 404, archive max 401,
  index high-water 398, no `40x` branch on either remote. See `Reconciliations.md`.

## Decisions

- **`_thisPlayerID` had to become a field on the host.** This was the load-bearing discovery. The host's
  own slot is `EPlayerType.Local` with an empty `ConnectionID` — byte for byte what an added local player
  looks like — and the host's `PlayerID` was a *constructor local*, discarded after use. So nothing could
  tell the two apart, and the obvious "any Local row is removable" rule would have let the host delete
  itself out of its own lobby. Both constructors now record it (the resume one records the slot the host
  adopts). `LobbyPlayerRemovalTests.Host_CannotRemoveItself` asserts the indistinguishability as a
  precondition, so the reason the field exists cannot be silently refactored away.

- **Policy lives in the engine, not the front end.** `CheckCanRemovePlayer` mirrors the existing
  `CheckCanModifyPlayerIDInfo` so the GUI asks rather than re-deriving who may be removed. `RemovePlayer`
  re-checks it rather than trusting the caller: the button state is a frame stale, and that is also the
  seam a future networked caller would arrive through.

- **No new wire message.** `UpdateInfoSummariesFromFullList` already broadcasts the *whole* roster as a
  `LobbyPlayerListUpdate`, so dropping the entry and re-broadcasting is what every client sees. Colour and
  team free themselves: colours resolve per frame from surviving picks (#221) and `FirstEmptyTeam` reads
  the live list. Pinned by `RemovingABot_ReachesTheConnectedClient`.

- **Remove is hidden where disallowed, not greyed out** (Chris, 2026-09-12) — unlike Random Army, whose
  greyed state is the one that most needs explaining. A disabled Remove on the host's own row would read
  as a bug rather than as meaning.

- **No confirmation dialog** (Chris, 2026-09-12). Re-adding is one click and the lobby rolls a fresh army
  automatically, so a misclick is cheap.

- **Squish, don't truncate or wrap** (Chris, 2026-09-12). Roster text is player-chosen, so no column
  width always fits. Truncating hides the end of a name and wrapping makes one long name grow every row,
  so the text shrinks to fit and stops at a floor, below which clipping is the lesser evil. The measured
  width must be the raw `CalcTextSize` - it ignores `SetWindowFontScale`, which is what makes the
  quotient a scale rather than a ratio of like quantities (the same gotcha the Back button measures
  around).

- **Removing mid-loop is safe.** `RemovePlayer` publishes a *new* list, so the `players` snapshot the
  frame is iterating is unaffected; the row vanishes next frame.

## Deferred (explicitly, not dropped)

- **Kicking a connected client.** `CheckCanRemovePlayer` returns false for `EPlayerType.Network`. A client
  leaves by disconnecting; taking its slot away needs its own wire message and a consent story. Chris
  asked for "local players and bots", so this is out of scope by request, not by oversight.
- **Removing slots in a resumed lobby.** Saved slots are the shape of the saved game; re-crew already
  covers changing who plays one.

## Outcome
_(written when the item closes)_
