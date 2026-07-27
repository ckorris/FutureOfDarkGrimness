# 292 — Weapon select shows special rules with per-rule hovers

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
- **`RuleTextFlow` was NOT generalized** (reversing this file's original plan). Two things differ, not
  one: the rule TYPE (in play a `ResolvedRule` carries its own description, so there is no `RuleGlossary`
  lookup at all) and the DRAWING MODE (the shoot panel paints rows onto a draw list at computed offsets
  over one invisible `Selectable`, so nothing may touch the ImGui cursor - which is exactly what
  `RuleTextFlow.Draw` is built to do). Sharing would have meant threading both a rule abstraction and a
  positioning abstraction through one function, and moving the Forge's description lookup from draw time
  to build time across `ArmyForgeScreen` / `ArmyBuilderScreen`. A sibling `RuleHoverText` duplicates ~15
  lines of underline/hit-test and keeps the visual convention identical; the shared-abstraction refactor
  is deliberately deferred, not forgotten.
- A hovered rule name outranks the row's own "why is this weapon grayed out" tooltip - it is the more
  specific thing under the cursor, and ImGui allows one tooltip per frame.
- Rule names are tinted slightly brighter than the rest of the subline, so they read as "there is more
  here" even before the underline is noticed.

## Outcome
Shipped 2026-07-26 (`c0d0e9e`). New `FdgRaylib/Rendering/RuleHoverText.cs`: segment builders
(`WeaponStatLine`, `RuleSegments`), `Tooltip`, a cursor-free `DrawInline` that underlines rule runs and
reports what the mouse is inside, and `ShowTooltip`. `GuiChooseRangedAttackResolver` uses it for the
weapon subline and adds a "Rules:" block with full descriptions to the Details pane. Undocumented rules
underline faintly and say they do nothing in play, matching #259. 7 new `RuleHoverTextTests`, including
the invariant that segmenting reproduces the previous line byte-for-byte. App suite 639/639, engine
2196/2196, headless smoke exits 0. Awaiting GUI hand-verify.
