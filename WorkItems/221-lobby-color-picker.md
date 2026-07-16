# 221 — Color picker in lobby

**Status**: in-progress (implemented + tested; awaiting GUI hand-verification)
**Related**: `LobbyScreen.cs`, `GameGuiWiring.cs`, `PlayerColorOptions.cs`

## Goal
Let each player pick their own color in the lobby (currently no player/team color selection exists there). Scope includes deciding where that color then shows up in-game (model highlight rings, UI accents, etc. — check what color-coding, if any, already exists elsewhere before building) and how collisions between two players picking the same color are handled.

## Decisions
- 2026-07-15: **Classic-RTS dropdown, 8 colors** (user direction): Orange, Purple, Green, Yellow, Red, Blue, Teal, Pink. Palette order is load-bearing twice - it's the dropdown order AND the default-assignment order, so the first four preserve the pre-picker slot defaults exactly (P1 orange, P2 purple, P3 green, P4 yellow); an untouched lobby looks identical to before.
- **Where color lands in-game**: no new plumbing needed - `GameGuiWiring.Launch` already assigns every player color consumed by the renderer (models, objective pips, scoreboard, chat names). The picker just feeds that assignment instead of the old fixed by-index palette.
- **Collisions**: an option explicitly picked by another row is disabled "(taken)" in the dropdown. Defaults don't reserve - explicitly picking someone's *default* color steals it and their default shifts to the next free one (deterministic, in `PlayerColorOptions.ResolveIndices`). With 8 colors / max 4 players there is always a free color.
- **DEFERRED - network sync (recorded, not silently cut)**: picks are app-side and local-machine only. Syncing them (each player picks their own, everyone sees it) needs an engine lobby-protocol extension - `LobbyPlayerInfoSummary` field + a set-color command + host rebroadcast - i.e. submodule changes gated on sign-off. Today each machine's lobby colors its own view; single-machine play (hot-seat / vs bots) gets the full experience. Revisit alongside #188 (multi-remote-client).
- Dropdown edit permission mirrors Load Army (`CheckCanModifyPlayerIDInfo`): host edits every row (incl. bots), a networked client its own. Works in resume mode too (colors are cosmetic, assigned at launch, not part of the save).

## Notes
- 2026-07-15: **Implemented.** New `PlayerColorOptions` (palette + pure pick-resolution, unit-tested); `GameGuiWiring.Launch` takes an optional per-player pick lookup (the `--scenario` call site is untouched and keeps pure defaults); `LobbyScreen` grows a Color column (swatch + dropdown, taken-options disabled) and passes picks at launch; picks reset with each new lobby session. Tests: `PlayerColorOptionsTests` (7 cases - palette shape/order, defaults, pick-wins, steal-bumps-default, no default collisions, out-of-range pick, >8-player wrap). Verify: full build clean, engine 1642/0, app suite 343/0, headless smoke exit 0. Remaining: GUI hand-verification.
- 2026-07-15: Filed from user playtest feedback. No existing player-color mechanism found in `LobbyScreen.cs`.

## Outcome
