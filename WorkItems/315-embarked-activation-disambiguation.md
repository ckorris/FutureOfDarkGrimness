# 315 — Embarked-unit activation disambiguation (label + hover-linked transport highlight)

**Status**: in-progress
**Related**: #035 (Transport core), #096 (transport visuals), #223 (option hover tooltip), #286 (two-way canvas hover), #248 (keyboard highlight)

## Goal
2026-08-01 game report: with two transports each carrying an identically-named unit, the activation
picker showed two byte-identical rows and there was no way to tell which unit was which. Done means:
(1) an embarked unit's option label names its ride — "Warriors (in Rhino)" — engine-side, so the CLI,
networked clients, and the stat tooltip all get it; (2) hovering an embarked unit's row in the GUI
rings its transport's models on the table in a distinct style (the existing hover ring silently
no-ops for embarked units because their models sit at the (0,0) sentinel); (3) per the #286 two-way
rule, canvas-hovering a transport emphasises its occupants' rows in the list (fill + border +
scroll-into-view, the Assign Wounds idiom).

## Notes
- 2026-08-02: Implemented + tested, awaiting GUI hand-verify. Engine: `ChooseUnitToActivateStage.GetOptionLabel`
  suffixes valid+invalid options; `EmbarkedActivationLabelTests` (4 tests, incl. the same-named-twins case).
  App: `TransportOptionLookup` (pure, 5 tests) + `GuiUnitSelectionResolver` rings the transport in amber on
  row hover; new `IsRowEmphasized` seam in `GuiSelectionResolver` paints + scrolls occupant rows on canvas
  transport hover. Suites: engine 2575/2575, app 892/892, headless smoke exit 0.
  GUI hand-verify checklist: (1) two transports with same-named cargo -> rows read "Warriors (in X)";
  (2) hover an embarked unit's row -> its transport rings amber on the table; (3) hover a transport model
  on the table -> its occupants' rows highlight yellow and scroll into view; (4) hover an on-table unit's
  row -> unchanged cyan ring.
- 2026-08-02: Filed. Design agreed with Chris: label suffix + hover highlight are complementary —
  the suffix disambiguates at rest and off-GUI, the highlight resolves the residual case where the
  transports themselves share a name. Rejected: camera pan-to-unit (no camera-focus machinery
  exists; auto-moving the camera on hover is disorienting), client-side-only detail line (weaker
  than the engine suffix — invisible to CLI/AI). Key simplification: the transport is always one of
  the acting player's own units, so it is already present in the request's option lists — the GUI
  resolver finds it there and needs no ITableState injection.

## Decisions
- Suffix lives in `ChooseUnitToActivateStage` option-label build (both valid and invalid options —
  an embarked unit that already activated still needs disambiguating), precedent: the deploy
  picker's "(Ambush)" suffix. Defensive: no suffix if the transport id resolves to no unit.
- Generic duplicate-name numbering (#N, the Army Forge idiom) deliberately NOT applied to on-table
  units: the existing hover ring already disambiguates those spatially. Revisit only if it bites.

## Outcome
(open)
