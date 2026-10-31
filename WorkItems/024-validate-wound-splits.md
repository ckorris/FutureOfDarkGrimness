# 024 — Validate wound splits in AssignWoundsResults

**Status**: done (engine-tested; no reachable UI behavior change to hand-verify)
**Related**: #023 (Tough wound-priority — the ordering rule this guards), #028 (Deadly: wounds don't carry across models), #031 (Tough/Hero rules), branch `024-validate-wound-splits`

## Goal
Close the illegal "wound split" the old TODO in `AssignWoundsResults.TryAddWounds` described: the GDF/OPR
rule requires finishing one wounded (still-alive) model before starting a fresh one, so a unit may never
be left with two different Tough models alive-and-partially-wounded. "Done" = the engine cannot produce
that state regardless of which resolver (CLI/GUI/AI, local or networked) drives the assignment.

## Notes
- 2026-06-21: Implemented on branch `024-validate-wound-splits` (both repos).
  - **Premise re-verified first.** The TODO's exploit ("assign a partial, unassign from another model,
    reassign to a different Tough model") requires `TryRemoveWounds`, which has **zero callers** — none of
    the three resolvers ever un-assign. And the pure-add path can't split: `TryAddWounds` pours a model's
    full remaining capacity (capped by the pool), so a *partial* assignment only happens once the pool is
    exhausted, which ends the loop. So the bug was **latent** (reachable only via the dead method), not live.
  - **Fix (engine, `AssignWoundsResults`):** `TryAddWounds` now refuses to *start* a model (nothing pending
    on it yet) while `AnyOtherModelMidFill` — another model alive with wounds already pending this
    assignment. Mirrored in `CanAssignWoundTo` so the GUI gray-out / map dimming stays honest. The unused
    `TryRemoveWounds` (the only lever to the illegal state) is deleted, and the TODO is gone.
  - **Tests:** 3 new cases in `ToughWoundOrderingRuleIntegrationTests` — touched model fills before any
    other; a full greedy add sequence never leaves two alive partials; and the guard itself rejects a fresh
    model while another is mid-fill (precondition stood up via the public `PendingWounds[i].Wounds` setter,
    simulating a future un-assign affordance / buggy caller). Engine suite 623/0; full app build clean;
    headless smoke exits 0.

## Decisions
- **Approach: harden the invariant, drop the dead remover** (chosen over "delete only" and "harden + keep a
  LIFO undo"). The guard makes the rule hold for *any* caller — a future un-assign button, AI path, or
  network replay can't smuggle in a split — while removing `TryRemoveWounds` keeps the API free of an
  unused, unvalidated mutator. If an undo affordance is wanted later, re-add it with the same guard.
- **Guard belongs in `AssignWoundsResults`, not the resolvers.** Same reasoning as #023: the results object
  is rebuilt per-resolver and on the networked client, so the invariant must live where every path shares it.
- **No "Awaiting verification" hold.** The guard is unreachable through the shipped UI (greedy fill already
  prevents the state), so there's no new in-app behavior to eyeball — it's a correctness backstop + dead-code
  removal, fully covered by tests. Marked done on engine verification.

## Deferred (explicitly, not silently cut)
- **#023's "which wounded model absorbs the shortfall" sub-choice** is a separate *feature* (letting the
  defender choose), not split-prevention — it stays deferred under #023, not folded in here.
- **`TryAddWounds` capacity quirk:** `woundsToAdd` is `min(model.RemainingWounds, pool)` and ignores wounds
  already pending on the entry, so *continuing* a model that somehow already carries out-of-band pending
  wounds overshoots and is rejected rather than clamped. Unreachable in normal flow (a model only carries
  pending wounds once the pool is spent). Left as-is; noted so a future un-assign feature accounts for it.

## Outcome
Engine hardened so illegal Tough wound-splits are impossible by construction; dead `TryRemoveWounds`
removed; TODO retired. Submodule `b97c9c5`. Suite 623/0, build clean, headless smoke exit 0.
