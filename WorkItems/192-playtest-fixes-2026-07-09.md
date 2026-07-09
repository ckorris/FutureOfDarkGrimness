# 192 — 2026-07-09 playtest fixes

**Status**: done (awaiting GUI hand-verification)
**Related**: #191 (cover, split out and deferred), #035/#096 (transport), #056 (beats), #161 (resolver consistency)

## Goal

Fix the five actionable bugs from the 2026-07-08 playtest. The sixth (cover) was split out as #191
because it needs a rules ruling rather than an engineering call.

## Notes

- 2026-07-09: All five landed. Engine test suite 1329 -> 1343 green; `dotnet build` clean; headless
  smoke exits 0 after each slice.

  1. **Escape no longer quits.** Escape was raylib's exit key, so pressing it to cancel an
     in-progress action tore the window down. First implementation added a confirmation modal
     (`d2b6a3b`); the user then asked for the hotkey to be removed outright, so that commit was
     reverted and replaced by `SetExitKey(KeyboardKey.Null)` (`cfa29be`). Escape now only reaches the
     resolvers that already bind it to cancel. Quit via the main menu or the window close button.

  2. **Fast / Very Fast** (`b9904f0`). Both declared a single `Movement_OnMoveActionDeclared` hook
     gated on `Advance`, so Rush and Charge got nothing. Now +2/+4/+4 and +4/+8/+8, mirroring Slow's
     -2/-4/-4. Two tests had *pinned the bug in place* (`MovementRuleIntegrationTests` asserted Rush
     and Charge were untouched; `PerModelMovementBudgetTests` asserted the same per model); both
     rewritten, and Very Fast gained coverage it never had.

  3. **Ranged morale** (`c4a2d70`). `ResolveRangedMoraleStage` sat inside the per-weapon loop, tested
     only the current weapon's defender, and re-baselined that defender's wounds on every weapon. It
     now runs once after the weapon loop drains (on BOTH shoot exits — `OnNoValidShots` fires only
     after a weapon has already shot, so it must not bypass morale), once per unit that was shot at,
     against the wounds each had when first targeted. `CombatActionContext.AttackedDefenders` carries
     those snapshots. Morale/Fearless beats now name the unit under test — melee's beats gain the
     name too, via the shared `MoraleUtilities.TakeMoraleTest`.
     `DetermineMorePendingShotsStage.ToMorale` renamed `OnVolleyComplete`; it no longer leads to morale.

  4. **Back out of any action.** Shoot, Cast and Embark already returned cleanly.
     - *Charge* (`a2eb958`): two bugs.
       `ChooseMeleeDefenderStage.BackToChooseAction` was bound to the same MeleeStage sibling as
       "melee finished", whose `OnWillActivate` calls `RegisterAttackedFinished()` — and
       `TransitionToSibling` re-fires `OnWillActivate`, so declining a charge set `HasAttacked` and
       `ChooseActionStage` then disabled Move, Charge and Shoot. (The old line carried the comment
       `//Should go back to choosing.`) It now has its own sibling. Second, with exactly one valid
       defender the stage auto-attacked, skipping the only prompt that has a Back button; it now always
       poses the pick.
     - *Move / Disembark* (`3821847`). `DefineMovementPathRequest` and `PlaceObjectsRequest` now reply
       with `CancellableResult` and carry an `AllowCancel` flag (mirroring `SelectionRequest`). Only the
       two player-chosen actions set it. Mandatory placements — deployment, Scout, Ambush arrival,
       transport spillout — go through `PlacementRequesting.RequestMandatoryPlacement`, which rejects a
       Cancelled reply; the rule-triggered move in `GameOperationServices` does the same.
       `MovementStage.ReconcileChildContextBeforeLeaving` runs on every sibling exit and threw when no
       path was submitted, hence the `MoveCancelled` guard.

  5. **Ambush** (`16a4ab3`). "In reserve" was inferred from "every model sits at (0,0)", re-derived
     independently in `GetIsOnBattlefield`, two private `IsUnplaced` copies, the renderer, the AI place
     resolver, the LoS blocker builder, the hit tester and the measurement overlay. Reserve now lives on
     the unit as `TokenType.InReserve` (see `ReserveRules`). `DrawModels` skips models whose unit isn't
     on the battlefield — which also stops embarked squads and flown-off Aircraft being drawn in the
     corner.

## Decisions

- **Fast = +2/+4/+4** (user ruling, 2026-07-09), chosen to mirror Slow's -2/-4/-4 rather than the
  narrower "+4 Rush only" the report literally described. Very Fast doubles it.
- **Morale keeps the half-strength crossing trigger.** The report said "once for each unit that
  defended"; a unit that took wounds without crossing into half strength still doesn't test. What
  changed is *when* the test happens and *what baseline* it measures against, not the trigger.
- **The single-defender charge now costs an extra click.** Auto-selecting the sole defender was the
  only reason Charge had no Back at all. Accepted deliberately: nothing is mutated until the impact
  hits and pile-in that follow the prompt.
- **`GetIsOnBattlefield` keeps the origin check as a backstop**, below the explicit token checks. A
  unit mid-deployment has never been placed and carries no token; a model's base can't fit wholly
  inside the table with its centre exactly at (0,0), so the coordinate remains a safe "never placed"
  marker. It is simply no longer what *defines* reserve.
- **No save-version bump.** `GameSaveSerializer.StampLegacyReserves` re-derives `InReserve` on load
  from the old positional rule. Deliberately save-path only: the network full-state sync shares
  `StoreReplay` but must mirror the host, which already sends the token. Placement clears the token
  everywhere, so a mid-deployment save that gets stamped self-heals when the unit deploys.
- **"Active turn 1" was never reproduced.** The renderer half is fully explained and fixed. The
  activation half required a reserve model holding a *non-origin* position, and no code path was found
  that writes one. The refactor makes the unit's reserve state authoritative, so a stray coordinate can
  no longer cause it either way — but the trigger remains unidentified. If it recurs, capture the save.

## Outcome

All five shipped across five engine commits + superproject bumps. Cover became #191, unfixed by
request. Remaining: hand-verification in the running app (below).

**Verify by hand:**
- Escape during a move / shoot / deployment placement cancels that step and never closes the window.
- A Fast unit (bikes) rushes 4" further and charges 4" further than a plain one.
- Shoot one unit with two weapons: exactly one morale test, after both weapons, titled with the
  defender's name. Shoot two different units: one test each.
- Click Charge by mistake with one enemy in range -> the defender prompt appears with Back -> Back
  returns to the action menu with Move, Charge and Shoot still available.
- Move -> Back leaves the unit unmoved and still able to act. Same for Disembark.
- Hold a unit in Ambush: it is not drawn in the bottom-left corner, is greyed out with
  "Reserve - arrives round 2.", and arrives normally on round 2.
