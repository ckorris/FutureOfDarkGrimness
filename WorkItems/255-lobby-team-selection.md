# 255 — Lobby team selection

**Status**: Implemented + tested 2026-07-21; awaiting GUI hand-verify (checklist below).
**Related**: #221 (color picker — the sync triad this clones), #188 (multi-remote-client roster/team edge cases), #052 (resume lobby).

## Goal
Owner request (2026-07-21): add team selection to the lobby. As many teams as players; each newly
added player defaults to the first empty team (at worst the one their own arrival created); launch
is blocked while ALL players share one team. Teams were already a first-class engine concept
(`ETeamOption` rides `LobbyPlayerInfoSummary`, `Launch()` feeds `PlayerSlot.TeamNumber`,
`GameBootstrap.AddTeams` + `TeamPlayerAlternationCursor` make teams drive turn order) but every
player was silently placed on their own team (`count + 1`) with no way to change it.

## Decisions
- Cloned the #221 color triad: `ILobbyViewModel.SetPlayerTeam` + `PlayerTeamUpdateMessage`
  (client -> host, own row only) + host apply/rebroadcast. No uniqueness check — sharing is the point.
- Teams rendered numerically (`Team {n}`, `(ETeamOption)n`) so >4 players needs no enum extension;
  `ETeamOption` untouched.
- Launch gate is a HARD block in `ValidateLaunchSettings()` (not the overridable #153 army-problems
  confirm), only when playerCount >= 2 so solo test launches keep working. Fresh launches only —
  `TryResumeGame` doesn't run it.
- On player disconnect, stale out-of-range teams are LEFT AS-IS (owner sign-off 2026-07-21); new
  picks are clamped to 1..playerCount host-side.
- Team editing disabled in resume mode (UI-disabled + host-side guard) — saved games keep saved teams.
- Known limitation, out of scope: `VictoryCalculationStage` scores per-player, not per-team —
  teammates don't share victory. Candidate follow-up if real team play is wanted.

## Notes
- 2026-07-21: Implemented. Engine: `FirstEmptyTeam()` replaces the three `count + 1` default sites
  (client greeting / AddLocalPlayer / AddAiPlayer); `SetPlayerTeam` on both view models;
  `PlayerTeamUpdateMessage` registered in both host ctors; team gate in `ValidateLaunchSettings`;
  `NetworkProtocol.Version` 3 -> 4. App: `LobbyScreen` player table 7 -> 8 columns, new
  `DrawTeamCell` (combo Team 1..N, gated by `CheckCanModifyPlayerIDInfo` + not resume).
  Tests: `LobbyTeamSyncTests` (8) over the loopback lobby — defaults, first-empty gap fill,
  host/client pick sync, own-row-only, out-of-range ignored, same-team launch block, distinct-team
  gate pass. Engine 1801/0, full build clean, headless smoke exit 0.

## GUI hand-verify checklist
- [ ] Host + Add Local Player + a bot: teams default 1, 2, 3; Team column shows dropdowns.
- [ ] Move players between teams via the dropdown; only Team 1..Team N offered.
- [ ] Put everyone on one team -> LAUNCH shows the inline red "same team" message and does not start.
- [ ] Split teams -> launches; two players on one team alternate activations as a team in-game.
- [ ] Two-machine (or two-instance) check: client can change only its own row; the pick syncs to the
      host and back; host can change Local/AI rows but not the client's.
- [ ] Resume a save: Team dropdowns disabled.
