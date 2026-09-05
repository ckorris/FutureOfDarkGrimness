# 394 — Simulation state copy without the JSON round trip

**Status**: todo
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
