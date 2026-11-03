# 400 — Deadly clump confinement used the wrong model order

**Status**: done
**Related**: #028 (Deadly weapon priority — where `ConfineToClumps` came from), #023/#024/#006 (the allocation-order rules it must mirror)

## Goal

Deadly(X)'s no-carry-over confinement must agree with the order wounds are actually assigned. Today
`AssignWoundsStage.ConfineToClumps` walks `defender.Models` in raw list order while
`AssignWoundsResults` pours the resulting pool in a *different* order (already-wounded first, joined
hero last), so against an already-damaged squad a clump's discarded overkill silently spills onto a
fresh model — exactly what "these wounds don't carry over to other models if the original target is
killed" forbids. Done = one shared implementation of both the allocation order and the clump math,
the confinement walking that order, and integration tests pinning the pre-wounded cases.

## Notes

- 2026-09-12 (later): Chris pasted the OPR Discord thread of 2026-07-22 that prompted the
  investigation. It **confirms the shipped fix** and **raises the priority of the deferred facet**.
  - Adam (OPR, Army Forge) on the headline question: "excess wounds are lost"; "if all the models in
    the unit are killed then yeah there would be nothing for the last 2 hits to do." Pinned as
    `Deadly_MoreClumpsThanModels_WipesUnitAndWastesTheRest`.
  - Yorekani's hero case ("2 models left... unless one of those models is an attached hero with
    Tough(6), then you'd need 3 or more wounds with Deadly(3)") already passes unchanged - our
    hero-last ordering matches. Pinned as the two `Deadly_ToughSixHero_*` tests.
  - The thread's real technical content is the part this item deferred. Adam: "you need to resolve
    them one at a time since you can't partially damage multiple models" / "just do them one at a
    time and it'll work out fine." Yorekani: "IF a unit of multiple models with Tough(3) fails to
    block hits with Deadly(X) and the models have Regeneration or similar... you'll have to slow-roll
    the Regeneration saves. That's because **you can't accurately prevent spillover otherwise**."
    That is exactly the residual recorded in Decisions.
  - Two `[Ignore]`-marked tests now assert the rule-correct behaviour and are verified to fail
    against today's code (checked by removing the attributes and running):
    `Deadly_Regeneration_RollsOncePerMultipliedWound` - rolls **1** Regeneration die (the
    capacity-capped total) where the rule wants **3** (the clump's multiplied wounds), so a 1-wound
    Regeneration model shrugs off a whole Deadly(3) clump 1/3 of the time instead of 1/27; and
    `Deadly_Regeneration_IgnoredWoundsDoNotSpillToTheNextModel` - the second model is assigned
    **1.0** wound where it should take **0**.
  - No new number filed yet: the deferred facet is still this item's Decisions entry, and the two
    Ignore strings point at #400. When the per-clump work starts it gets its own number (it is
    genuinely separable - a clump-aware request shape across both resolver sets) and those two
    strings move with it.

- 2026-09-12: Filed. Number taken from `origin/master` after `git fetch origin --recurse-submodules`
  (index high-water mark **398**, archive max **399**, no `WorkItems/40[01]*` on any remote branch);
  Reconciliations.md read, no collision. Logged in `Reconciliations.md`.

- 2026-09-12: Reproduced against the real `AssignWoundsStage` with a throwaway harness (three Tough(3)
  models, model C already at 1 remaining wound, attacker with Deadly(3)):

  | Failed saves | Correct | Before the fix |
  |---|---|---|
  | 1 | 1 wound — the clump goes to C (mandatory, already wounded), 1 lands, 2 lost | **3** — C dies and 2 spill onto A |
  | 2 | 4 wounds — C dies (1 of 3 lands), A dies (3), B untouched | **6** — C dies, A dies, **and B takes 2** |

  Order-dependent, which is why it hid: move the wounded model to list index 0 and
  `ConfineToClumps` walks it first, computes 1, the pre-assignment consumes the pool, and the answer
  is right with no prompt at all. Same board state, different index, different answer.

  Fresh uniform squads were already correct in both models (Deadly(3) x5 saves into three fresh
  Tough(3) wipes either way; Deadly(2) x5 gives 8 either way) — which is why every existing Deadly
  test passed. `#024`'s finish-a-model-before-starting-another guard is what stops the pooled clump
  being spread one wound per model; without it the divergence would be far larger.

## Decisions

- **The fix is the ordering, not a rewrite of the wound pipeline.** Walking the confinement over the
  same order the allocator uses produces exactly the right totals for both rows above, because the
  pre-assignment then consumes the clump's share before any fresh model can be reached. No new
  request shape, no resolver change.

- **`CombatMath` already had the order.** Its private `AllocationOrder(UnitData)` was written as a
  deliberate mirror of `AssignWoundsResults`' mandatory ordering, and its private `ConfineToClumps`
  was a hand-copy of the stage's. Both moved to `WoundAllocation` (namespace `FDG`, next to
  `AssignWoundsResults`, which is the thing they mirror) so the engine, the AI valuation and any
  future reader share one definition. That is what makes the ordering fix a one-line change instead
  of two copies to keep in sync.

- **Heroes were never a second divergence.** Filing suspected the joined hero (ordered last by #006)
  as another source, but `UnitData.AttachHero` *appends* the hero's model bindings, so the hero is
  already last in the raw `Models` list — the two orders agreed on it. The pull-forward of
  already-wounded models is the only place they differed. `WoundAllocation.Order` keeps the hero
  clause anyway: it is the contract, not an accident of how `AttachHero` happens to build the list.

- **DEFERRED (recorded, not silently dropped): clump-aware assignment.** The pipeline still carries a
  scalar `totalWoundsDealt` and re-allocates it, so the confinement is only correct while the order it
  walks and the order the allocator drains along cannot diverge. Two residuals:
  1. **Regeneration inside a clump** — reachable in ordinary play (any Regeneration defender hit by a
     Deadly weapon). Ignored wounds shrink the pool, which is then re-allocated; ignoring a wound
     *inside* a clump should not free that clump's remaining wounds to travel to another model.
  2. **Unequal fresh capacities** — the confinement assumes the defender's free choice among *fresh*
     models cannot change the total. True for every unit shape the game can currently build (uniform
     rank and file, hero forced last), so this is a latent assumption rather than a live bug — but it
     is an assumption, not something the code enforces.
  Both need Deadly to emit a per-model assignment (N clumps of X, defender picks each recipient,
  excess discarded at the model boundary) instead of a scalar — a clump-aware request shape across
  both resolver sets. Not attempted here; worth its own number if (1) is to be fixed.

- **The defender's sub-choice among several already-wounded models stays deferred**, unchanged from
  `AssignWoundsResults.PreAssignToAlreadyWoundedModels`' existing note: they fill in unit-list order.

## Outcome

Shipped 2026-09-12. `WoundAllocation` (namespace `FDG`, beside `AssignWoundsResults`) now owns both
the allocation order and the clump confinement; `AssignWoundsStage` and the Tactician's `CombatMath`
each dropped a private hand-copy and call it, so `CombatMath`'s estimate and the engine's resolution
cannot drift apart on this again. The confinement walks that order instead of the raw model list,
which is the whole fix.

Tests: `DeadlyAttacker_ClumpOnAlreadyWoundedModel_OverkillDoesNotSpill` and
`DeadlyAttacker_TwoClumps_SecondDoesNotReachTheThirdModel`, each parameterized over the wounded
model's index (0 and 2) because the old answer depended on it — both `(2)` cases go red against the
pre-fix ordering and both `(0)` cases stay green, which is the order-dependence pinned directly. Plus
`DeadlyAttacker_FreshSquad_TwoClumpsKillTwoModels` as the undamaged control. Engine suite 3323/0,
full `dotnet build` clean, headless smoke exits 0.

Deferred, see Decisions: clump-aware per-model assignment (Regeneration inside a clump is the one
residual reachable in play).
