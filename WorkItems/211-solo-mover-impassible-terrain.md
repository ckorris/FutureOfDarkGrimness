# 211 — Solo AI mover submits a path through impassible terrain (rare fault)

**Status**: closed 2026-07-22 (fixed by #256's StayInPlaceValidated; 3200-game gate-replica clean)
**Related**: #256 (the fix), #159 (same "mover does not fully pre-validate" family), #208 (decline gate)

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

**2026-07-22 — VERIFIED: gate-replica bench 3200/3200 clean.** `bench --pool FdgLab/armies
--profile-a tactician --profile-b solorules --games 50 --seed-base 3000 --timeout 240` at
superproject `a333167` / engine `d568b77`: **0 faults, 0 timeouts** across 3200 games (the
post-2026-07-10 ordered-pair pool shape, 64 matchups x 50 - a superset of the filing gate's 36
matchups / 1800 games; same seed base, so seed 3001 BB/HEF was replayed in both side
assignments). Outcome hash `6D36F2D7603BCC45`. The filing frequency was ~1/1800, so 3200 clean
games at the same distribution, on top of the structural argument below, closes this.

**2026-07-22 — root-caused to the pre-#256 unvalidated stand-still early-outs; closed by #256's
StayInPlaceValidated.** Code archaeology against the gate-time engine (`6b1e444`, the exact
submodule SHA the superproject pointed at on 2026-07-10):

- At gate time, `AiDefineMovementResolver` had exactly two paths that could submit a NON-zero-length
  move without validating it: the `enemyFootprints.Count == 0` early-out and the `dist < 0.01`
  early-out, both returning a raw `MovementPlanner.StayInPlace` re-pack. A re-pack is a real move
  (PackGrid slots, non-zero segments), so its sweep is subject to
  `ValidateMovingThroughImpassibleTerrain` - a unit parked against an impassible piece could re-pack
  a model THROUGH it and fault the stage. Every other gate-time path was already validated
  (`ValidateWithBackoff` validated each rung + the StayInPlace fallback) or exempt (the hold-exact
  last resort is zero-length, and the zero-length impassible exemption predates the gate,
  engine `a4f2187`, 2026-06-25).
- #256 (engine `f7b6d78`, 2026-07-22) routed both early-outs through `StayInPlaceValidated`
  (validate the reform, degrade to the zero-length hold), for the friendly-stacking flavor of the
  same hole (bench seed 1051). That closes the impassible flavor too - same validation call.
- Belt-and-braces on top: #208's `AllowCancel` decline gate (strict full `ValidatePaths` on the
  final candidate; `DefinePathStage` always sends `allowCancel: true`) means any residual invalid
  candidate is DECLINED back to Choose Action rather than submitted. Lenient-fail implies
  strict-fail, so a stage-rejected path cannot pass that gate. The G3 validate-or-decline ladder
  this item asked for is therefore in place: ladder rungs validated, stand-stills validated,
  hold-exact impassible-exempt, decline gate as the backstop.
- The original repro is stale at HEAD: `smoke --seed 3001 --a HEF --b BB --profile-a solorules
  --profile-b tactician` now plays to a clean Win (4 rounds, 0 "was invalid" lines) - trajectory
  shifted by the engine changes since the gate, so it neither proves nor disproves; the gate-replica
  bench below is the real evidence.
- D1 note per the filing: no new engine change lands with this close - #256 already re-pinned the
  D1 benchmark hashes when its behavior change went in, so no fresh pin is needed here.

**2026-07-10 — filed** from the a5-3-gate fault reading. Frequency ~1/1800 games. Solo-rules
behavior is benchmark-frozen (D1), so the fix needs a pin + a baseline note when it lands.

## Outcome

Fixed as a side effect of #256 (engine `f7b6d78`, 2026-07-22), which routed the solo resolver's
two stand-still early-outs through `StayInPlaceValidated` - the only paths that could still
submit an unvalidated non-zero-length move (a `StayInPlace` re-pack whose PackGrid sweep could
cross impassible terrain). With those validated, every solo answer on `DefinePathStage` is now
validated, zero-length (impassible-exempt), or declined by the #208 strict gate. No new engine
change landed for this item, so the D1 pin requirement is satisfied by #256's own re-pin.
Verified with a 3200-game gate-replica bench (0 faults, 0 timeouts; hash `6D36F2D7603BCC45`).

Suggested (not done - engine submodule, needs sign-off): a regression test pinning the
impassible FLAVOR of the stand-still early-out (all enemies dead, unit parked against an
impassible wall whose re-pack would sweep through it), mirroring
`Resolve_AllEnemiesDead_StandStillNextToFriendly_ResultIsEngineValid`.
