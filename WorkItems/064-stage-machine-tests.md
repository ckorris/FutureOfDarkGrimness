# 064 — Stage-machine tests (audit §4/§12)

**Status:** Done — engine tests added and green (suite 424→440); submodule `a1fead9`.

## Goal

Add direct test coverage for stage-machine pieces the audit flagged as implemented-but-untested:
- `VictoryCalculationStage` (winner/tie tally)
- `StartOfRoundExtraActionStage` reserve arrival
- `DetermineMeleeWinnerStage`
- `ParentStage` enter/exit/reconcile ordering — characterization tests pinned **before** #083 refactors the async-void transition chain.

## Notes

### 2026-06-13
- Suite went 424 → 440 (16 new tests), all green. New/changed files (all in the `FutureOfDarkGrimness` submodule `Tests/`):
  - `VictoryCalculationStageTests.cs` (4) — pins the exact `NotifyGameEnded` string for: no objectives → tie, objectives-but-none-owned → tie, single top scorer → `"Player {id} wins!"`, two-way top-score tie → tie. Capture via a `RecordingGameEndContext : TestGameContext` override.
  - `DetermineMeleeWinnerStageTests.cs` (4) — attacker-wins / defender-wins (both fire `OnNeedsRollToDecide`) / equal-wounds tie / zero-wounds tie (both fire `OnDoesntNeedRollToDecide`), asserting both the fired binding and the recorded `DetermineMeleeWinnerResults`. Builds the `CombatActionContext` after `SetMaxWounds` so the start-of-melee snapshots are full, then deals wounds.
  - `ParentStageOrderingTests.cs` (6) — characterization of `ParentStage` via a `TestParentStage` + recording child stages + a `RecordingLayer` sharing one ordered trace list: starting-child entry, child→child exit-before-enter ordering, sibling transition (reconcile-then-bubble), unknown-event `KeyNotFoundException`, `Exit` clears `CurrentChild`, and the `GetResumeEntry` save/load hook. Uses synchronous-completing children so the order is deterministic and the guards survive #083's refactor.
  - `AmbushRuleIntegrationTests.cs` (+2) — added the two genuinely-new cases (token stamp on arrival; non-reserve unit never offered). See scope note below.
  - `Tests/Doubles/TestGameContext.cs` — `NotifyGameEnded` made `virtual` so the Victory recording subclass can capture the result; base stays a no-op.

### Scope notes (no silent cuts)
- **`StartOfRoundExtraActionStage` reserve arrival was already well-covered** by the existing `AmbushRuleIntegrationTests` (round-1 gate, round-2 accept with the 9" min-enemy-distance, decline). Rather than duplicate, I added only the two uncovered cases to that fixture: (a) the `ArrivedFromReserve` token is stamped on arrival — the seize-exclusion marker `ObjectiveOwnershipTests` relies on being set by the real stage; (b) a unit without a later-round defer rule is never offered, even at round 2.

## Decisions

- `ParentStage` characterization uses synchronous-completing child `Enter`s. The current code fires transitions through an async-void chain (`SignalEvent` discards the `ExecuteTransition` task); with sync children everything completes inline, so post-`Activate` assertions are deterministic. This deliberately pins observable *ordering* (which #083 must preserve) rather than the broken async propagation (which #083 fixes), so the tests act as regression guards across the refactor.

## Outcome

16 stage-machine tests added across 3 new files + the existing Ambush fixture; engine suite green at 440/440. Pure test work — the only non-test change is making `TestGameContext.NotifyGameEnded` virtual. Remaining 063/065/066/067 test-coverage items are unaffected and still open.
