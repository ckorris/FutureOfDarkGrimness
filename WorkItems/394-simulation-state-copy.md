# 394 — Simulation state copy without the JSON round trip

**Status**: done (2026-09-05)
**Related**: #191 (B4/B5 search, step 10), R9 (cooperative simulation stop), the C replan (step 11)

## Goal
Every search expansion copies the game state by saving it to JSON and loading a fresh store from
that text. Replace that with a typed in-memory copy (or a copy-on-write store) so a child node costs
what one activation of engine play costs, not that plus a 400 KiB text round trip. Done means: the
Strategist's outcome hash through the new path equals the serializer path's on the DOP-1 six-game
check (byte-identical decisions, not "close"), the per-expansion cost is measured before and after
with `fdglab b0` phase [2]/[3], and nothing about the tree, the boundaries or the search changes.
Filed 2026-09-05 from a CPU profile of a real Strategist game (below); not scheduled inside the
B+C window - the gate and the C replan come first.

## Notes
- 2026-09-05 (16:00): **Built.** Chris pulled it into the window ("do 394 now... way more games, way
  faster"). The gate v4 run was stopped at 215 matrix games (kept on disk but NOT to be resumed: the
  gate is a wall-clock budget, and a faster state copy changes what that budget buys, so the gate is
  relaunched fresh once this lands).
  - **The churn, pinned.** An in-process `EventListener` on the runtime's JIT keyword (scratchpad
    `jitpin/`) counted dynamic-method compilations by name across 50 loads: 75 x
    `dynamicClass::InvokeStub_DataBindingJsonConverter`1..ctor` and nothing else of the kind. That is
    `Activator.CreateInstance` for the per-type converter in the store constructor (and the sibling
    `MakeGenericMethod().Invoke` for RegisterType): .NET 8 emits an IL invoke stub per reflected
    member and parks it in the type's reflection cache, which is held WEAKLY - so under the search's
    allocation rate the cache died at every gen-0 GC, and the 15 stubs were emitted again for the
    next store and finalized again (`DynamicResolver.DestroyScout`). The store-ctor-only loop showed
    0 stubs (no GC pressure), the load loop 1.5 per load. Fix: one `Func<GameDataStore,int,JsonConverter>`
    per type via `Delegate.CreateDelegate` on a closed generic static method, cached per process.
    After the fix the load loop emits no invoke stubs at all.
  - **The copy.** `SaveLoad/StoreClone.Clone(GameDataStore)`: a new store from the source's type map
    and capacities, each component store filled by `ComponentStore<T>.ReplayFrom` (occupied slots
    only, source generations adopted, free slots left at generation 0 - exactly what a save carries),
    values copied per type: value types / immutables (int, float, string, Position, Float2, PlayerID,
    PlayerSlotInfo, RectangularZone, TerrainData) as themselves; TeamData, ObjectiveData, ModelData,
    UnitData, ArmyData, GameProgressData through internal copy constructors that build what the JSON
    constructor plus `RehydrateRules` would (bindings rebound into the target through
    `BindForReplay`, which hands out the per-slot binding before the slot is filled - the same forward
    reference StoreReplay retries around; tokens and weapons copied because play mutates them;
    resolved rules and base shapes shared because they are immutable and equal by name; spells left
    empty as after a load, the resume path restores them on both paths). A registered type without a
    cloner (test types, future components) falls back to the serializer for its entries through the
    loader's own checked replay. Then the loader's post-steps in its order: rewire wound
    subscriptions, rehydrate rules (a no-op here), stamp legacy reserves.
  - **The seam.** `IStoreSnapshot { Materialize(); ToJson(); }` with `StoreSnapshot` (a frozen clone;
    Materialize = clone it again; any number of workers concurrently, the clone only reads) and
    `JsonSnapshot` (the old path, exactly). `SimulationService` runs on `IStoreSnapshot`; every string
    entry point stays and wraps a `JsonSnapshot`; `SimulationResult.State` is the typed result and
    `.Snapshot` serializes on demand (the search never asks; the equality pins and b0 do).
    `SearchNode`, `ExpansionOutcome`, `RootBoundary`, `SearchTree.FromSnapshotAsync/ProbeRootAsync`
    and `UctSearch.RunAsync` carry the interface; the Strategist captures the live store with
    `SimulationService.Capture` (a clone - the live game's store is read, never touched). The
    authored-tree tests key nodes by a `KeySnapshot` stand-in.
  - **Pins (`Tests/StoreCloneTests.cs`, 7):** Save(Clone(x)) == Save(x) on the compiled fixture and at
    every boundary of a six-activation natural line (played board: tokens, wounds, moved models, flow
    state); a clone is independent (moving/wounding it leaves the source alone, the clone unit's
    wound aggregation fires, tokens are not shared); the next Create hands out the same reference on
    both paths after a recycled slot; the serializer fallback for an unregistered type binds into the
    clone; a three-activation line and a 2-worker/3-iteration search from the same state agree byte
    for byte and choice for choice across the two paths. Existing search/simulation fixtures pass
    unchanged in meaning (three assertions now compare `.ToJson()`).

- 2026-09-05: **Profile, one Strategist-vs-Tactician 2k game, benchmark budget, `dotnet-trace`
  sampled, 133 s of CPU** (scratchpad `prof-strat.speedscope.json`; attribution by nearest engine
  frame under each CPU sample). 63% of CPU is inside the search's simulations. Of all CPU:

  | bucket | share |
  |---|---|
  | JSON serialize + deserialize (Newtonsoft reader/writer, `RuleAttachmentPersistence`, `ArmyListRuleResolution`, `StoreReplay`) | 41.4% |
  | `System.Reflection.Emit.DynamicResolver+DestroyScout.Finalize` on the finalizer thread | 27.8% |
  | rules and tokens (`TokenContainer.HasToken`/`GetAllTokens`, `RuleEvaluator.CollectFromRules`) | 13.0% |
  | AI planner (`TacticianPlanner`, `CombatMath`, `MovementPlanner`, `MacroActionGenerator`) | 6.3% |
  | stage machinery | 3.9% |
  | data store / bindings | 2.0% |
  | search + evaluator + encoder | 0.3% |

  So the state copy is about 40% of CPU outright, and the 28% of dynamic-method finalization is very
  likely part of it: something emits `DynamicMethod`s at a high rate and lets them die. The engine
  has no `.Compile()`, `DynamicMethod` or `CreateDelegate` of its own; `GameDataStore`'s constructor
  registers a converter per component type by `MakeGenericMethod` + `Activator.CreateInstance`
  once per store (i.e. once per simulation), and Newtonsoft's delegate factories emit per contract.
  No global `JsonConvert.DefaultSettings`, no custom `ContractResolver`, so the contract cache should
  be the shared `DefaultContractResolver.Instance` - which makes the churn the FIRST thing to pin
  (a trace with the JIT/MethodDiagnostic keywords, or a `DynamicMethod` allocation stack). If it is
  per-store reflection, it is a one-line cache; if it is Newtonsoft, a shared serializer instance.
  Either way it is cheaper than the clone and may be worth 25% on its own.
- 2026-09-05: the earlier b0 wall-clock split (load 20 / assemble 7 / run 53 / save 10 ms, single
  thread) under-states the copy's share because the finalizer work lands on another thread and the
  contract/JIT churn is spread across the run step. Trust the CPU profile for WHERE the cycles go,
  the b0 numbers for the wall clock of one expansion.
- 2026-09-05: a profile of `fdglab b0` itself was misleading and is worth knowing about: b0's own
  throw-stops (`SimulationStopSignal` at capture, THROW-mode advances, the [3c] natural lines) cost
  46% of that run's CPU - the stage machine nests every transition as an awaited call, so by round 3
  the chain is 300-500 frames deep and an exception is rethrown at every frame, each rethrow walking
  the whole remaining stack: quadratic in depth, seconds per throw. The engine's simulation stop has
  been cooperative since R9 and pays none of this; b0's capture and THROW phases still do. Not a
  search cost, but any engine fault deep in a game pays it too.

## Plan (when scheduled)
1. Pin the dynamic-method churn (above). Fix it if it is a cache miss. Re-profile.
2. Typed copy of `GameDataStore` + bindings: a `Clone()` that produces an independent store with the
   same IDs and the same `GameProgressData`, re-entered through the existing resume path
   (`SimulationService` builds the server from a store either way).
3. Determinism pin: DOP-1 six-game Strategist hash identical through both paths; the
   `SimulationStopTests` / `UctSearchTests` reproducibility tests unchanged.
4. Measure: b0 [2]/[3] before/after; a probe run and a 6-cell bench at benchmark budget for the
   iterations-per-budget gain (the smarts change is zero by construction; what changes is depth).

## Decisions
- Filed as its own number rather than inside #191: it is engine infrastructure (data store, save/load)
  that outlives the B/C campaign, and #191's ledger is already the longest file in the repo.

## Outcome
Done 2026-09-05 (engine `53a917e`, the same day it was filed - Chris pulled it into the B+C window).
Contract met: `Save(Clone(x)) == Save(x)` at every boundary of a played line, a search through the
clone path and the serializer path chooses identically with identical visit distributions, the
DOP-1 Tactician hash is unchanged (`4241CF7010C28571`, twice), suite 3249/0/1, smokes exit 0.

| b0, default 2k pair, boundary 20 | JSON (v6) | typed copy (v7) |
|---|---|---|
| state round trip per expansion | 76-81 ms | 1.0 ms |
| round-4 root, interactive budget (6.6 s, 4 workers) | 315-346 iterations, depth 6-7 | 752-798 iterations, depth 7 |
| serial cost per iteration | 95 ms | 43 ms |
| benchmark budget (1.5 s) | 69 iterations, 94 MiB live tree | 166 iterations, 46 MiB live tree |
| tiny probe boards (search reaches game end) | hundreds to low thousands of iterations | 30k-3.5M iterations |

The churn (plan step 1) was the store constructor's reflection invoke stubs dying with .NET's weak
type cache; fixed with cached delegates. b0's own [3] advance phase did not move because it calls
the serializer directly rather than the seam - the seam's numbers are the rows above.

Consequence worth knowing: on the small probe boards the search now reaches terminal nodes in
every line, and the `count-says-they-win` probe fails 3/3 because the in-sim Tactician does not
play the denial the probe (and P1's projection) assume - an A-policy last-round facet, recorded in
the #191 ledger (2026-09-05 16:30), not a regression here. Step 4's 6-cell bench is superseded by
the fresh gate v5 run on this engine (`FdgLab/reports/step10-gate-v5-2026-09-05/`).
