# 309 — Networked client: late-deployed models render label-only (no base) until they move

**Status:** implemented + tested (2026-08-02); awaiting networked GUI hand-verify
**Reported:** first real internet playthrough — the remote client sometimes saw only a unit's
overhead label (name/health/chips), no circle/rectangle bases, until the unit next moved; worst
with late deployments (Ambush). The host never sees it. Reproduced by Chris 2026-08-02 (host +
client on one machine, host ambusher arrives -> client shows label-only ghost; moving it fixes it).

## Root cause (two halves)

1. **App (renderer stale capture):** `RaylibRenderer.OnModelPlaced` registers a model for base
   drawing in `_placedModels`, capturing the owning `UnitData` **instance** at registration time.
   `DrawModels` gates every frame on that captured instance's `GetIsOnBattlefield()`. On a client,
   every replicated `UnitData` update (`GameDataStore.SetValueWithJson`) deserializes a **new
   instance** into the store slot (`ComponentStore.SetValue`), so the captured one keeps its old
   token state forever. (Host mutates in place — `TokenChangeBroadcaster` re-Sets the same
   instance — which is why the host never sees it.)

2. **Engine (broadcast ordering):** every late-placement flow applied model positions **first**
   and cleared the off-table token **after**:
   - `StartOfRoundExtraActionStage.PlaceFromReserve` (Ambush/aircraft): `SetPosition` loop, then
     `ClearReserve`; aircraft's `OffTableFromForcedMove` removed even later, at the call site.
   - `PlaceReinforcements`: same shape.
   - `DisembarkStage`: positions, then `TransportUtilities.Disembark`.
   - `SpilloutExecutor`: positions, then un-embark inside `ApplySpilloutEffects`.
   - `DeployUnitStage` / `PlaceDeferredUnitsStage`: same shape (benign today — the token is never
     present outside legacy saves — reordered for uniformity).

   Client-side, each position update fires the shared `DataBinding<Position>` event; the renderer
   re-registers and captures the unit instance **that still carries InReserve/EmbarkedIn** (the
   token-clear message hasn't arrived yet). Every later frame reads that stale instance ->
   `GetIsOnBattlefield()` false -> base skipped. The label overlay (`TableTooltipOverlay`) reads
   live state each frame, hence label-only ghosts. Any later position change re-runs the lookup
   against live state — which is why moving "fixes" it.

## Fix

- **Engine:** clear the off-table state (reserve / embarked / off-table-forced-move tokens)
  **after the placement decision returns but before the first `SetPosition`**, at all six sites.
  Between the clear and the first position the unit reads "not in reserve, all models at origin"
  -> still `GetIsOnBattlefield() == false`, so no observer window regression. Spillout keeps
  `ApplySpilloutEffects` intact (its internal `Disembark` becomes an idempotent no-op).
- **App (defense in depth):** `OnModelPlaced` matches the owning unit by `ModelID` instead of
  instance reference, and `DrawModels` resolves the owning unit from live table state each frame
  (per-frame `ModelID -> IUnit` map, cached tuple as fallback), so a stale capture can never gate
  drawing again.
- **Tests:** ordering pins asserting that at the moment the first position update fires, the
  unit's replicated state already reads on-battlefield: ambush arrival, disembark, spillout.

## Notes

- 2026-08-02: BOTH halves landed. Engine `3c2ac8d` (all six reorder sites + 3 ordering pins:
  `RoundTwo_Accept_ReserveClearsBeforeFirstPositionReplicates`,
  `DisembarkStage_UnembarksBeforeFirstPositionReplicates`,
  `Spillout_UnembarksBeforeFirstPositionReplicates`; suite 2553 green). Superproject `91451c2`
  (renderer live-resolve + ModelID matching + engine bump; full build green, headless smoke
  exit 0). NOT pushed - submodule master fast-forwarded locally to `3c2ac8d`; push both when
  ready (submodule first).
- Hand-verify: rerun the repro (host + client, host ambusher arrives round 2+) - client must
  show bases immediately at arrival. Ideally also disembark and a transport destruction
  (spillout) viewed from the client.
- 2026-08-02: filed; root cause confirmed by repro.
