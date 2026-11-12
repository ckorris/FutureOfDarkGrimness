# 298 — Resolver option buttons too short; melee weapon menu hid its rules

**Status**: in-progress (awaiting GUI hand-verify)
**Related**: #292 (weapon rule hover text in the shoot panel), #259 (Forge rule tooltips), #248 (hotkeys/back), engine commit `7a824db`

## Goal
Every resolver option list reads like the panels that were already right (unit activation, assign wounds,
the shoot panel's weapon/target rows): the button is comfortably taller than its label at any UI scale, and
the melee weapon menu says what a weapon's special rules actually do. Done when the four panels the owner
named (action menu, melee weapon, spell, charge/melee target) and the rest of the same family are
font-relative, and a rule-bearing weapon's menu entry carries its rule text.

## Notes

- 2026-07-27: Shipped both halves.
  - **Root cause** of the short buttons: these panels hardcode pixel heights (28-32px) while the ImGui font
    is `18f * uiScale` (`RaylibRenderer.cs`, up to 25.2px at the 4K anchor). The label filled the button edge
    to edge. The panels the owner called "good" all size their rows from the font instead - the shoot panel's
    `GetTextLineHeight() * 2.4f` is the row height now shared by everything.
  - `ResolverPanelLayout.OptionRowHeight(lineHeight)` (+ live-font overload) and `ActionRowHeight()`
    (2.0 line heights, for Back/Cancel) are the single source of truth. Applied to: string selection
    (action menu + melee weapon), cancellable selection (charge target, #100 targeting), selection Back,
    spell picker, ability effect, cast assist, deployment zone, yes/no, aircraft fly-off confirm, melee
    defender confirm card, and the shoot panel's rows + footer.
  - Same root cause fixed wherever a hardcoded pixel step sat next to text: instruction blocks in the two
    selection panels were assumed 48px (a wrapped prompt ran under row 1 at the 4K font), and the spell
    picker's header offsets, relay note, advice notes, boost stepper and readout lines were all flat pixel
    counts. Those now step by the live line height, and the notes wrap instead of running off the panel.
  - **Melee weapon rules**: `ChooseMeleeWeaponStage` now fills `StringSelectionRequest.OptionDescriptions`
    (an existing but until-now unused channel) with one `"Name - description"` line per documented rule.
    Both front ends already rendered descriptions, so the GUI shows them as subtext and the CLI as an
    indented block (the CLI resolver now indents each line of a multi-line description).
  - The string-selection panel's rows are drawn like the selection panel's: an empty button with the label
    painted on the draw list, wrapped. `ImGui.Button` center-CLIPS a label wider than the button, which is
    what was silently eating the tail of a weapon option - the rule names included.
  - Verified: engine 2232/2232, app 663/663 (incl. new `MeleeWeaponRuleDescriptionTests` x4 and
    `ResolverPanelLayoutTests` x4), full `dotnet build`, headless smoke exit 0 (tie, 4 rounds).

- 2026-07-27 (follow-up, owner asked for it in the same session): the movement / placement / consolidation
  **footer** buttons converted too - Done, Back, Stay, Skip all, Undo, Auto-place/Auto-advance, Clear,
  Restart, and the objective/terrain Confirm+Cancel pair. Done/Confirm take a full option row; every
  secondary and destructive footer button takes `ActionRowHeight`. `PlacementPanelLayout`'s costed footer
  constants (26-32px) became line-height functions and `FooterHeight`/`StatsHeight` gained a `lineHeight`
  parameter, so the budget that keeps Done on screen still matches what the drawing code uses; the stat
  box's 90px floor became 5 text lines. App suite 664/664 with a new pin that the footer grows with the
  font; headless smoke exit 0.

## Decisions

- **Font-relative, not "twice 28px".** The owner asked for "at least twice as tall"; a flat 56px would have
  been wrong-by-construction in the same way 28px was - a pixel constant that ignores the DPI-derived UI
  scale. `2.4 x line height` gives ~60px at the 4K anchor (>2x the old row) and ~43px at the 1.0 scale
  floor, where the whole UI is proportionally smaller anyway.
- **Weapon rules belong to the engine.** The label already listed rule NAMES; the missing thing was what
  they do, and that text lives on `SpecialRuleDefinition.Description`. Sending it as the option's
  description means the CLI gets it too and no front end has to re-derive rule text from a formatted string.
- **Undocumented rules are omitted** rather than listed with a "does nothing" note. The shoot panel's
  hover tooltips (#292) are where that distinction is made; a menu subtext of empty lines helps nobody.
- Whole-panel scope (not just the four named): the same hardcoded sizes ran through yes/no, cast assist,
  deployment zone, ability effect and aircraft advance, and leaving them would have shipped two button
  sizes. Owner signed off on the wider scope.

## Outcome
Pending GUI hand-verify.
