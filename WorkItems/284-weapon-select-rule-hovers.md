# 284 — Weapon select shows special rules with per-rule hovers

**Status**: in-progress
**Related**: #259 (Army Forge rule hovers / `RuleTextFlow`), #027 (`WeaponStatFormatter`)

## Goal
In the Shoot panel (`GuiChooseRangedAttackResolver`) a weapon's special rules must be readable and
explainable, the way the Army Forge explains them: the rule names on the weapon row's stat subline are
underlined and individually hoverable (tooltip = name + description), and the Details pane lists the
selected weapon's rules with their full descriptions.

Done when: hovering "Rending" on a weapon row raises its description; the Details pane shows a Rules
block for the selected weapon; an undocumented rule underlines faintly and says it is inert in play
(same convention #259 established).

## Notes
- 2026-07-26: filed from a play session. In-game weapon rules are `ResolvedRule` (carrying
  `Definition.Description`) rather than the army-file `SpecialRuleEntry` that `RuleTextFlow`/`RuleGlossary`
  were written for, so the hover path needs to accept in-game rules too.
- 2026-07-26: design fork resolved with the user — **row subline + Details pane** (not one or the other).

## Decisions
- `RuleTextFlow` is generalized over "a rule that can describe itself" rather than duplicated: the
  segment/layout/draw machinery is identical, only the description lookup differs.
- The shoot panel's weapon rows are drawn with a raw `ImDrawList` over an invisible `Selectable` (no
  ImGui items per line), so the underline + hit-testing has to be done against the same draw list rather
  than via `RuleTextFlow.Draw`, which assumes it owns the cursor.

## Outcome
