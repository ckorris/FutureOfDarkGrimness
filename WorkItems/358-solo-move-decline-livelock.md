# 358 — Solo bot livelocks when its own move resolver declines the main activation move

**Status**: done (2026-08-05, same-day fix)
**Related**: #208 (the decline channel), #159 (lenient hold-exact coherency), #333 (0" moves
return to the menu instead of burning the activation), #216 (the Tactician's immunity)

## The mechanism

`DefinePathStage` builds its request with `allowCancel: true` - for a HUMAN that is the Back
button (nothing is committed until ExecuteMoveStage, so backing out returns to the action menu
for free). `AiDefineMovementResolver` replies `Cancelled` when even its hold-exact ladder
bottom fails strict validation (#208: a unit intermingled/wedged so no legal path exists). The
stage then returns to Choose Action - where the DETERMINISTIC solo policy (Charge > Move >
Shoot > Pass) sees the same options, picks Move again, and the same decline follows. Forever:

```
Entered Choose Action.
<unit> did not move - returning to Choose Action.   x ~750,000
```

#208's comment says the decline "keeps the unit's options open" - written for the OPTIONAL
post-combat move (`GameOperationServices`, where declining is final and correct). Nobody
noticed the MAIN activation move is also cancellable and its decline reopens a menu that a
deterministic bot answers identically. The Tactician is structurally immune (its re-entry
returns Shoot/Pass off the cached plan), but its solo FALLBACK pair inside
`TacticianResolverRegistryFactory` carries the same loop when the planner has no claim.

Live repro: FdgLab pool seed 1010, HDF-vs-Hives (Tactician-vs-solo, #359 build): the solo
Hive Guardians wedge, and the game burns 600s+ / ~1.5M decisions until the watchdog kills it.
Control (pre-#359 trajectories) happens not to reach a wedge on these seeds - the landmine
predates #359; #359's different movement just walks onto it (2 of 3200 games).

## Fix (this item)

Layer-correct: the HUMAN affordance stays; the SOLO bot stops re-picking movement after its
own resolver declined.

- `DefineMovementPathRequest.MainActivationMove` (new, default false): true only from
  `DefinePathStage`. The triggered-move flow keeps false, so declining an optional
  post-combat move stays a final, latch-free "no thanks".
- `SoloMoveDeclineLatch` (new, one per solo resolver set, shared): armed when
  `AiDefineMovementResolver` declines a MainActivationMove request; consumed by the next
  `AiStringSelectionResolver.ChooseAction`, which skips the Charge and Move branches for
  that one pick (Shoot-if-in-range, else Pass) - ending the activation instead of looping.
  Wired in both `AiResolverRegistryFactory.BuildSoloRules` and the Tactician's embedded
  fallback pair.

## Notes

- 2026-08-05: filed + fixed same-session. Pins in `SoloMoveDeclineLatchTests`; the live
  integration proof is the seed-1010 rerun completing (was: watchdog at 600s). Charge analog
  (a reachable-but-unbuildable charge declining into the same menu) believed unreachable
  today (#312 gates the offer on true reach; the ladder shortens rather than declines when
  contact fails) - not separately fixed, recorded here.

## Decisions

- Skip BOTH Charge and Move on the latched pick: the menu's movement family shares the
  decline channel; skipping only Move would leave a charge-shaped variant of the loop open.
- Consume-on-read (one pick), not per-unit state: the engine runs a player's stages
  sequentially, so the arming decline and the very next Choose Action belong to the same
  activation by construction - no unit identity needed, nothing to leak across activations.

## Outcome

Fixed at the solo layer with the engine's human affordance untouched:
`DefineMovementPathRequest.MainActivationMove` marks the menu-reopening flow, and a shared
`SoloMoveDeclineLatch` makes the next action pick skip the movement family exactly once (wired
in `BuildSoloRules` and the Tactician's fallback pair). Two pins (`SoloMoveDeclineLatchTests`);
both fault seeds (1010 HDF-vs-Hives, 1006 RL-vs-Hives swapped) complete on the fixed build; the
3200-game pool bench runs 0 faults with max game 29s (the faulted runs hit 600s+/~1.5M
decisions); solo D1 bit-identical on both matrices (no D1 game reaches a wedge, so the frozen
baseline stands). Recorded, not built: the charge-shaped analog (believed unreachable - #312
gates the offer on true reach).
