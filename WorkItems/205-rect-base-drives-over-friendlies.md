# 205 — AI unit with a large rectangular base drove over friendly models

**Status:** DONE 2026-07-11 (engine change; app pointer bump). NOT base-shape specific - it happens with
circle bases too (Chris confirmed); the real gap was a missing engine rule, not the collider.
**Related:** #150 (base-shape geometry everywhere), #182 (move through friendlies without stopping
on them), #011 (ending-stacked checks), #018/#019 (pile-in/consolidation stacking), #191 (shared AI
movement ladder; this shifts its benchmark outcome hashes - see note)

## Report (verbatim intent)

During a game, an AI unit with a large rectangular base ended up driving over friendly models —
Chris believes during a charge or pile-in. Whether the offense is passing THROUGH friendlies
(legal per the rules? see #182's scoping: friendly pass-through is allowed, ending stacked is not)
or ENDING on top of them (never legal) needs establishing first.

## Exploration notes for whoever picks this up

- Movement validation today only checks ENEMY footprints (#182's headline); the "can't end
  overlapping a different friendly unit" guard does not exist yet - if the rect base ENDED on
  friendlies, this may simply be #182 manifesting at its ugliest (a big vehicle base makes the
  missing guard visible), and the fix may just be #182 with rect-base-aware geometry.
- Pile-in/consolidation paths (`PileInStage`, `ConsolidateStage`, `AiConsolidationMoveResolver`)
  have their own move construction - check whether they run ANY overlap validation at all.
- #150's shape-aware geometry landed for collision/swept paths; verify the AI's charge/pile-in
  construction uses the oriented rectangle, not the circumscribing radius (a rect base validated
  as a circle can legally "cover" models its true footprint overlaps... or vice versa).
- Repro: play/replay a pool game with the HDF tough/vehicle list (large rect bases) vs a horde;
  seeded games make any observed instance replayable exactly - grab the seed when it happens.

## Notes

- 2026-07-09 — filed.
- 2026-07-11 — **fixed.** Confirmed root cause by inspection: the movement validator only ever checked
  ENEMY footprints and the moving unit's own cohesion - nothing forbade ENDING stacked on a *different
  friendly* unit. The GUI resolver enforced it live (`WouldOverlapAnyModel`), so a human never saw it, but
  the AI move resolver AND the AI consolidation resolver validated only against enemies. Owner's rule
  (confirmed): pass THROUGH a friendly is legal, ending ON one is not; no standoff band for friendlies -
  only true base overlap. Scope signed off: ALL movement (Move/Charge, triggered post-combat, Consolidation,
  Pile-in).

## Outcome

Engine change (submodule) + superproject pointer bump, owner-authorized for this item.

**Core (`MovementUtilities`):** new `EErrorReasonType.EndedOnFriendlyUnit`; `GetFriendlyModelFootprints`
(team-based, excludes the moving unit, on-battlefield living only); `ValidateEndsOnFriendly` - flags a model
whose END position base-overlaps (`BaseShapeGeometry.AreColliding`, i.e. SurfaceGap < 0, so mere contact is
fine) a friendly it did NOT already overlap at its START (the "only NEWLY stacked" guard means a unit is
never trapped). Threaded as an optional `friendlyFootprints` arg through the per-model / max-distance
`ValidatePaths` overloads and `ValidateConsolidationPaths`.

**Enforcement (authoritative, all throw on an invalid move):** `DefinePathStage` (Move/Charge),
`MovementExecutor.TryMove` (triggered), `ConsolidateStage` all pass friendly footprints. `PileInStage` folds
friendlies into its obstacle list (pile-in stops the mover short of obstacles, so it just halts short - no
throw). Because these stages throw, the resolvers had to learn the rule too or they'd crash:

**Resolvers back off:** `MovementPlanner.ValidateWithBackoff` gained a friendly param + `LiveFriendlyFootprints`
(team-based, tableState); `AiDefineMovementResolver`, `AiConsolidationMoveResolver`, `TacticianMovementResolver`
(+ `PlanMoveToward`), and the CLI `DefineMovementPathResolver` / `ConsolidationMoveResolver` all compute and
pass friendlies. Both GUI resolvers already prevented it (`WouldOverlapAnyModel`) - no GUI change.

**Tests:** `EndsOnFriendlyValidationTests` (6 cases: ends-stacked rejected, pass-through allowed,
base-contact allowed, pre-existing-overlap not trapped, no-friendlies no-op, rectangular shape-aware).
Mutation-checked (neuter the check -> the two rejection cases go red). Engine 1583/0, app 325/0.

**Verify:** cited 100-game bench 0 faults; HDF vehicle-heavy (rect bases) vs Orks horde - the report's
scenario - 40 games 0 faults; headless smoke (CLI path) clean. Instrumented the check: it fired **306 times
across 20 AI games**, so the AI genuinely tried to end on friendlies and now backs off every time.

**Behavior/hash note for #191:** this deliberately changes AI movement, so the #191 benchmark outcome hashes
shift (BB-vs-DarkElf 100-game `D47BA61C1305013C` -> `C9DE660B0836A512`). The #191 hashes live only in
comments/ledger (no test asserts them), so nothing broke - but the #191 owner should re-baseline the recorded
hashes against this engine.

Engine commit: `6dd54a7`; superproject pointer bump (+ CLI resolver friendly-awareness): this commit.
