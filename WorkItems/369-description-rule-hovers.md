# 369 - Rules named inside an ability's description are not explained

## Goal

Follow-on to [#367](367-ability-action-descriptions.md). An ability action now carries its rule's
description, but that description is written in terms of the rule it CONFERS - "...which gains Courage
for its next relevant roll" - and Courage is the rule the player does not know. The name sat in the
subtext as plain prose with no way to learn what it does.

Weapon rules have had the treatment since #292/#336: the name is underlined where it already sits and
hovers its own text (GUI), or is printed as an indented line (CLI). This extends it to the subtext.

## Notes

### 2026-08-08 - implemented

- `EffectRuleReferences.NamesIn(Effect)` (engine): the rule names an effect grants, suppresses, marks
  with, or attaches to hits - `AddRule`, `Aura`, `MarkTarget`, `IgnoreRule`, the three `WithRules`
  carriers, recursing through `MoraleTestThen` (the only wrapping effect). Deliberately NOT a text scan
  of the description: half the catalog's names are ordinary English words ("Fast", "Tough", "Devout"),
  so a scan would underline prose.
- `StringSelectionRequest.OptionDescriptionRules`: same `List<OptionRule>` shape as `OptionRules`,
  pointed at the DESCRIPTION instead of the label. Separate map on purpose - an ability row's label IS
  a rule name ("Courage Buff"), so the label matcher would find the "Courage" inside it and explain the
  wrong rule.
- `ChooseActionStage` fills it: for each offer, the effect's referenced names that (a) actually appear
  in the description text and (b) resolve in the registry. An unmentioned name has nowhere on screen to
  be underlined; an unregistered one cannot be explained. A registered-but-undocumented one is KEPT,
  because "this rule is not enforced" is worth knowing at the moment of the choice.
- GUI: `MenuRow.DescSegments` / `DescSegmentLines`, split by the existing `OptionRuleSegments.Build`
  and drawn with `RuleHoverText.DrawInline`. `DrawInline` gained a `fontScale` parameter - the subtext
  renders at 0.82, and neither `CalcTextSize` nor the draw list's default-size `AddText` honours
  `SetWindowFontScale`, so a scaled caller would paint its underlines away from its glyphs.
- CLI: an indented `Name - description` line under the subtext, matching what it already does for
  `OptionRules`.

### Deferred / out of scope

- A referenced rule name that is a substring of a longer word in the description would underline that
  word's prefix. Effect-derived names keep the exposure small and no corpus description hits it today;
  a word-boundary check is the fix if one ever does.
- Only the Choose Action menu populates the new map. Other string menus (spells, weapons) are
  unchanged - their descriptions do not name conferred rules today.

## Outcome

Implemented + tested (engine: 2 request-level tests + 4 on `EffectRuleReferences`; app: 2 on the
subtext splitter). CLI-verified on the Blessed Sisters Procession Altar - each buff row now prints
"Courage - +1 to this unit's morale test rolls." under its description. Awaiting GUI hand-verify of the
underline + hover.
