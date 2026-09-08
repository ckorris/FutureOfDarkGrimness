# 398 - Combat Calculator: visual + usability pass

**Status**: in-progress
**Related**: #397 (the screen this reworks), #325/#345 (the in-game shooting forecast whose chip
wording this matches), #329 (the printed-list stat pills this borrows), #292/#259 (the rule underline +
hover convention), #153 (Army Forge - the shared unit column this widens/wraps)

## Goal

Owner request after seeing #397 running: "consider best practices in UI/UX and well-known principles
... general aesthetics, and the theme of the rest of the app ... make this look a lot nicer and be more
usable."

Done = the results pane reads as a designed panel rather than a debug printout: a headline the eye
lands on first, one weapon row per weapon in rules order (Attacks -> Hits -> Saves -> Wounds), the
situational inputs beside the outputs they drive, one weapon notation and one chip style shared with
the rest of the app, and no clipped text in either side column.

## Findings that motivated it (from the owner's 2026-09-08 screenshot)

1. **No hierarchy.** The two numbers that matter (6 hits, 2.78 wounds) are the same size and weight as
   everything else. `RaylibRenderer.LargeFont` exists and five other screens use it; this one did not.
2. **Three notations for one weapon.** `18", A3, AP(1)` in the side columns, `18in, A3 AP1` in the
   middle, and a rule drawn as plain blue text in the middle vs the #292 underline+hover in the sides.
3. **Sequence not modelled.** OPR resolves Attacks -> Hits -> Saves -> Wounds; the pane drew a nested
   tree with `->` arrows and put the save chips BELOW the buckets they explain.
4. **Proximity violated.** The variables drive the results but sat a screen away under a fixed 1/3
   split, with most of the middle column empty between them.
5. **Clipping.** Both side columns cut upgrade lines (`(+1`, `Deadly(`, `Sh`) at 27% width while the
   middle column had the most space and the least content.
6. **Empty tag line.** A volley with no save tags still emitted a blank line.
7. **Unexplained modifier.** `Save 5+ 0.5 hits [Ferocious]` is correct (Ferocious fires over 9in and
   the distance was 12in) but nothing said so, and nothing hinted that dragging the distance under 9in
   removes it - the best demonstration the tool could give of why the variables exist.

## Notes

_Newest on top._

- 2026-09-08 (slices 1-3, the middle column): app **2878/0**, build clean. The pane now reads
  headline -> table -> working:
  - **Headline**: the two numbers in `LargeFont` on the header accent, captioned, with a wound meter
    (`UiChrome.DrawMeter`) and "7.33 of 9.00 wounds remain" under them. They are the only large,
    coloured thing in the pane, so the eye lands there and can stop there (finding 1).
  - **Pipeline table**: one row per weapon, columns DICE / HIT / HITS / SAVE / WOUNDS in the order the
    rules resolve, with the weapon's stat subline in the in-game shoot panel's notation and each rule
    underlined + hoverable via `RuleHoverText.DrawInline` (findings 2 and 3). The working - modifier
    chips and any real save split - sits in a sub-row beneath the row it explains, and is omitted
    entirely when there is nothing to say.
  - **Situation bar**: the inputs moved from a fixed bottom third to a single row directly under the
    tabs, so an input sits beside the output it changes (finding 4). The distance is a slider over
    0-48in with ticks at every reach the fight's weapons actually have, plus a numeric field for exact
    values; dragging it walks the whole table through its thresholds, which is what makes a range-gated
    rule visible instead of mysterious (finding 7).

- 2026-09-08 (slice 0, view model): app **2877/0** (+11), build clean. `CombatReportView` +
  `VolleyRowView` + `SaveLineView` (`FdgRaylib/Rendering/CombatCalc/CombatReportView.cs`) turn a
  `CombatReport` into every string the pane draws, so the formatting is unit-testable and the ImGui code
  is a painter. Fixes findings 1 (number format), 6 (empty tag line) and the save-bucket half of 3
  before a single pixel moves. `CombatReportViewTests` (11) pin two-decimal formatting, the locale trap
  (a de-DE machine would otherwise print "2,78"), merged-vs-split save buckets, and the wipe-out
  divide-by-zero.

## Decisions

## Deferred (recorded, not silently cut)

- **Expected models killed** stays deferred (it already was, in #397). Under `ProbabilisticDiceRoller`
  wounds are fractional, so a model holding 0.4 wounds is alive and a living-model count would read as
  a false integer. The headline shows the wound bar only. It falls out of the deferred Monte Carlo
  distribution work, which is where it belongs.

## HAND-VERIFY (owner)

## Outcome
_Open._
