# 373 — Bolt Action turn style (unimplemented; lobby option withdrawn)

**Status**: todo
**Related**: `ETurnStyle` in `FutureOfDarkGrimness/GameModel/GameSettings.cs`

## Goal
`ETurnStyle.BoltAction` — "whose turn it is is random, weighted by how many activations each player
has left" — is declared, synced, saved and remembered, but **nothing in the state machine ever reads
`GameSettings.TurnStyle`**. Done = the activation order actually branches on it
(`DeterminePlayerTurnStage` / `ChooseUnitToActivateStage`), and the lobby control comes back.

## Notes

- 2026-08-11: **Lobby control removed** (`LobbyScreen.DrawSettings`). Filed at the same time, so the
  deferral is recorded rather than silently dropped. Nothing else was touched — the enum, the
  `GameSettings.TurnStyle` field, the host/client observable + setter, the `LobbyGameSettingsUpdate`
  broadcast, the save payload and `HostGameSettings` round-trip all stay exactly as they were, so a
  config or save written while the dropdown existed still loads. The value is simply inert and no
  longer editable.
- 2026-08-11: Audited every reference before removing. Reads of `TurnStyle` are: the two lobby view
  models (plumbing), `UserConfig`/`HostGameSettings` (persistence), `GameProgressData` (save), and
  three test files that use `BoltAction` purely as an arbitrary non-default value for
  serialization/override assertions. No rules code. Those tests keep passing untouched.

## Decisions

- **Remove the row, not the enum.** A one-entry dropdown is worse than none, but stripping the setting
  out of `GameSettings` would churn the wire format, the save payload and three engine/app test files
  for a feature that is wanted later. The plumbing is inert and costs nothing to keep.

## Outcome
_Open._
