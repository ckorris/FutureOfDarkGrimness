# 083 — Await through the stage-machine transition chain

**Status**: done
**Related**: audit §4 (`Audit-6-10-2026.md`), #064 (characterization tests written before this), submodule `c4fb4f2`, superproject bump `6a8ecb1`

## Goal
The stage machine advanced via a `void`-returning `Transition` delegate fed `async` lambdas (async-void), so the await chain broke at every transition. Done = `Transition` returns `Task` and is awaited end-to-end through every transition boundary, so a fault thrown in any stage after the first transition propagates out as a faulted Task that `FDGServer`'s top-level handler can observe — instead of escaping to the synchronization context (silent or process-crash). Plus a `Task.Yield` once per round so the game doesn't run as one ever-deepening synchronous-continuation stack under piped/AI input.

## Notes
- 2026-06-14: Implemented and shipped. Core plumbing: `Transition` delegate → `Task`; `ExecuteTransition` awaits `transition.Invoke`; `SignalEvent` → `Task`; `StageBinding.Activate` → `Task`; `TransitionToSibling` → `Task`; the `AddSibling` builder lambda made `async`. The real transition boundary turned out to be `StageBinding.Activate(context)` (~75 call sites across 46 files), not `SignalEvent` directly — bulk-prefixed `await` to every standalone `Binding.Activate(...)` statement.
- 2026-06-14: Compiler surfaced the non-mechanical spots. CS4014 on six `base.Enter(context)` calls in parent stages (they were fire-and-forgetting `ParentStage.Enter` too — same bug) → awaited. CS4033 (`await` in non-async) in `OfferStrikeBackStage` (`void` helpers `MoveToStrikingBack`/`SkipStrikingBack` → `Task`), `ChooseMeleeDefenderStage` (local function `ChooseDefender` → `async Task`), and `CombatStage.Finish` (part of the callback chain below).
- 2026-06-14: `ChooseActionStage` stored its action outcomes as `Dictionary<string, Action>`; changed to `Func<Task>` and `await outcomes[choice].Invoke()`.
- 2026-06-14: Verified — engine suite 467/467 (added one test), full `dotnet build` clean (no CS4014 remain; only pre-existing nullable warnings), headless smoke runs all 4 rounds and exits 0 with "It's a tie!".

## Decisions
- **The combat sub-stage chain needed the callback threaded, not just `Activate`.** `CombatStage<TResult,…>` drives its sub-stages (range check → roll to hit → … → assign/apply wounds) through an `Action<TResult> onFinished` callback that ultimately calls `NextStage.Activate`. Leaving it as `Action` would have re-broken the chain at every combat step (Activate fire-and-forgotten inside the callback). Changed `onFinished` to `Func<TResult, Task>` and awaited it through all 11 `RunStage` implementations + `RunPostExecuteEffects`/`Finish`. This was the only non-obvious part of an otherwise mechanical change.
- **`Task.Yield` placed in `ReconcileNewRoundStage.Enter`** — the once-per-round entry point — rather than deeper in the per-activation loop. One yield per round is enough to cap stack growth without adding scheduler churn to every stage.
- **`FDGServer` needed no change.** `LaunchStateMachineOnceReady` already `await`ed `stateMachine.Enter(context)` inside a top-level try/catch that calls `OnGameEnded` on fault (written in anticipation of this work). Once the middle of the chain awaits properly, that handler genuinely catches faults from anywhere in the tree. The audit's "give FDGServer one end-to-end game Task with a top-level fault handler" was already satisfied; only the broken middle needed fixing.
- **`OnWillActivate` left synchronous** (`Action<TContext>`) — it's a pre-transition notification, fires before the await, behavior unchanged.
- Left the dead commented-out `OnHandled` block in `AssignWoundsStage` alone (out of scope; it's already `/* */`).

## Outcome
Shipped the full await-through refactor across ~65 engine files. `Transition` returns `Task` and is awaited through `ExecuteTransition`/`SignalEvent`/`Activate`/`TransitionToSibling`, the combat `onFinished` callback chain, and `base.Enter` calls; `Task.Yield` added at the round boundary. Stage faults are now observable as faulted Tasks reaching `FDGServer`'s existing top-level handler. Added a fault-propagation regression test to `ParentStageOrderingTests` (asserts an exception in an entered child's `Enter` propagates out of the awaited `Activate`) — the suite's #064 ordering tests were written as characterization guards specifically to survive this refactor, and all still pass (467 total). No app-side changes were needed. Nothing deferred.
