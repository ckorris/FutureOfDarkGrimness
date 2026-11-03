# 023 — Tough wound-priority

**Status**: done
**Related**: #024 (illegal wound-split validation), #006 (Hero — supplied the hero-model identity the "heroes last" clause needed), #031 (Defense/unit rules umbrella), branch `UITweaks-6-15-2026`

## Goal
Enforce the GDF/OPR Tough wound-ordering rule: when a unit takes wounds, an already-wounded (but still
alive) model must receive wounds before any fresh model, and a wounded model must be finished (killed)
before moving on. "Done" = the engine forces this allocation (player cannot leave a wounded model and
wound a fresh one instead), the GUI reflects the forced state, and — per the rule's "heroes last" clause
— a joined hero is wounded only after the rest of the unit. Wound assignment that has no remaining choice
should resolve without prompting the player.

## Notes
- 2026-06-15: First slice implemented (engine + GUI), on branch `UITweaks-6-15-2026`.
  - Engine (`AssignWoundsResults` ctor): mandatory **pre-assignment** — wounds pour into each model with
    prior damage (`WoundsDealt > 0`) in unit-list order until it dies or the pool empties; these are
    locked (non-cancellable). Added `RemainingAssignableModelCount` + `HasRemainingChoice`.
  - Engine (`AssignWoundsStage`): the player-choice branch now only emits `AssignWoundsRequest` when
    `HasRemainingChoice`; otherwise it `AutoFill()`s and resolves silently (no window when there's no
    decision — e.g. the pre-assignment consumed the pool).
  - GUI (`GuiAssignWoundsResolver`): pre-assigned wounds show as `(N assigned)` on the buttons and in the
    map tooltip; models that can no longer take wounds are dimmed on the table canvas (mirrors the
    disabled buttons). The earlier UX slice (per-model weapon lines on buttons, button↔map hover
    highlight, map tooltip, click-model-to-assign) is in the same resolver.
  - CLI (`AssignWoundsResolver`): remaining-wounds listing now subtracts pending and shows assigned count.
  - Tests: `ToughWoundOrderingRuleIntegrationTests` (6) — ctor pre-assignment + stage prompt
    suppression/emission. Full engine suite green (562/0).

## Decisions
- **Rule lives in the `AssignWoundsResults` constructor**, not the stage. Reason: the resolver (CLI/GUI/AI)
  rebuilds its own results from the request, and networked play resolves on a different machine. Putting
  the pre-assignment in the constructor makes it deterministic and identical everywhere (it's a pure
  function of synced unit state + total wounds, since wounds aren't applied until `ApplyWoundsStage`). The
  stage only *gates the prompt* via `HasRemainingChoice`.
- **Each click/assignment fills a model to capacity** (existing `TryAddWounds` behavior, kept). Side
  effect: within one allocation you can't sprinkle single wounds across multiple fresh Tough models —
  concentration is implicitly forced, which matches the rule's intent for the in-allocation case.

## Deferred (explicitly, not silently cut)
- **"Heroes last"** — ✅ **done under #006** (closed 2026-06-21). When #023's first slice was written the
  blocker was identifying the hero model; that arrived with #006 (Hero), which itself implemented the
  wound-last ordering (`AssignWoundsResults` orders the hero last + `TryAddWounds`/`CanAssignWoundTo`
  reject the hero while any rank-and-file model has room). It composes with #023's pre-assignment — the
  pre-assignment walks the same hero-last order and the hero guard defers the hero even when already
  wounded. Covered by `HeroWoundAssignmentTests`. Was the only thing blocking this item's close.
- **Multiple already-wounded models, pool too small to finish all** — they fill in unit-list order; the
  defender doesn't get to choose which wounded model absorbs the shortfall (a legal sub-choice). Rare;
  documented by `Construct_MultipleWoundedModels_FillInListOrder`.
- **Illegal wound-split validation** (assign → unassign → reassign) was under #024 — **done 2026-06-21**:
  the split was latent (only `TryRemoveWounds` could reach it, and it had no callers), now closed by a
  finish-before-next guard in `TryAddWounds` + removal of `TryRemoveWounds`. See `WorkItems/024`.

## Outcome
**DONE 2026-06-21.** The Tough wound-ordering rule is fully enforced on master and the item is closed by
reconciliation — no new code was needed, because its one open dependency resolved elsewhere. State on
master (super `83ff2f7` / submodule `3b81e2e`):
- **Mandatory pre-assignment** to already-wounded models, finishing each before the next, lives in the
  `AssignWoundsResults` constructor (`PreAssignToAlreadyWoundedModels`); the stage only prompts when
  `HasRemainingChoice`, so no-choice allocations resolve silently. GUI/CLI reflect the forced state.
- **Finish-before-next guard** (the illegal-split backstop) landed under #024 (`TryAddWounds` +
  `CanAssignWoundTo` refuse to start a fresh model while another is mid-fill; dead `TryRemoveWounds` removed).
- **"Heroes last"** landed under #006 (Hero), which supplied the hero-model identity this clause required
  and implemented the ordering; it composes with the pre-assignment in the same constructor.

Verified on master: engine suite **633/0** (incl. `ToughWoundOrderingRuleIntegrationTests` ×5 and
`HeroWoundAssignmentTests` ×7), full build clean, headless smoke exit 0. Only remaining deferral is the
rare "which already-wounded model absorbs the shortfall when the pool can't finish them all" sub-choice
(documented above, not in scope). No "Awaiting verification" hold — the forced-allocation UI and hero
gray-out were already hand-verified under #023's earlier slice and #006 respectively.
