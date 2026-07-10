# 211 — Solo AI mover submits a path through impassible terrain (rare fault)

**Goal:** the solo-rules movement resolver never submits a validator-rejected path (the same
G3 validate-or-decline standard the Tactician's mover follows).

## Evidence (2026-07-10)

- a5-3-gate: BB vs HEF, seed 3001, swapped=True — "Response to DefinePathStage movement request
  was invalid: Moves through impassible terrain" (twice, one per failed retry presumably).
- Repro: `smoke --seed 3001 --a HEF --b BB --profile-a solorules --profile-b tactician` faults;
  the log ends inside a SOLO (HEF) unit's activation - "Activating: Retributors (Combined)" ->
  Choose Action -> (DefinePathStage) - so the invalid path came from the solo
  AiDefineMovementResolver, not the Tactician (whose games showed 0 own faults in 3 consecutive
  1800-game gates).
- Same seed solo-vs-solo plays clean (different trajectory) - the Tactician's new behavior
  (A5-3 objective gradient) steers games into board states that expose the solo edge case.

## Relation to existing items

Same family as #159 (solo/CLI DefinePathStage cohesion crash, residual ~1/10 runs) - a different
validation reason (impassible sweep vs cohesion) from the same "mover does not fully pre-validate"
gap. Whoever picks up #159 should fix both with one validate-or-decline ladder on the solo mover
(mirror MovementPlanner.ValidateWithBackoff).

## Notes (newest first)

**2026-07-10 — filed** from the a5-3-gate fault reading. Frequency ~1/1800 games. Solo-rules
behavior is benchmark-frozen (D1), so the fix needs a pin + a baseline note when it lands.
