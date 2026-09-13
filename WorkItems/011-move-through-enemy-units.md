# 011 — Moving through enemy units + 1" standoff

## Goal

Implement the empty `MovementUtilities.ValidateMovingThroughEnemyUnits`. A move may not pass
through or stack on an enemy base, and a model that isn't charging must end at least
`ENEMY_STANDOFF_DISTANCE_INCHES` (1", base-to-base) from enemy models.

## Decisions

- **"Charge" is detected geometrically, not from an action flag.** `IMovementActionContext` carries
  no charge/advance/rush declaration — the move is unified (one path up to `hardCap`). And with the
  default constants `RUSH == CHARGE == 12`, the existing distance-based charge inference
  (`ValidateChargeReach`, "any model beyond Rush") is inert, so it can't be reused to gate the
  standoff. Instead: a move **engages** an enemy unit when any moving model ends within
  `MELEE_RANGE_INCHES_HORIZONTAL` (2", centre-to-centre) of one of its models — the same
  completed-charge test `ValidateChargeReach` already uses. Engaging a unit waives the standoff for
  **all** that unit's models (you legitimately end within 1" of every model of a unit you charge,
  not just the one you reached — hence the per-unit key on the footprint).
- **One segment-distance test covers pass-through and stacking; it naturally permits charge-to-contact.**
  A clean charge's closest approach to the target is its own destination (tangent at base contact),
  so a strict "interior point of the path comes within < contact distance" test allows it while
  catching a path that crosses an enemy base or ends overlapping one.
- **Only moves that close the distance are penalised** (`endGap < startGap`). A model already inside
  the standoff — e.g. left there by a pile-in / consolidation move, which aren't enemy-checked — can
  still move away or hold without being trapped in an unsatisfiable state (which would crash the
  throwing `DefinePathStage`).
- **Enemy data as `EnemyModelFootprint` (centre + radius + per-unit key).** Replaced the charge
  `ValidatePaths` overload's `IEnumerable<Position> enemyModelPositions` with footprints;
  `ValidateChargeReach` reads `.Center`. `GetEnemyModelPositions` → `GetEnemyModelFootprints`.
- **Resolver parity to avoid the throw.** `DefinePathStage` is authoritative and throws (no retry) on
  an invalid move, so every move source pre-checks with the same validator: the GUI Done-gate and the
  AI resolver already called the charge overload (now fed footprints); the CLI human path was upgraded
  from the basic overload to the enemy-aware one; and the AI + CLI `AutoAdvance` carry a
  validate-and-back-off loop so EOF/auto play never emits a rejected move.

## Notes

### 2026-06-14
- Discovered #050 was already merged to master (engine `57e667b`, bump `896303d`) — index checkbox
  was overdue; ticked it, no code needed.
- Implemented #011 on branch `011-move-through-enemy-units` (both repos). Engine commit `f49e089`,
  suite 488/0. App: GUI/CLI resolver wiring + CLI auto-advance backoff; full build clean; headless
  smoke ran a 4-round game to completion, exit 0.
- DEFERRED, recorded in the index: (a) advance-vs-charge-into-contact is indistinguishable without
  action-type plumbing (ending in base contact is allowed regardless of declared action; the
  consequences live in the melee/charge stages and #051); (b) `ConsolidateStage` and the
  Vanguard/Strafing executor move paths still use the no-enemy `ValidatePaths` overload, so they
  aren't move-through/standoff checked yet.

## Outcome

`ValidateMovingThroughEnemyUnits` implemented (pass-through + stacking + 1" standoff with melee-range
charge waiver). Engine `f49e089`; superproject bump + app wiring in the follow-up commit. Suite 488/0,
headless-verified. Two nuances explicitly deferred (see Notes).
