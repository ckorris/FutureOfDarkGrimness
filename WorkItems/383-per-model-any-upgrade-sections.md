# 383 — "Any model may ..." upgrade sections cap at one per option (should be one per model)

**Status**: in-progress
**Related**: #153 (importer/Forge), #218 (affects cost semantics), #219 (RefreshCosts field-transfer precedent), #241 (share-list import), #324 (replace pool sharing)

## Goal

The OPR "Any model may replace/take ..." sections offer a per-option count stepper whose SECTION total
is capped at the unit's current model count (and, for replaces, by target availability) — so a 3-model
Hive Warriors squad can take 3x Ravager Gun via "Any model may replace one Razor Claws". Done = the
Forge offers steppers with the shared budget, the validator flags overshoot, the importer classifies
new imports correctly, and the 22 affected bundled-book sections are re-stamped.

## Notes

- 2026-08-22: Filed from a player report with screenshots (Hive Warriors + Robot Snakes, both [3]:
  "you cannot give more than one shooting weapon ... in both cases it should be 3").
- 2026-08-22: Root cause verified against the raw Army Forge API. OPR encodes these sections as
  `select: {type:"any"}` + `model: true` on a `replace`/`attachment` section (`affects` is per-
  APPLICATION target count: "exactly 1", or null = the model's whole set). The importer only reads
  `affects`, so they land as `Affects=One` + `MaxPicks=options.Count` -> the Forge renders multi-select
  CHECKBOXES: each option once, section total = option count. Wrong bound in both directions
  (Robot Snakes: 2 options -> cap 2, should be 3; duplicates impossible everywhere).
- 2026-08-22: Full corpus decision table (all 47 GDF books, raw API, `select:any` sections):
  - `attachment` + `model:true` + affects exactly-1 (16): "Any model may take one X attachment" -> PER-MODEL counted.
  - `replace` + `model:true` + affects exactly-1 (3) or null (3): "Any model may replace [one] X" -> PER-MODEL counted.
  - `upgrade` + affects null (88): "Upgrade with any" -> subset-of-options checkboxes (correct today).
  - `upgrade` + `model:true` + affects all (7) / exactly-1 (1): "Upgrade all models/one model with any"
    -> subset checkboxes applied to all/one model (correct today).
  So the classifier is: select any AND model:true AND variant in (replace, attachment) -> per-model.
- 2026-08-22: Fix shape: new `UpgradeSection.PerModelBudget` flag; importer maps the classified
  sections to `Affects=Any` (counted stepper, per-application cost — the machinery "Replace any X"
  already uses) + `PerModelBudget=true` (the shared cross-option cap at the unit's CURRENT compiled
  model count). Forge steppers get the shared budget; `ListValidator` errors on overshoot;
  `SelectionSolver.CountedBound` caps its search. Bundled books re-stamped by section Id from the live
  API via a dev CLI, the #219 `--import-book` transfer precedent (books are curated snapshots — never
  wholesale re-imported).
- 2026-08-22: Combined squads: each copy keeps its own budget (each buys under its own bounds, #107) —
  the merged unit's doubled model count is NOT a doubled per-copy budget.
- 2026-08-22: Out of scope, unchanged: what one application CONSUMES for the affects-null replaces
  ("Any model may replace Spike Whips" removes one target copy per application — Robot Snakes' whips
  are one aggregate entry, quantity = model count, so availability already equals the model budget).

## Decisions

- Classify from the raw API's `model` flag by section Id transfer, not from bundled-data heuristics
  (labels/maxPicks are ambiguous: "Upgrade with any" shares the bundled shape exactly).
- `Affects=Any` + a flag, not a new Affects variant — rides the existing counted-stepper, per-
  application-cost, replace-pool and starved-replace machinery untouched.

## Outcome

(pending)
