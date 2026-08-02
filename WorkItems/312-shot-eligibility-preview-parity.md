# 312 — Targeting previews must ask the rules' own can-hit question

**Status**: implemented (awaiting GUI hand-verification)
**Related**: #276 (truthful attack-beat endpoints — the "thing that works" this borrows from), #245 (dice
caption strip), #212 (team-aware enemy test), #158 (never aim at corpses)

## Goal

Playtest report (2026-08-02, screenshot): during a shoot action the panel drew green fire lines from the
shooter straight THROUGH a blocking terrain piece. The shot itself was legal — the unit had line of sight
to other models of the target unit — and the attack animation aimed truthfully when it fired. Only the
preview lied.

Cause: `GuiChooseRangedAttackResolver.DrawHoverLines` aimed each shooter's line at
`NearestModel(...)` — nearest by raw base-to-base distance, no sight test. Its own comment admitted it
("per-model LoS data isn't in the request"). When the closest defender was the blocked one, the line went
through the wall.

Owner's ask: fix it, and have the preview use the SAME can-hit mechanism as the paths that already work.

## Notes

- 2026-08-02 (implemented — ENGINE + app):
  - **Engine**: new public `ShotEligibility` (`ShootStage/ShotEligibility.cs`) — the one answer to
    "which defender can this shooter actually hit":
    - `BuildBlockers(tableState, attacker, defender)` — terrain snapshot + `BuildModelBlockers`
      (intervening enemy bases block; allies and off-table models don't). Was hand-rolled at every
      call site; forgetting the model half silently lets shots pass through a crowd.
    - `NearestVisibleModel(from, shape, facing, targets, blockers, maxRange)` — nearest living, placed
      defender that is both seen and in range, by the engine's base-to-base 3D metric. **A null blocker
      list means the weapon ignores line of sight** (Indirect/Takedown), which is how every caller
      already spelled that case.
    - `CanHitAny(...)` — the same question as a bool.
    - `AttackBeatPositions` (#276, the truthful animation endpoints) now delegates: `CanModelShoot` is
      `CanHitAny`, and both blocker lists come from `BuildBlockers`.
  - **App**: the shoot panel's fire lines aim at `NearestVisibleModel` instead of `NearestModel`, with
    blockers from `ShotEligibility.BuildBlockers` and `null` when `WeaponOption.IgnoresTerrain`.
    - **No range data had to cross the wire**: `modelsThatCanShoot` already means "this model can hit
      this unit", so the nearest VISIBLE defender is necessarily the in-range one (every other visible
      defender is farther away). Recorded because the obvious alternative — adding
      `EffectiveRangeInches` to `WeaponTargetStats` — would have been a needless request/wire change.
  - **Team awareness** (same report, second ask): every can-hit / can-charge path was ALREADY team-aware,
    so no behavior change — but the rule was hand-copied in three places. `GuiDefineMovementResolver`
    (x2) and `TacticalOverlayController` (x2) now call the existing `TeamAwareness.IsEnemyUnit`, and
    `FdgRaylib.Tests/TeamAwarenessTests` pins "another player on my team is not an enemy" plus the
    no-teams fallback.
  - Tests: engine `ShotEligibilityTests` x5 (nearer-blocked skipped, all-blocked -> null, null blockers
    ignore LoS, range/corpse/unplaced filtering, and an agreement test that runs the REAL
    `ChooseRangedAttackStage` and checks every model it calls a shooter has a visible defender for the
    preview to point at). App: `GuiChooseRangedAttackResolverTests` gains the through-terrain case.
    2561 engine + 885 app green.

### Deliberately NOT changed

- **`ChooseRangedAttackStage.CanWeaponShootAtUnit` keeps its own cached implementation.** It is the
  authority, not a preview, and its per-attacker LoS cache spans that model's weapons — routing it
  through the helper would recompute sight lines per weapon on the hot targeting path. The agreement
  test above is the anti-drift guard instead.
- **`GuiDefineMovementResolver.DrawTargeting` (the post-move preview) was audited and left alone.** It
  already runs the engine's `EvaluateSightLine` per candidate, picks the nearest CLEAR model, and draws
  a red stub for a blocked one — plus it needs the clear/cover/blocked distinction that
  `NearestVisibleModel` deliberately doesn't model. If through-terrain lines are ever seen during a MOVE
  (no shoot panel open), this is the place to look and the assumption to re-test.

## Outcome

_(pending GUI hand-verification: open the shoot panel against a unit with some models behind blocking
terrain and confirm every green line lands on a model that is actually visible)_
