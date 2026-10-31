# 284 — Deployment overlap: second AI block deployed concentric with an ally (YellowDeployedOverGreen)

## Goal

Find why yellow's Retributors (Combined) deployed on top of green's Saurian Guardians (Combined)
(`YellowDeployedOverGreen.fdgsave`, 9 overlapping bases, worst 0.63"), and add the check Chris asked for
("there should be a check to prevent that") so no upstream failure can silently corrupt the board again.

## Established facts (2026-07-27)

- Both blocks sit at **exactly their Tactician preferred centres**: Retributors (27.00, 42.00) = aim
  objective (27,25) x, depth-6 z, unclamped; Saurians (27.00, 42.05) = same aim, czMin-clamped
  (42.055 with the hero's 0.985" circumscribed radius). Both bots' first deployments aim at
  objective[0] by round-robin design, so a shared aim is expected; the overlap is not.
- The sweep returns the preferred centre only when it is fully legal, so the second deployer's
  occupancy scan **did not contain the first block** (with it visible, penalty > 0 there and the
  sweep moves off).
- **Cannot reproduce with the real resolver on the real save**: loading the save, blanking either
  overlapping unit and re-running `TacticianPlaceObjectsResolver` deployment places it CLEAN
  (0.000" overlap) both on the full final board and in the near-empty first-deployment
  configuration (only the other block present). Tested both directions (diagnostic test since
  removed; recipe: `GameSaveSerializer.Load`, `SetPosition(0,0)` on one block, resolve a
  `PlaceObjectsRequest` with zone (0,72,39,48)).
- Positions commit synchronously and strictly sequentially (`DeployUnitStage.Enter` writes
  `SetPosition` before `OnFinish`); all bots share the host's one `GameDataStore`
  (`LobbyViewModel_Host` creates each bot's `FDGGame_AsLocal` over `_gameDataStore`). No replicas.
- `PlacementRequesting.RequestMandatoryPlacement` throws on cancel — no naive fallback path.
- Ruled out: Re-Deployment stage (no unit in this game grants it; Highborn is movement-only),
  Scout/deferred placement (neither unit has Scout), deploy-time embark (the embarked units are
  Raptor Riders x2 and a Knight Brothers 5-man, parked at origin), `StampLegacyReserves` (only
  stamps origin-parked units), dead models (nothing dead during deployment; that was the separate
  CantAmbushHere bug, fixed 2026-07-27), zone fullness, and the old blind fallback (prior session:
  old vs new code place identically here).

## Prime remaining suspects

1. **A second, stale engine instance racing the real one after a resume/back-out** — the #279
   teardown class (fixed only recently; the game was played across several sessions on builds that
   may predate it). Two deployment flows interleaved on one store would produce exactly
   "both at preferred, second blind to the first" and is unreproducible in-process.
2. A since-fixed unit-visibility state (`GetIsOnBattlefield` false for an on-table unit at that
   moment: a lingering `InReserve`/`EmbarkedIn` token later cleared — the save shows no tokens on
   either unit now, so only a transient would fit).

## The defensive check (design fork - needs sign-off)

`DeploymentSelection.ValidatePosition` still has the literal TODO "make sure the models aren't
overlapping" - the engine gate checks only zone containment, so DeployUnitStage commits whatever a
resolver returns. Options:

- (a) Commit-time warn: log loudly (unit pair, worst depth, the occupancy view size) when a deploy
  placement overlaps on-table models. Zero behavior risk; turns any recurrence into a diagnosis.
- (b) Warn + auto-repair: on violation, re-place via the existing least-overlap machinery
  (`AiPlaceObjectsResolver` sweep) against a FRESH occupancy view. Fixes the symptom whatever the
  upstream cause; small risk of fighting a legitimately cramped zone.
- Recommendation: (b), with (a)'s logging kept either way.

## Notes

- 2026-07-27 (later): **Option (b) built and committed** - `PlacementCommitGuard` (StateMachine/) wraps
  `RequestMandatoryPlacement` at all five mandatory-placement commit seams (DeployUnitStage,
  ReDeploymentStage, PlaceDeferredUnitsStage/Scout, StartOfRoundExtraActionStage/Ambush,
  SpilloutExecutor). On interpenetration > 0.01in vs any other unit's living on-table model
  (true oriented footprints), it warns in the game log naming both units and the depth, then
  re-places through `AiPlaceObjectsResolver` (fresh occupancy, honours the request's zone /
  enemy-distance / edge constraints); a still-cramped zone commits least-bad with a second warning.
  Tests: `PlacementCommitGuardTests` (clean pass-through untouched + overlap re-placed clear
  with the warning). Suite 2158/2158, app 592/592, headless smoke clean.
  **Root cause of the original save remains open** - the guard converts any recurrence into a
  logged diagnosis (watch for the WARNING line in future games).
- 2026-07-27: investigation above; awaiting Chris's pick on the check design.
