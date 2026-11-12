# 269 — Teleport / reposition placement rejects most of its own reach ring

**Status:** implemented 2026-07-23, awaiting GUI hand-verify
**Related:** #214 (the reach ring this is measured against), #197 (Teleport), #159 (lenient movement coherency)

## Report

Teleport placement is off. Tried before and after moving, in both group and individual movement modes: the
area where each individual model can actually be placed is far smaller than the green circle drawn around
it - the legal region doesn't come close to filling the ring. Screenshot showed a tight orange blob with
~6" rings extending well past it in every direction, none of that space usable.

## Root cause (2026-07-23)

Two independent over-restrictions in `GuiPlaceObjectsResolver.IsPlacementValid`, both of which the #214
ring exposed by finally drawing the bound the code claimed to enforce.

1. **The unit blocks itself.** `GetTableOccupants()` scans `_tableState.Models.Objects` for overlap. A
   reposition starts with every model of the unit still standing at its old spot - `PlacedObjectEntry`
   defers the writes until the whole placement is accepted - so the unit's own not-yet-moved models were
   fencing it out of exactly the ground it was about to vacate. This is why the blob's interior was dead.
   Invisible on a deployment, where the models to place sit at the unplaced (0,0) sentinel and are skipped.

2. **Incremental cohesion in list order.** `IsInCohesion` demanded each model land within **1"**
   base-to-base of an *already-placed* model. Vacuously true for model 1 (nothing placed yet), so model 1
   roamed its whole ring - and then models 2..N were confined to a 1" band around wherever model 1 landed,
   however much of their own rings were free. The constraint is also order-dependent: a legal final
   formation is unreachable if it can't be built in the request's model order.

Neither is the actual rule. Cohesion is a property of the finished formation, not of the order you build it
in, and a model about to teleport is not an obstacle.

## Fix

- **Own-model exclusion** — both the GUI and CLI resolvers keep a `_selfModels` reference set built from
  `request.ModelsToPlace` and skip those models in `GetTableOccupants()`. Each is still accounted for
  exactly once, at its NEW position, through the placed-so-far overlap loop. No-op for deployment.
- **Cohesion moves to the Done gate** (owner's call, over "count unmoved teammates too" and "drop cohesion
  entirely"). New `FdgRaylib/Placement/PlacementCohesion.cs` evaluates the whole finished formation and the
  GUI reports it live above the buttons + disables Done while it's broken. The per-click check is kept for
  **deployment**, where the unit is built from nothing and the running feedback is the only cohesion signal
  a player gets - only `MaxDistanceFromStartInches > 0` placements switch to the Done gate.
- **The test is "not worsened"**, mirroring `MovementUtilities.ValidateCoherencyNotWorsened` (#159): each
  model must end within 1" of its nearest team-mate and 9" of its farthest, *or* be no worse on each count
  than it started. Without the lenient half a unit that casualties had already scattered could accept no
  placement at all - not even standing still - and the prompt would trap.
- **CLI** — same exclusion; its inline cohesion check now applies only to deployment, and a reposition whose
  finished formation breaks cohesion re-prompts the unit from the top. Terminating: the EOF branch stands
  every model still, which cannot worsen cohesion, so piped/automated runs always leave on the first pass.

**No engine change needed.** `AiPlaceObjectsResolver` short-circuits every `MaxDistanceFromStartInches > 0`
request with `StayPut`, so the bot never reached either restriction.

## Notes

- 2026-07-23 — implemented. App-side only. `PlacementCohesionTests`, 9 tests (packed/stranded/chained,
  the three already-scattered leniency cases, all-pairs scatter, lone model, mismatched lists). App
  557/557, engine 2023/2023, build clean, headless smoke exit 0. **Needs a GUI hand-verify:** teleport a
  multi-model unit and confirm (a) the legal area now fills each ring, including the ground the unit
  currently occupies, (b) placing model 2 far from model 1 inside its own ring is accepted, (c) Done greys
  out with an amber "N models out of cohesion" line when the final formation is broken, and (d) normal
  deployment still rejects an out-of-cohesion click immediately, as before.
- **Deferred, deliberately:** deployment keeps the order-dependent incremental cohesion check. It has the
  same theoretical flaw, but it is long-standing behaviour on a much more heavily used path and the running
  red/green feedback is load-bearing there. Not folded into this slice.
