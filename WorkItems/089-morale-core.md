# 089 — Morale core mechanics (failed-test outcome: Shaken / Rout)

**Status**: in-progress
**Related**: underpins #006 (hero takes morale for unit), #008 (Shaken activation behavior), #009 (end-of-activation / ranged half-strength trigger), #020 (fatigue), #021 (morale modifiers + Fear/Fearless). Built on #042 token + hook architecture.

## Goal
Make a failed morale test *do something*. When a unit fails a morale test it becomes **Shaken**, or is **Routed** (removed from play) if it is at half strength or less. This item delivers the core outcome machinery and the shared half-strength predicate that every other morale facet depends on. "Done" = the melee `OnMoraleFailed` path applies Shaken (or Rout) to the correct unit, gated on a correct half-strength check, with tests. Explicitly **out of scope** for this item (each is its own facet): Shaken activation behavior/clearing (#008), the wound-driven half-strength *trigger* from shooting and end-of-activation (#009), fatigue (#020), morale roll modifiers / Fear / Fearless / hook firing (#021), hero-on-behalf (#006), and presentation beats for a routed unit.

## Notes
- 2026-06-14: Item opened. Reconnaissance of existing wiring complete (token API, melee stage graph, dice roller, integration-test shape). Branch `089-morale-core` in both repos off synced master.
  - Existing melee graph already routes `rollForMorale.OnMoraleFailed → assignMeleeMoralePenalty → applyFatigueStage`. The `AssignMeleeMoralePenaltyStage` is a stub — this is the home for the Shaken/Rout outcome.
  - Slice 1 (this commit): `IUnit.IsAtHalfStrength()` predicate; `DetermineMoraleSaveNeededResult` carries the losing unit; `AssignMeleeMoralePenaltyStage` applies Shaken (or Rout at half strength).

## Decisions
- **Rout = kill all living models, not a new removal primitive.** No whole-unit removal exists in the engine; a destroyed unit is already represented as "all models dead" (`GetIsAlive()` filters such units out of activation, turn order, and objectives everywhere). Routing by dealing lethal wounds to every living model reuses that invariant instead of adding a parallel removal path. Cost: it does not emit per-model death presentation beats (those come from `ApplyWoundsStage`, not `DealWounds`) — deferred polish, noted as out of scope.
- **Shaken token uses `TokenClearTrigger.ManualOnly`, not `ActivationEnd`.** `ActivationEnd` clears at the *bearer's own* end-of-activation. But a unit can become Shaken during its **own** activation (charge, then lose the melee), and OPR requires it to stay Shaken through its *next* activation and idle there. Auto-clearing at the current activation's end would be wrong. Clearing is therefore tied to the idle-activation behavior and owned by #008.
- **`AssignMeleeMoralePenaltyStage` is repurposed.** Its stub comment ("finish once we can fatigue a unit") conflated failed-morale with fatigue. In current GDF rules a failed morale test → Shaken/Rout; fatigue is a separate, automatic mechanic (#020). This stage only runs on the `OnMoraleFailed` branch, so it is the correct home for the Shaken/Rout outcome.
- **Half-strength is shape-dependent.** Multi-model units measure by living model count (`living*2 <= startingModels`, where starting = `Models.Count` since dead models are retained); single-model units measure by wounds (`RemainingWounds*2 <= MaxWounds`, MaxWounds being Tough-aware). A plain wound-sum proxy would be wrong for multi-model units whose models have Tough > 1, so the predicate branches on `Models.Count == 1`.
- **Hook firing deferred.** `Morale_OnPreMoraleTest` / `OnMoraleTestComplete` / `OnShakenApplied` integration (the surface Courage/Fearless ride) lands with #021; `RollForMoraleStage` already rolls without firing them, so this stays consistent.

## Outcome
_(open)_
