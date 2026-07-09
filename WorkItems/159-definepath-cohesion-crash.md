# 159 — Intermittent DefinePathStage cohesion crash from AI/auto movement

**Status**: in-progress (code complete + headless-verified; awaiting GUI hand-verification)
**Related**: #017 (where it was the "(separate, still-open)" note), #089/#108 (AI packing), #011/#090 (move-through / consolidation enemy checks), #150/#153 (shape-aware geometry — the containment bug fixed here), #018/#019 (melee pile-in/consolidation stacking, the out-of-scope deeper cause)

## Goal
A headless HEF (High Elf Fleets) game intermittently ends with a `DefinePathStage` game error
(`Breaks cohesion: Model is further than 1 inch from the closest model`). Find the real root cause and make
the AI/auto resolvers never submit a move `DefinePathStage`/`ConsolidateStage` rejects, so a full game plays
to a real ending every time. Repro recipe: generate a HEF army
(`--book-to-army FdgRaylib/Assets/Books/HighElfFleets.fdgbook /tmp/hef.fdgarmy`), then loop
`--headless --army /tmp/hef.fdgarmy` and grep the output for `was invalid` / `Breaks cohesion` (endings are
graceful exit 0, so don't rely on the exit code).

## Notes

- 2026-07-09 (later, from #194's harness): **strong seeded repro found** —
  `dotnet run --project FdgLab/FdgLab.csproj -- smoke --seed 1027 --repeat 10` (builtin armies, both
  slots AI) hits the crash **~8/10** and captures the full `[GAME ERROR]` stack: the rejected move is
  submitted during a normal AI activation; `smoke --dump-logs DIR` gives per-run transcripts. Two AI
  slots (no CLI AutoAdvance in the loop) — so `AiDefineMovementResolver` (or a consolidation path) is
  the submitter, answering the open isolation question below. Also explains why the crash flakes even
  at a fixed seed: movement paths themselves are nondeterministic run-to-run (**#198**) and the crash
  sits downstream of them — fixing #198 should make seed 1027 either always or never crash.
- 2026-07-09 (measured during #202's pre-push verification, on the built-in two-unit EOF default army):
  the residual reproduces at roughly **1 run in 10**. Paired A/B, 24 headless runs each:
  **origin/master `334b58c` / engine `38c5aa5`: 2/24**; **#202 branch: 3/24**. Statistically
  indistinguishable, so #202's morale-sequencing and back-out changes neither caused nor worsened it —
  they only shift the random trajectory that reaches it. Confirms the 2026-07-08 counterexample below
  and settles that this is **not** fixed: the item must not be ticked on the strength of "0/24".
  Not yet isolated to a resolver: in a headless smoke the human slot answers via the CLI resolver's
  EOF `AutoAdvance` and the other via `AiDefineMovementResolver`, and either could be the submitter.
- 2026-07-08 (drive-by observation during #169 verification): a plain headless smoke (the built-in
  two-unit EOF default army, NOT the HEF repro army) ended once in 4 runs with the `DefinePathStage`
  "Breaks cohesion: further than 1 inches" game error (graceful exit 0; 3/3 clean ties on rerun).
  Engine was at `0de69be` + the #169 spillout change (no transports in that army, so unrelated).
  The 0/24 repro tally therefore has a counterexample - the residual is live, and reproducible even
  on the default army. Worth re-running the repro loop when this item's hand-verification happens.
- 2026-07-04: **Deeper cause fixed — melee no longer stacks a model inside an enemy base.** (Was flagged
  out-of-scope on 2026-07-03; the user asked to close it.)
  - The geometry fix stopped the *crash*, but instrumentation showed the *overlap itself* still happened in
    **19/20** AH games (models sitting inside enemy bases, gaps to -3.5). Bisected the melee movers with
    before/after gap snapshots: **`PileInStage` creates it** (a defender went from gap 0.006 to -1.011 in one
    pile-in, compounding across activations).
  - **Root cause:** `PileInUtilities.ComputePileInMoves` only obstacle-checked the defender against the
    **charging unit's** models and its own unit-mates. A defender piling toward its charger could plow straight
    through a **third-party enemy unit** (or a unit it was already engaged with) — nothing clamped that lane.
    Secondary: the step's contact cap (`NearestB2BAt`) used the **facing-less** base gap, which over-estimates a
    rotated rectangle's reach and could overshoot the target charger too.
  - **Fix:** (1) new `MovementUtilities.GetEnemyModelFootprints(..., excludeUnit)` overload; `PileInStage` passes
    every enemy of the defender EXCEPT the charging unit as hard obstacles, and `LimitStepByObstructions` clamps
    the pile-in step against them (stop at contact, never overlap). (2) `NearestB2BAt` now uses the facing-aware
    base gap. Circle-vs-circle is byte-identical (facing irrelevant); `otherEnemyModels` defaults to empty so
    existing callers/tests are unchanged.
  - Tests: `PileInTests.DefenderPilingTowardCharger_DoesNotPlowThroughAThirdPartyEnemy`. Engine 1134/0, app 78/0.
  - Headless: **AH stacking 0/20** (was 19/20); multi-faction stacking + crash sweep clean.

- 2026-07-03 (cont.): **Second variant (enemy-crossing) root cause found + fixed; AH 0/30, sweep clean.**
  - After the consolidation fix, HEF was 30/30 clean but Alien Hives still crashed ~1/13 in **`DefinePathStage`**
    (normal movement) with `Moves through an enemy unit` — the ledger's "second variant".
  - **Diagnosed with temporary DefinePathStage logging:** the move was an **exact hold** (segLen=0, every
    `movedCloser=False`), yet `BaseShapeGeometry.SurfaceGap2D` reported **startGap=+0.63** for the mover (a
    circle, r=1.18) and an enemy (a rectangle, r=1.81) at the **identical centre**. Two concentric bases must
    overlap (gap ~ -3), so SurfaceGap2D was returning a *positive* gap for a **contained** circle.
  - **Root cause (a real #150/#153 geometry bug):** in `BaseShapeGeometry.SeparatedOnAnyEdgeNormal`, the SAT
    overlap test used `overlap <= 0f` to mean "separated". A zero-WIDTH projection (a point / circle hull)
    whose centre is strictly inside the other's slab yields `overlap == 0` on every rectangle axis, so a circle
    contained inside a rectangle was wrongly classed as *separated* and fell through to `HullSeparation`, which
    returns the (positive) point-to-edge distance. The move-through pass-through guard
    (`startGap > 0.1 && endGap > 0.1`) then believed the model started/ended *clear*, and the (correct)
    `DoesSweptBaseIntersectZone` reported the overlap -> **false-positive "moves through an enemy"** on a
    stationary model a melee pile-in had stacked concentric inside a large base.
  - **Fix:** `overlap <= 0f` -> `overlap < 0f`. A strictly-negative overlap is a real separating axis; `== 0`
    is a shared boundary (and the zero-width-inside case), which must NOT count as separation. Just-touching
    bases still resolve to gap 0 via the penetration branch (min overlap 0), so the touching boundary and
    circle-vs-circle are byte-unchanged; only genuine containment flips from +gap to overlapping (negative).
  - Tests: `BaseShapeTests.Gap_CircleInsideRectangle_IsNegative`,
    `MoveThroughEnemyValidationTests.StackedInsideLargeEnemyBase_Holds_Accepted`. Engine 1133/0, app 78/0.
  - Headless: AH 30/30 clean (was ~1/13); default smoke exit 0; multi-faction sweep clean.
  - **Note — the deeper cause of the stacking:** melee pile-in could leave a small model concentric inside a
    large enemy base (an illegal overlap). The geometry fix stops that pre-existing overlap from crashing later
    movement; the actual overlap was then traced to pile-in and fixed on 2026-07-04 (see the newest note above).

- 2026-07-03: **Root cause found + fixed; 25% -> 0% repro.**
  - Reproduced the crash at **5/20 (25%)** with the HEF smoke army. Contrary to the index's "mixed base sizes"
    hypothesis, **every** crash was in **`ConsolidateStage`**, not the normal movement path:
    `Response to ConsolidateStage was invalid: Breaks cohesion`.
  - **Mechanism:** after a melee, a unit that lost a *middle* model has survivors >1" apart (a casualty hole).
    The consolidation resolvers (`AiConsolidationMoveResolver`, engine; `ConsolidationMoveResolver`, app) only
    offered a **rigid unit-wide delta** (or a hold on EOF). A rigid translate preserves the hole, so it can
    never restore coherency — and with a 1" Disengage cap it often *can't* be restored at all — yet
    `ConsolidateStage` enforced coherency strictly (its own comment wrongly claimed "coherency is preserved
    trivially when the unit moves as a single delta"). Result: no legal consolidation exists -> throw ->
    game-ends-on-error.
  - **Fix (with the user — "drop the hard requirement, but keep a mechanism ensuring the attempt to bring them
    together"):** two guards.
    1. **Resolvers re-form** the survivors toward their centroid within the cap
       (`CohesiveFormation.ReformTowardWithinCap` + `IsCohesive`) instead of a rigid delta when the unit is
       broken — the attempt to bring them together, pulling them as tight as the 1-3" allows.
    2. **`ConsolidateStage` coherency becomes lenient** (`MovementUtilities.ValidateConsolidationPaths` +
       `ValidateCoherencyNotWorsened`): a consolidation is only rejected for coherency if it makes coherency
       *worse* than before the move (per-model nearest/farthest gap can't exceed `max(limit, its pre-move
       gap)`). Mirrors the existing enemy-standoff "only penalise moves that close the distance" rule. A hold is
       therefore always legal (unit can't be trapped), but the unit also can't scatter further. Cap/terrain/
       enemy-crossing checks stay strict.
  - **Also fixed a real *latent* mixed-base bug** (the index's hypothesis, but a different code path):
    `CohesiveFormation.PackGrid` spaced the whole grid by the largest base, stranding small models >1" from
    every neighbour in a mixed-base unit — which would crash the *movement* re-pack path. Rewrote PackGrid to
    place models edge-to-edge per-row at their own base size, stack rows by their tallest model, and bump the
    column count so no row is ever left with a single (neighbourless) model. So the 1" rule now holds for any
    base-size mix. Kept as part of this item with its own tests.
  - Tests: `CohesiveFormationTests` (mixed-base pack, IsCohesive, ReformTowardWithinCap),
    `AiDefineMovementResolverTests.Resolve_MixedBaseCasualtyUnit...`, `AiConsolidationMoveResolverTests`,
    `ConsolidateStageTests` (holed-unit hold accepted; scattering rejected). Engine 1131/0, app 78/0.
  - Headless: 30/30 HEF runs clean after the fix (was 5/20 crashing before).

## Decisions
- **Consolidation coherency is one-directional-lenient, not skipped.** Fully skipping the check would let a
  buggy/adversarial response scatter a unit; requiring strict end-coherency traps a casualty-holed unit with no
  legal 1" move. "Not worse than the pre-move state" is the sweet spot: a hold always validates (no trap) and a
  re-form validates (improves), but a scattering move is still rejected. This matches how the enemy-standoff
  rule already only penalises moves that *close* the distance.
- **The resolver, not the validator, guarantees the *attempt*.** We control the AI/CLI resolvers, so they
  actively re-form (`ReformTowardWithinCap`). The validator can only cheaply verify "not worsened"; it can't
  verify "tried its best" for an arbitrary (e.g. networked) response, so that guarantee lives in the resolver.
- **Cohesive units keep the existing rigid-delta consolidation** (behaviour unchanged); only *broken* units
  trigger the re-form. Keeps the common case identical to before.
- **"Mixed base sizes" was a partial red herring for the actual crash** but pointed at a genuine latent PackGrid
  bug, fixed here rather than deferred.
- **The enemy-crossing variant was a geometry-primitive bug, not a resolver bug.** Fixing `SurfaceGap2D`
  containment at the source (one comparison) beats hardening the move-through check, because the wrong +gap for
  a contained base also silently mis-answered cohesion/collision/deploy overlap queries elsewhere. `<= 0` -> `< 0`
  is safe: touching stays gap 0, separated stays positive, circles are byte-unchanged; only containment flips.
- **Four independent root causes fed one crash symptom.** (1) consolidation rigid-delta on casualty holes
  [primary, HEF], (2) `SurfaceGap2D` containment [AH enemy-crossing], (3) `PackGrid` mixed-base spacing [latent
  movement re-pack], (4) pile-in not obstacle-checking third-party enemies [the actual source of the overlap].
  Each needed its own fix. The ledger's single-cause "mixed base sizes" hypothesis matched only #3.
- **Bisecting the melee movers needed before/after snapshots, not point-in-time logs.** A point-in-time overlap
  check re-flagged the same overlap in every downstream stage (pile-in re-logs a charge overlap, etc.), making
  all three movers look guilty. Snapshotting each unit's min-gap-to-each-enemy BEFORE a stage and comparing
  AFTER isolated the ONE stage that *created* a new deep overlap (pile-in).

## Outcome
_(pending GUI hand-verification / merge)_
