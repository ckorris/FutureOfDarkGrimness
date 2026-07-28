# 299 — Batched wounds must animate: dangerous terrain, spillout, self-destruct

**Status**: in-progress (implemented + tested; awaiting GUI hand-verify)
**Related**: #096 (spillout beats), #169 (spillout from the destruction choke point), #232 (casualty
cascade / beat overlap), #035 slice E (transport spillout), #153 (counts-as dangerous terrain).

## Goal

Every model that dies has to play a death animation. Three paths dealt wounds in a BATCH and then
presented (or failed to present) separately, which broke that:

1. **Dangerous terrain** — no casualty beat was ever emitted. Models killed crossing dangerous terrain
   simply vanished.
2. **Transport spillout** — the beats existed but landed too late: the player saw models missing at
   placement, then the dice row, then each casualty *reappear* to play its death animation.
3. **Self-destruct** (`ResolveMeleeReflectStage`) — same class of bug as (1), no beat at all.

Done means: no model is ever dead in authoritative state without its death beat already enqueued; the
dice row is read before its casualties drop; nothing animates twice.

## Root cause

`PresentationPlayer.GetModelDrawState` hides any model that is dead in authoritative state and has no
death override registered — it cannot know an animation is still coming. The override is registered when
the beat is **enqueued** (`OnBeat`), not when it plays. So the only safe shape is the one
`ApplyWoundsStage` has always used: deal one model's wound and present its beat in the same instant,
with nothing awaited in between.

The batched tests violated this by construction. They must roll all dice at once (a single N-die roll is
what lets the probabilistic roller yield the expected number of 1s), and they were *applying* the whole
batch at roll time — seconds of announce + dice beat before the first death beat enqueued.

## Decisions

- **Split roll from apply**, rather than trying to teach the front-end that a death is pending. Rolling
  stays exactly where it was (so the dice draw keeps its place in the seeded stream); the wounds come
  back pending and are landed by the presentation step, one at a time.
- **Dangerous-terrain casualties fall at their destination** (decided with the user, 2026-07-28). The
  roll still happens in `ApplyNonMovementTerrainEffectsStage`; the wounds are stashed on the movement
  context and landed by `ExecuteMoveStage` *after* the `UnitMovedBeat`. The alternative — resolving
  everything in the roll stage — was zero-rules-risk but had the model die on the start line and the rest
  of the unit walk off without it.
  - **Accepted rules delta**: a dangerous-terrain casualty now lands *after* Strafing / Crossing Attack
    instead of before, so such a model still contributes those mid-move attacks. Confined to a rare
    corner — a non-flying unit with Strafing/Crossing Attack that also crosses dangerous terrain (flyers
    ignore dangerous terrain outright, and Strafing grants only fly-over, not terrain immunity).
- **Probabilistic mode now animates too.** Both batched paths previously recorded no casualties at all
  under the probabilistic roller, so a fractional wound that took a model's last wound killed it with no
  beat. Casualties are now recorded in both modes, matching `ApplyWoundsStage`, which has always emitted
  a flinch for any wound > 0 regardless of mode.
- **Double-animation guard** (raised by the user): `CasualtyPresentation.ApplyAndPresent` skips any model
  that is already dead — no second wound, no second beat. Confirmed there is no other subscriber to model
  death: the renderer's dust puff (`RaylibRenderer.cs:968`) reads the same `_deaths` map the beat
  populates, so it is one animation per beat by construction.

## Notes

- 2026-07-28: Implemented. Engine 2243/2243 green, app 664/664 green, full `dotnet build` clean, headless
  smoke exits 0 (and exercised the new seam live — "Heavy Gunners: 2 model(s) tested dangerous terrain -
  1 wound(s) dealt").
  - New `CasualtyPresentation` (+ `PendingModelWound`) in `StateMachine/`: the one way a batched test
    turns pending wounds into dead models on screen. Applies each wound and presents its beat in the same
    instant, with #232 overlap on all but the last so a multi-kill batch cascades. `ApplyOnly` for
    state-only callers.
  - `MovementExecutor.ApplyDangerousTerrainEffects` -> `RollDangerousTerrain` (rolls, applies nothing);
    `PresentDangerousTerrainRolls` -> `ResolveDangerousTerrain` (dice beat, then lands the batch).
    `DangerousTerrainResult` carries `PendingWounds` + the unit identity the beats need.
  - `IMovementActionContext.PendingDangerousTerrain` / `RegisterDangerousTerrainRoll` carry the roll from
    `ApplyNonMovementTerrainEffectsStage` to `ExecuteMoveStage`.
  - `GameOperationServices` (triggered moves: Vanguard, forced moves) resolves through the same call.
  - `TransportUtilities.SpilloutCasualty` -> `SpilloutWound` (the model + what it owes, not "it died");
    `ApplySpilloutEffects` still un-embarks and Shakens immediately but leaves the dangerous-terrain
    wounds pending. `SpilloutExecutor.PresentSpilloutRolls` lands them after the dice beat.
  - `ResolveMeleeReflectStage.SelfDestructBearer` collects its lethal wounds and routes them through
    `CasualtyPresentation` instead of dealing them inline with no beat.
  - Tests: 6 new in `DangerousTerrainWoundTests` (death beat emitted; dice precedes death; survivor
    flinches instead of dying; safe roll animates nothing; roll stage leaves wounds pending and resolving
    lands them exactly once) and 1 new in `TransportSpilloutTests`
    (`Spillout_ModelsStayOnTableUntilTheirOwnDeathBeat`) which snapshots how many models are dead at the
    instant each beat is emitted — 0 at the banners and the dice row, then the Nth death beat seeing
    exactly N dead. `DangerousTerrainMoraleTests` + two `TransportUtilitiesTests` updated to drive both
    halves of the new seam.

## Verify (GUI hand-check)

1. **Dangerous terrain.** Move a multi-model unit across a Mine field / Barbed wire so at least one model
   rolls a 1. Expected: the whole unit glides to its destination intact, THEN the "Dangerous Terrain"
   dice row appears, THEN the casualty flashes red and fades **at its destination**. No model disappears
   before the roll, and none reappears to die.
2. **Transport spillout.** Blow up a loaded transport. Expected: every occupant model appears and is
   placed (all of them — none missing), the wreck/Shaken banners play over a full squad, the dangerous
   dice row is read, and only then do casualties drop, each animating exactly once.
3. **Self-destruct.** Win/lose a melee against a unit with Self-Destruct. Expected: each self-destructing
   model plays a death animation rather than blinking out.

## Deferred (explicitly, not dropped)

- **A unit wiped out by dangerous terrain never reaches `UnitDestructionNotifier`.** Pre-existing, not
  introduced here (the wounds were unnotified before too), so no token cleanup and — if the unit is a
  Transport — no spillout of its cargo, i.e. the #169 ghost state via a terrain death. Left alone because
  fixing it is a rules change, not a presentation one. Same gap applies to spillout deaths killing an
  occupant unit outright.

## Outcome

_(pending hand-verify in the GUI)_
