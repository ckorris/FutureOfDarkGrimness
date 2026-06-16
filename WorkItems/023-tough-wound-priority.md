# 023 — Tough wound-priority

**Status**: in-progress
**Related**: #024 (illegal wound-split validation), #031 (Tough/Hero rules), branch `UITweaks-6-15-2026`

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
- **"Heroes last"** — a joined hero taking wounds only after the rest of the unit is NOT implemented.
  Needs the Hero/join rule (#031) to identify the hero model.
- **Multiple already-wounded models, pool too small to finish all** — they fill in unit-list order; the
  defender doesn't get to choose which wounded model absorbs the shortfall (a legal sub-choice). Rare;
  documented by `Construct_MultipleWoundedModels_FillInListOrder`.
- **Illegal wound-split validation** (assign → unassign → reassign) stays under #024; currently
  unreachable because nothing calls `TryRemoveWounds`.

## Outcome
_(open)_
