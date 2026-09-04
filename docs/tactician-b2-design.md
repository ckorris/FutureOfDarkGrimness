# B2 design: composite action space + multiplayer backup (campaign step 6)

Authored 2026-09-03 (Fable, high effort - the campaign doc's "design: Fable / high, one turn").
Build is Sonnet / medium; this file is the spec the build is verified against. Design authority for
the WHY: `docs/ai-agent-plan.md` sec 9 (B2, B4, B5), invariant **G13(c)** (per-side reward vector,
max^n backup, bit-identical 1v1 reduction), Appendix A's generator rules (diversity-preserving
pruning, explicit feasibility), and the campaign doc's step 6 bullet.

**Status: design turn done, awaiting build.** Nothing here is built. The build lands in the engine
under `FutureOfDarkGrimness/Ai/Tactician/Search/`, hash-verified like every step 5 commit
(`8D6EFA0AF0B4019E` on the DOP-1 six-game cell; a changed hash is a stop).

---

## 0. What B2 is, in one paragraph

B2 is the tree: what a node is, what an edge is, how a child is created, how a leaf's value is
backed up to the root, and how many children a node is allowed to have. It is **not** the search
loop (B4 owns selection/time budgets/root parallelism) and **not** the leaf value (B3 owns the
evaluator). B2 therefore ships with a placeholder evaluator and a trivial, deterministic expansion
driver that exist only so the tree can be tested end to end; B3 and B4 replace them without
touching the tree.

## 1. What B2 inherits from B1 (facts, not assumptions)

| Inherited | Where | Consequence for B2 |
|---|---|---|
| Node boundary = `DeterminePlayerTurnStage.Enter`, after the acting player is known | `IActivationBoundaryHook` (5c) | A node is "player P is about to activate"; the tree interleaves players as the engine alternates them, including P19 overrides and reactivations - B2 never models turn order itself |
| Edge payload already exists | `SimulationService.Prescription(DataReference? Unit, string? Action, MacroAction? Macro)` | B2's edge IS a `Prescription` plus search bookkeeping; no new wire shape |
| A prescription that is not offered at play falls through to natural scoring (G3) | 5b's `TakePrescribedAction` | An edge can silently become "A's own move" at play time. **B2 must detect this** (sec 4.3) - a fell-through edge must never be credited with the outcome it did not produce |
| A prescribed activation skips `Urgency`, `Enumerate` and `Score` | 5b pin | Expanding a child costs a line, not a policy think: ~20-35ms/activation at 2k under load (5c table) |
| Line length is depth; the line serializes once at its end | `SimulationService.Run` | A leaf's state is live in the sim instance at the terminal boundary - evaluate it there, before the one Save (sec 5.2) |
| Snapshot size 401.6 KiB (2k) / 640.5 KiB (4k) | B0 | 500 stored nodes at 4k is ~320 MB - fine at v1, but sec 5.3 records the fallback |
| Per-simulation seed, probabilistic dice, sampled decisive rolls | `SimulationOptions` | Every simulation of an edge is one sampled outcome; sec 6 decides how the tree treats that |
| `TacticianPlanner.LastMacroLabel` only (not the `MacroAction`) is readable | 5c note (2) | The action space enumerates its own candidates (sec 3) - it does not read them off the planner |

## 2. Node

```
SearchNode
  Snapshot        string?          // materialized when the node is first expanded (sec 5.2)
  ActingSide      int              // team number of the player about to activate
  ActingPlayer    PlayerID         // the player (matters for 2v2: a side has two rosters)
  Terminal        GameResult?      // set when the line reached the game's end (EndedEarly)
  Visits          int
  ValueSum        SideValues       // sec 7: one float per side, summed over backed-up leaves
  Units           List<UnitBranch> // sec 3.2: level-1 children, ordered by prior
  Depth           int              // activations from the root
```

`ActingSide`/`ActingPlayer` are read from the snapshot's `GameProgressData` (cursor + teams), never
from the parent - a reactivation or a P19 `ActivatesNext` flag can make the same player act twice,
and the tree must follow the engine, not assume alternation.

A node whose `Terminal` is set has no children and its value is fixed by sec 7.1.

## 3. Edge = composite activation, enumerated in two levels

The plan's edge is `(unit, action, macro-action, primary target)`. Enumerating it flat is
unaffordable: at 4k a player has ~20 activatable units x a 16-candidate budget = ~320 edges, and
each unit's enumeration costs the planner's full think (`Enumerate` + `Score` ~165ms at 2k, B0). So
the edge is enumerated lazily in two levels, **both belonging to the same acting player**, so the
tree still backs up as if it were one edge (no intermediate max^n node).

### 3.1 Level 1 - which unit (cheap)

Candidates: the acting player's entries in `GameProgressData.UnactivatedUnits` that are alive and
on the battlefield - the same pool `ChooseUnitToActivateStage` offers (P19 overrides are already
applied by the time the hook fires, so the pool is the offer).

Prior: `TacticianActivationResolver.Urgency(unit)` - the A policy's own activation ranking,
normalized to a softmax over the pool. It is what A would pick, so the first-expanded unit is A's
unit (sec 8's reduction pin depends on this). Urgency is cheap (a per-unit scan, no enumeration).

### 3.2 Level 2 - which macro-action for that unit (the expensive one, once per (node, unit))

Candidates: `MacroActionGenerator.Enumerate(evaluator, tableState, unit, budget)` on the node's
loaded store, budget = `DefaultCandidateBudget` (16) at v1. The generator's Appendix A rules already
guarantee at least one feasible candidate per intent family and never prune by immediate value;
B2 must not add value-based pruning on top (sec 8 test 2 pins the family coverage).

Each candidate maps to an edge:

| Field | From |
|---|---|
| `Prescription.Unit` | the unit's `DataReference` |
| `Prescription.Macro` | the candidate |
| `Prescription.Action` | `ActionNameFor(candidate, offeredActions)` - **the planner's own mapping, made `internal static` and reused**, so the edge vocabulary is provably the planner's: Charge only when `Feasibility == Reachable`; Hold -> Shoot if offered else Pass; everything else -> Move. `offeredActions` at enumeration time is the full set the stage could offer (`Move`, `Charge`, `Shoot`, `Pass`, and `Cast` when the unit is a caster); an action the stage then does NOT offer is caught at play by sec 4.3, not predicted here |
| primary target | `candidate.TargetEnemy` / `TargetObjective` / `TargetAlly` - already on the `MacroAction`; **the shooting target inside the activation stays the A heuristic** (`TacticianRangedAttackResolver`) at B2. Prescribing it is a B5 extension through the same seam (`Prescribe` gains a target argument) if the charge-vs-shoot or focus-fire probes demand it - recorded, not built |

Plus one non-plan edge per applicable action: `Cast` (unit is a caster with a castable spell; the
planner's cast path picks the spell) and `Disembark` (unit is embarked). These carry `Macro = null`,
which 5b already handles (Cast/Disembark carry no plan).

Prior: the planner's `Score(candidate)` for plan-bearing edges, softmax-normalized within the unit;
Cast/Disembark take the mean prior (the planner scores them by a different path and B2 does not
try to make the two comparable - search does). `Score` needs the planner's `BeginActivation(unit)`
state, so level-2 enumeration runs on a **scratch `TacticianPlanner`** built over the node's loaded
store - never on a live game's planner (which would disturb its activation state).

### 3.3 Cost accounting (why two levels)

First simulation from a fresh node: one `Urgency` scan + one `Enumerate`+`Score` for the top unit -
the same work A does for one natural activation - then a line. Every further child of the same
unit is a line only. A new unit is one more `Enumerate`+`Score`. So B's cost is A's cost plus lines,
and B with a budget of one expansion plays exactly A's move (sec 8 test 4).

## 4. Child creation

### 4.1 Progressive widening at both levels

Allowed children at a node with `N` visits: `k(N) = ceil(C * N^alpha)`, **C = 2, alpha = 0.5** (the
plan's "progressive widening by visit count"). Applied at level 1 over units by the node's visits,
and at level 2 over a unit's macros by that unit's visits. Children are opened in prior order.
Tuned at B4 on the benchmark, recorded there; the constants are `SearchOptions` fields, not
literals.

### 4.2 Expansion = one line

Opening an edge from node `n` runs `SimulationService.Run(n.Snapshot, line)` where the line is the
edge's prescription followed by **`continuation` natural activations** (default 0 at B2 - the child
node is the very next boundary, whichever player it belongs to). B4 may set a continuation > 0 so
that a child is "our next boundary" rather than "the opponent's" (cheaper trees at the cost of
searching over the in-sim policy's replies); depth is a parameter from day one per the campaign
doc, and B2 exposes it without choosing it.

The in-sim policy for natural activations is `SimulationOptions.Profile` (default Tactician; the
SoloRules arm is 40% cheaper per 5c's table and is a real B4 decision - evaluation bias vs cost -
not made here).

### 4.3 Honored-prescription flag (new, required)

`SimulationService` gains, per boundary of the line, whether the prescription was **consumed** or
**fell through** (5b's fall-through leaves the planner scoring naturally; `HasPrescription` being
still set after the activation, or the planner's decision log being non-empty, is the tell -
build whichever is cheaper and pin it). `SimulationResult` carries `IReadOnlyList<bool> Honored`.

An edge whose prescription fell through is marked **infeasible at play** and closed: its visit is
not credited, its child is discarded, and the next edge in prior order opens instead. This is
Appendix A's "search never wastes rollouts on infeasible branches" applied at the one place the
generator cannot see - the stage's actual offer.

### 4.4 Callback-driven line (5c note 1)

`SimulationService.Run(snapshot, IReadOnlyList<Prescription?>)` stays. B2 adds
`Run(snapshot, ILineDriver driver)` where the driver is asked at each boundary for the next
prescription (`Prescription?`), `Stop`, or `StopAndEvaluate` - and, for the last, is handed the
live `ITableState` before the Save so the leaf can be evaluated in place (sec 5.2). The list-based
overload becomes a thin driver. Both must produce byte-identical results for the same line (pin).

## 5. Leaves, snapshots, memory

### 5.1 What a leaf is

The node at the end of a line. Its value comes from `IPositionEvaluator` (sec 7.2) applied to the
state at that boundary, or from the `GameResult` if the game ended inside the line
(`SimulationResult.EndedEarly`).

### 5.2 Evaluate live, save once

The line's terminal boundary has the store live in the sim instance. The driver's `StopAndEvaluate`
evaluates there (the C1 encoder reads `ITableState`; 1.7-3.6ms measured) and THEN the line saves its
snapshot - the child node is created with both. This keeps 5c's "one Save per line" and adds no
serialization for evaluation.

### 5.3 Memory

v1 stores the snapshot on every created child (it will be a parent the moment it is selected
again). Budget at B4's 500-game soak: nodes x 0.4-0.64 MB. If that soak shows the tree is
memory-bound, the recorded fallback is **branch-point-only storage**: a node keeps `(parent,
edge, seed)` instead of a snapshot and re-runs its line from the nearest stored ancestor on
expansion (deterministic under the per-simulation seed - sec 8 test 6 is what makes this legal).
That is precisely what 5c's line primitive was built to allow, and it is a B4 switch, not a B2
decision.

## 6. Dice: determinization at v1, chance nodes recorded as the upgrade

Every simulation samples dice under its own seed. Two ways to build a tree over that:

- **(a) Chance nodes:** a child per distinct resulting state (keyed by a state hash), widened like
  any other level. Correct, and expensive: it multiplies branching by the outcome spread and needs
  a cheap state hash at every line end.
- **(b) Determinization:** an edge's FIRST simulation fixes its child state; the subtree conditions
  on that one sample. Cheap, biased toward lucky first samples.

**Decision: (b) at v1, with the bias controlled by B4's root parallelism** - each worker is an
independent tree with its own seed, so the root's final choice (summed visits across workers) is an
ensemble over determinizations. The seed of an edge's simulation is derived from
`(worker seed, node depth, edge index)` so two workers never share a determinization and a single
worker is reproducible.

Detector, not a guess: the **charge-vs-shoot probe** (B-gate) is the play most sensitive to a lucky
charge roll. If B's answer there flips across seeds at the same budget, that is the evidence to
build (a); until then it is recorded here as the upgrade, not built.

## 7. Value vector and max^n backup (G13c)

### 7.1 `SideValues`

A `float[]` indexed by **team number** (not player), one entry per side in the game, each in [0, 1].
Terminal values from `GameResult`: 1.0 for the winning team, 0.0 for every other team on a Win; 0.5
for every team on a Tie. Faults are not nodes (a faulted line is discarded and its edge closed,
like a fell-through prescription).

### 7.2 `IPositionEvaluator` (engine seam; B3 fills it)

```
interface IPositionEvaluator
  SideValues Evaluate(ITableState state)   // one value per side, each in [0,1]
```

B2 ships `TerminalOnlyEvaluator` (0.5 for every side at any non-terminal leaf - the tree is then
driven by terminals and by the prior alone) and `ObjectiveShareEvaluator` (each side's projected
objective share via `TacticalAnalysis.ProjectObjectives`, normalized so the sides sum to 1 in a
two-side game - a placeholder so end-to-end tests have a gradient). B3 replaces both with the
C1-vector evaluator and the ONNX one lands at step 14 behind the same interface.

**Two-side constraint the evaluator must satisfy:** when there are exactly two sides,
`v[other] == 1 - v[self]`. This is what makes max^n reduce to minimax in 1v1 (sec 8 test 5); the
evaluator interface documents it and a test asserts it for every shipped evaluator.

### 7.3 Backup

A leaf's `SideValues` are added, unchanged, to `ValueSum` of every node on the path to the root, and
each node's `Visits` increments. No discounting, no shaping at B2 (the plan's lambda ~0.1
objective-differential term lives in B3's evaluator shape if it is wanted; keeping it out of the
backup means the tree code has no reward knobs to hack - G2).

### 7.4 Selection value (what B4's UCT reads)

At node `n` with acting side `s`, a child `c`'s exploitation term is `c.ValueSum[s] / c.Visits` -
**the acting side's own component**. Teammates share it by construction (same team number). That is
max^n: each acting player maximizes its own side's value, with no assumption about what other
sides maximize. B4's selection formula (PUCT with sec 3's priors) is written against this accessor,
so B2 must expose `QFor(side)` and `Prior` on the edge and nothing else about the formula.

## 8. Verification (the build is not done until these exist and pass)

Engine tests under `Tests/`, on authored states - `TacticianPrescriptionTests.cs` shows how to
build a mid-game store for the seam; reuse its fixtures.

1. **Candidate counts at 1k and 4k** (campaign doc's "generator-level tests"): from a real
   activation-boundary snapshot at each level, the action space's level-1 count equals the
   unactivated alive on-table pool, and every unit's level-2 count is <= budget and >= the number
   of feasible intent families the generator reports.
2. **Diversity rule survives B2:** for a state where the generator emits a `ChargeToContact`
   candidate that `Score` ranks last, the edge exists (priors order it, they never drop it).
3. **Edge vocabulary is the planner's:** for every enumerated edge, prescribing it through the seam
   on a resumed copy yields `Honored == true` when the stage offers that action, and the
   fell-through edge is closed (not credited) when it does not - one authored case each.
4. **B reduces to A:** a tree given exactly one expansion at the root (k = 1 at both levels) picks
   the unit and macro the natural Tactician picks on the same state, and prescribing that edge
   reproduces the natural activation byte-identically (5b's pin, one level up).
5. **max^n reduces to minimax in 1v1:** the same authored 1v1 tree (fixed leaf values) backed up as
   `SideValues` and as a scalar negamax produce identical visit counts and root choice. Then in an
   authored 2v2 tree: teammates' nodes read the same component, opposing nodes read their own, and
   a node whose acting side is not the root's does NOT minimize the root's value (a 3-side
   authored case pins that it maximizes its own instead).
6. **Determinism under seed:** two expansions of the same edge from the same node with the same
   derived seed produce byte-identical child snapshots; a different worker seed does not.
7. **Callback line == list line:** `Run(snapshot, driver)` and `Run(snapshot, list)` for the same
   line produce byte-identical snapshots and the same `Honored` flags.
8. **Hash-verify:** DOP-1 six-game cell still `8D6EFA0AF0B4019E` - B2 adds no code to natural
   play's path except the `Honored` bookkeeping, which is null-hook-guarded like 5c.
9. **Cost, recorded not gated:** the fully prescribed line (unit + action + macro from a real
   enumerated edge) at 2k, Release, `fdglab b0` - the number 5c could not measure. Expected at or
   below the SoloRules arm (20.5ms under load); report whatever it is.

## 9. Deferred, recorded (never silently cut)

- Shooting-target prescription (sec 3.2) - B5, through the same seam.
- Chance nodes / double progressive widening (sec 6) - built only on probe evidence.
- Branch-point-only snapshot storage (sec 5.3) - B4's soak decides.
- Transposition table - plan sec 9 says no; unchanged.
- In-sim policy choice (Tactician vs SoloRules) and continuation depth - B4, on the benchmark.
- Widening constants C / alpha - B4, on the benchmark.
