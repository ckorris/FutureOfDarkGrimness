# 259 — Army Forge: underlined special rules with hover tooltips

**Status**: in-progress (implemented + suite green; awaiting GUI hand-verify)
**Related**: #151 (authored the `SpecialRuleDefinition.Description` field this reads), #153/#156 (the Forge
itself), #241 (import modal's "rules not enforced by the engine" list — the same unimplemented-rule set),
#260 (closed as not-a-defect; its investigation fixed this item's case-sensitivity bug)

## Goal

In the Army Forge, every instance of a special rule name reads as a rule — underlined — and hovering it
shows what the rule does, matching the real Army Forge. Done = all four rule-bearing surfaces (list-pane
rows, the config pane's compiled unit, the roster preview, and the interactive upgrade editors) underline
their rule names and tooltip them, with no regression to row selection or to clicking an upgrade option.

## Notes

- 2026-07-23: Implemented. Two new app-side files, four call-site clusters in `ArmyForgeScreen`.
  - `RuleGlossary` — name -> description, built per `BookFile` from `CoreRuleCatalog.All` then the book's
    embedded `RuleDefinitions` overriding by name (the precedence army load uses). Resolves a
    `SpecialRuleEntry` structurally: numeric looks up `Name` not `"Tough(3)"`, an alias falls through to
    the rule it renames. Rebuilt through `ArmyForgeScreen.UseBook()`, the single seam every `_book`
    assignment now goes through (ctor / switch / load / share-link import).
  - `RuleTextFlow` — segments a printed line into plain text + rule names, then either lays it out by hand
    with word wrapping (`Draw`, for read-only lines) or decorates the label ImGui already drew for a
    radio/checkbox (`DecorateControlLabel`). Segment builders are pure and reproduce the exact strings they
    replaced (`Flatten(WeaponLine(w)) == ArmyBuilderScreen.WeaponSummary(w)` is a test).
  - Upgrade option labels are free text from the imported book, so their rules are found by scanning — but
    only against the rules that option actually grants, never the whole ~316-name corpus, so an unrelated
    word can never light up.
  - Coverage measured over all 47 bundled books: 94.3% of rule references resolve to a description
    (1825/1936). The remainder are rules OPR emits that the engine does not implement.
  - Verified: `dotnet build` 0 errors; engine 1892/1892; app-side 445/445 (30 new); headless smoke exits 0.
- 2026-07-23 (follow-up): **the glossary's case-sensitivity was a bug**, fixed. See #260 — `RuleResolver`
  has been case-insensitive since #100, so the five book/catalog casing divergences resolve fine, and the
  tooltip was wrongly reporting them as inert. The glossary now uses the resolver's `OrdinalIgnoreCase`
  comparer, with a test pinning that the two agree name-for-name.

## Decisions

- **Read the existing `Description` field rather than author new text.** #151 already put a player-facing
  description on every `SpecialRuleDefinition` for the granted-rule token hovers, and `BookRuleSupplement`
  already embeds the faction subset into each `.fdgbook`. The glossary was the only missing piece — no new
  data, and the tooltip necessarily agrees with what the engine will do.
- **The glossary tracks `RuleResolver`'s lookup semantics** (case-insensitive, `OrdinalIgnoreCase`), so it
  is silent exactly when army load would also fail to resolve a name and a tooltip can never contradict
  what the engine does. This was initially built case-SENSITIVE on the strength of a stale doc comment —
  see #260 for the correction and the regression guard.
- **Unimplemented rules underline faintly and say so** (user sign-off, 2026-07-23) rather than rendering
  plain. Same fact the #241 import modal reports in bulk, now per rule at the point of use.
- **Interactive controls keep their clickable label** (user sign-off, 2026-07-23). Re-drawing a radio's
  label as separate hoverable items would have cost the label its click target; instead the control is
  drawn unchanged and the underlines/hit-spans are computed over the label rectangle ImGui reports. Cost:
  no wrapping inside a control label — which those labels never had anyway.
- **The list-pane row's hit rectangle is now measured, not assumed.** `DrawListRow` sized its invisible
  full-row selectable as `2 + weapons + (rules ? 1 : 0)` lines. Segmented lines wrap, so that constant
  would have left the bottom of a wrapped row unclickable; `RuleTextFlow.MeasureLines` runs a layout pass
  first.

## Outcome

_(pending hand-verification)_

Hand-verify checks:
1. List pane — a unit with several rules: names underlined, hover shows "Name" + description; the row still
   selects when clicked anywhere, including on a wrapped stat line.
2. Config pane, list unit — weapon rules, wargear rules, and the unit rule line all underline and tooltip.
3. Roster preview — same, plus rule names inside upgrade option labels.
4. Upgrade editors — rules underlined inside radio/checkbox/stepper labels; clicking the label still
   toggles the option, and a disabled ("none to replace") option still tooltips.
5. An unimplemented rule (e.g. Ratmen "Repel Ambushers") shows the faint underline + the not-enforced note.
6. Switch books / load a saved list / import a share link — tooltips follow the new book's own rules.
