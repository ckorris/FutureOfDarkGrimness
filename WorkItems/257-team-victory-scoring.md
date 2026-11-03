# 257 — Team-based victory scoring

> _Renumbered from #256 on 2026-07-22 (reconciliation 19) — collided with origin/master's #256 (AI repack clamp immobilizes big units). The engine commit message was amended to #257 before push._

**Status**: Implemented + tested 2026-07-22; awaiting GUI hand-verify (a real team game's end banner).
**Related**: #255 (lobby team selection — flagged this as its known limitation), #192 (structured GameResult), #191/#194 (FdgLab consumes GameResult).

## Goal
Owner request (2026-07-22, follow-on from #255): victory should be decided per TEAM, not per player —
teammates pool their controlled objectives. Importantly, the victory text must list all the players on
the winning team, never just the team number.

## Decisions
- `VictoryCalculationStage` still tallies per player (logs + per-player `Scores` unchanged), then sums
  per team via each slot's `TeamNumber`. Unique top team wins; tied teams or zero score = tie, same
  paths as before. An objective owner with no player slot keeps a private bucket (preserves the old
  "A player wins!" edge; pseudo keys count down from int.MinValue so they can't collide with real teams).
- Victory text names every player on the winning team in slot order, including zero-score teammates:
  "Alpha wins!" / "Alpha and Bravo win!" / "Alpha, Bravo and Delta win!". Never a bare team number.
- `GameResult` gains `WinnerPlayers` (full winning roster, slot order; empty on Tie/Fault). `Winner`
  stays the roster's first player so FdgLab's winner-slot mapping and all 1v1 consumers are unchanged;
  `WinnerName` is now the joined names. For singleton teams every field and message is byte-identical
  to before - bench outcome hashes are unaffected.
- Team tally log lines ("Team N: X objective(s)") appear only when a real multi-player team exists,
  keeping existing 1v1 logs byte-identical (#193 determinism claims).

## Notes
- 2026-07-22: Implemented. Engine-only: `GameResult` (new `WinnerPlayers`, team-aware `ForWin` with
  name joining), `VictoryCalculationStage` (team aggregation + roster message). Tests:
  `VictoryCalculationStageTests` `CreatePlayer` now defaults each player to their own team (pre-#257
  degenerate case, all old pins green) + 5 new team facets: pooled sum decides over individual tally,
  message lists all players, zero-score teammate still named, 3-name comma join, team-sum tie.
  Engine 1806/0, full build clean, headless smoke exit 0 with unchanged summary line.

## GUI hand-verify checklist
- [ ] 2v1 game (two players teamed via #255): end banner reads "X and Y win!" listing both teammates.
- [ ] Teammates' objectives pool: the team with the higher combined count wins even if a lone opponent
      holds the single largest individual count.
