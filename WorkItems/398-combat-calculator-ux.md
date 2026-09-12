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

- 2026-09-12 (round 4, the owner's second review pass + a crash it turned up): app **2905/0** (+14),
  engine 3308/0 (1 skipped), build clean, smoke exits 0.
  - **Round 3 shipped a crash, and this is what it was.** `DrawWrappedOptionLabel` overlaid its click
    target by drawing the text, winding the ImGui cursor BACK over it, submitting the invisible button,
    and winding the cursor forward again. That last `SetCursorPos` leaves the cursor past the pane's
    content extent with no item after it to justify the extent, and ImGui asserts on exactly that
    (`ErrorCheckUsingSetCursorPosToExtendParentBoundaries`, imgui.cpp:11010). In the **Army Forge** an
    upgrade section IS the last thing in the detail pane, so selecting any unit with options aborted the
    process. In the calculator a join button happened to follow it, which is the only reason round 3
    looked fine. The footprint is measured up front now (`RuleTextFlow.MeasureHeight`, the same formula
    `Draw` reserves with), claimed by the button, and the text drawn over it: the cursor only ever moves
    backwards and the very next call validates the extent.
  - **`??=` short-circuits.** `foreach (var row in rows) tooltip ??= DrawVolleyRow(row);` stopped
    CALLING `DrawVolleyRow` once a tooltip was in hand, so hovering a rule on the second of three
    weapons made the third weapon disappear while the tooltip was open. Written out longhand.
  - **Tabs per column** (`SideTabs`): one tab per candidate unit, "+" copies the unit in hand into a new
    tab beside it, "x" closes (hidden while one tab remains). Only the selected tab fights, so flipping
    tabs re-runs the fight. The copy deep-copies the `BuilderList` and shares the book - editing the
    variant must not reach back into the original, and a book is half a megabyte of parsed JSON.
  - **The distance track is three bands**: green where every weapon reaches, yellow where only some do,
    red where none do, painted before the slider with its frame background pushed transparent. Distances
    snap to whole inches, with coarse/fine steppers (6in/3in) either side of the number.
  - **60 FPS.** Raylib samples the mouse once per frame and hands ImGui that sample, so at 30 a click
    under 33ms could be pressed and released between two samples and never reach ImGui - the owner's
    "feels like I need to double-click, and then I click a unit I didn't mean to" (the second click
    landing on what the first one had revealed). Nothing here is expensive to draw.
  - **Chrome sized from the font**: Back/Cancel/Swap/Load list/join buttons via `UiChrome.ButtonSize`,
    and the army-list pills' padding, rounding and gaps derived from the style rather than the flat
    7px/8px literals they were. `ScaleAllSizes` had been scaling everything EXCEPT the hand-written
    numbers, which is what made small resolutions look wrong.
  - **"(i) what this does not account for" removed** at the owner's request. The report's `Notes` had no
    other surface, so they are no longer shown anywhere (Warnings still are) - see Deferred.

- 2026-09-08 (round 3, wrapping + the army-list look): app **2891/0** (+3), engine 3308/0, build clean,
  smoke exits 0.
  - **Wrapping, now authorized.** The control is drawn with NO label and the text laid out by
    `RuleTextFlow.Draw`, which already wrapped (the stepper had been using it all along); an invisible
    button over the reserved rect keeps the label clickable, which the naive fix would have cost. The
    rule tooltips are unaffected - `Draw` hit-tests the mouse itself, in the same frame, before that
    button is ever submitted. `DecorateControlLabel` had no other caller and no tests, so it went, and
    the side columns' horizontal scrollbars went with it: they were standing in for this, and a
    horizontally scrollable child reports a content width the wrap would have had to fight.
  - **The unit columns now wear the printed list's own badges.** `UiChrome.DrawPill` is
    `ArmyListOverlay`'s two-tone pill promoted, not a lookalike - `ArmyListOverlay` calls the promoted
    one. Quality/Defense/Tough as pills; weapons as the list's `Weapon / RNG / ATK / AP / SPE` table
    through the shared `ArmyListLayout` formatters, so two guns can be compared down a column instead of
    across parenthesised sentences.
  - **Tough is read off the TYPED rule** (`SpecialRuleEntry_CoreNumeric.NumericValue`), not by parsing
    the "Tough(3)" string this same code formats - the number is right there, and a parser would be a
    second place to be wrong. An alias ("Ancient Hide (Tough(6))") counts.
  - **Colour now means something.** `ImGuiTheme.DamageAmber` = damage, wherever damage appears: the
    wounds headline, the WOUNDS column, and the spent slice of the wound meter (which gained a second
    segment - filled is what survives, amber is what the attack took off). Hits stay blue, because a
    die landing and a wound sticking are different kinds of thing. `HeroGold` moved to the theme and is
    shared, so a joined hero is tagged in the calculator in the same gold the army list uses.
  - **The Army Forge inherits all of this** - wrapping, pills, weapon table, UPGRADES header - because
    it is literally the same `ForgeUnitDetail`. That was the accepted trade for the wrap; keeping the
    two divergent would have been the worse outcome, and the Forge wanted the same fixes.

- 2026-09-08 (round 2, owner screenshot): app **2888/0** (+9), build clean, smoke exits 0.
  - **Game-system filter** (owner request): Grimdark Future / Age of Fantasy, mutually exclusive, GDF
    default, **per side**, remembered in `UserConfig` as `CalculatorSystemA`/`B`. Slugs rather than an
    enum so the file stays hand-readable and an unknown value degrades to the default instead of
    throwing - pinned, along with a pre-#398 config having no such field at all. Per-side because a
    cross-system what-if is a fair question of a calculator even though no real game allows it. Two
    exclusive buttons, not a combo: there are exactly two options, so a dropdown would cost a click to
    show what buttons show for free. Labels match the Army Forge's.
  - **Situation bar overflowed its column** - "Defender is in cover" was cut off at the edge, and the
    distance appeared TWICE (once inside the slider track, once in the field beside it, reading as two
    controls). Now: SITUATION on its own line, slider + numeric field, checkboxes on a second line, and
    the slider prints no number of its own.
  - **The working detached from its weapon.** The chips were their own table row, so a horizontal rule
    landed between a weapon and the explanation of its numbers. They now live inside the weapon cell,
    under an indented stat subline, and the out-of-range note stays in that cell too rather than
    spilling into the DICE column.

- 2026-09-08 (slices 4-5, the side columns and polish): app **2879/0**, build clean. Columns widened to
  30/40/30 (finding 5); each column headed by an **ATTACKER/DEFENDER badge** that follows Swap, with the
  points right-aligned so the two costs line up against the screen edges and can be compared; the unit
  picker's rows are now ONE two-line Selectable, closing the dead stat-line trap logged in #397; the
  empty state names the side that is missing rather than repeating "choose a unit on both sides"; the
  caveat wall became a single hoverable "(i) what this does not account for".

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

- **The middle column speaks the in-game combat notation, not the Forge's.** Two notations exist in the
  app already: the Forge prints `(24", A6, AP(4), Reliable)` and the in-game shoot panel prints
  `24", A6 AP1, Rending`. The calculator's side columns ARE the Forge's unit detail, so they keep the
  first; the results pane is a combat forecast, so it now uses the second via
  `RuleHoverText.WeaponStatLine` - the same call the live shoot panel makes. Three notations became
  two, each matching its surface, and the results pane inherits the #292 underline+hover convention
  instead of drawing rules as flat blue text.
- **The view model never derives arithmetic.** `CombatReportView` renders report fields to text and, in
  exactly one place, ADDS numbers the report itself split apart (save buckets sharing a threshold). No
  probability, threshold or modifier is computed there. #397's whole premise is that the figures come
  from the real stage chain, and a formatting layer is precisely where that would quietly erode.
- **Merging same-threshold save buckets is presentation, not a change of answer.** Furious injects hits
  saved at the SAME number the rest are; the report reports them separately because the stages produce
  them separately. Drawing them as two "Save 5+" lines asked the reader to add 1.5 and 0.5 and implied
  two different saves were being rolled. Genuinely different thresholds (Rending) still get their own
  line - merging those would be a lie rather than a tidy-up.
- **One chip palette, two renderers.** The dice overlay paints chips with Raylib on the canvas; the
  calculator paints them with ImGui in a panel, so they cannot share a draw call. They now share the
  colour constants (`UiChrome.ChipBackgroundRaylib`/`ChipForegroundRaylib`), which is what makes a
  calculator chip and a live-roll chip read as the same object.
- **Distance is a slider first, a number second.** The number was always available; what was missing was
  the ability to sweep it. Dragging walks the whole table through its thresholds, which is how a
  range-gated rule (the screenshot's unexplained `[Ferocious]`, live only over 9in) shows itself.

- **The tab bar owns which tab is selected.** `SideTabs.Active` mirrors whatever ImGui reports as the
  open tab each frame rather than driving ImGui from our own index. Two sources of truth for a selection
  disagree on exactly the frames that matter - the frame a tab is added, and the frame one is closed -
  and ImGui already resolves both.
- **A tab's key is not its index.** ImGui identifies a tab by id; keyed by index, inserting a copy in
  the middle renumbers every tab after it, which ImGui reads as "these tabs' contents changed" rather
  than "a tab was inserted". Keys come from a per-side counter, are never reused, and are re-issued on
  arrival after a Swap so a slot cannot bring an id its new column has already spent.
- **Middle-click does not close a tab.** It is the standard browser gesture, but it is invisible and
  there is no undo here: a stray middle-click would silently throw away a unit someone spent a minute
  configuring. The "x" is deliberate, so it needs no confirm dialog either.
- **Distances land on whole inches.** Every range-gated rule in the corpus is written in whole inches
  and a tabletop is measured to the inch, so the slider's halves ("13.47in") were never a distinction a
  player could act on - just a fresh simulation per pixel of drag.
- **The frame rate is an input-sampling rate.** Raising it was the fix for a latency complaint, not a
  rendering one; worth remembering before anyone lowers it again to save power.

## Deferred (recorded, not silently cut)

- ~~Wrapped upgrade labels~~ - **done in round 3** once the owner authorized the Forge change.
- **Segmented-control tabs - NOT done.** The plan floated replacing the ImGui tab bar with a custom
  segmented control. The tab bar is already themed, is the idiom used elsewhere in the app, and carries
  keyboard/focus behaviour a hand-drawn pair of buttons would have to reimplement. Not worth the risk
  for a cosmetic difference; say the word if you want it.

- **The report's `Notes` now have no surface.** The "(i)" line was their only one and the owner asked
  for it to go. They carry the "not priced in v1" caveats (no terrain, clear line of sight, no spent
  markers, Unpredictable branch `None`, ...) which are still true and still worth saying somewhere -
  a collapsing "assumptions" section under the table, or the screen's own help, whenever one exists.
  Warnings (a hero that could not join, a rule that failed to resolve) are still drawn.
- **An ImGui frame harness for tests.** Both defects this round were ImGui-layout defects that no test
  could reach: one asserted inside the native library, the other silently skipped a draw call. A test
  fixture CAN drive ImGui headlessly (`CreateContext`, `AddFontDefault`, `NewFrame`/`Render`, no GL
  needed) and would have caught both. Not built yet for one reason: `IM_ASSERT` aborts the process, so a
  regression would take the whole `dotnet test` run down with it rather than failing one test. Worth
  doing behind its own test project or an assert redirect.
- **Expected models killed** stays deferred (it already was, in #397). Under `ProbabilisticDiceRoller`
  wounds are fractional, so a model holding 0.4 wounds is alive and a living-model count would read as
  a false integer. The headline shows the wound bar only. It falls out of the deferred Monte Carlo
  distribution work, which is where it belongs.

## HAND-VERIFY (owner)

The layout itself is ImGui and cannot be asserted; the strings and the tick arithmetic under it are.

1. **Headline** - the two numbers are large and accent-coloured, captioned "expected hits"/"expected
   wounds", with the wound meter and "N of M wounds remain" beneath.
2. The meter empties as the attack gets deadlier, and a defender reduced to nothing leaves an empty
   track rather than a stray dot.
3. **Table** - one row per weapon, columns DICE / HIT / HITS / SAVE / WOUNDS, numbers aligned down the
   column; WOUNDS is accented.
4. The weapon subline reads `24in, A6 AP0` with rule names underlined, and hovering one shows its
   tooltip - inside the table, which is where it was most likely to break.
5. A volley with no modifiers shows NO chip line and no blank gap (the old empty-tag-line bug).
6. **Ferocious case** - Assault Buggy vs Warriors at 12in: the two "Save 5+" rows are now one line
   reading `2.00 hits (Ferocious)`. Drag the distance below 9in and it should vanish entirely.
7. **Slider** - ticks appear at each weapon's reach; dragging past one greys that row to "out of range
   - reaches Nin" live.
8. The numeric distance field and the slider stay in step, and neither goes below 0 or above 48.
9. **Melee tab** - the bar swaps to charging/fatigued, and charging starts ON.
10. **Badges** - the left column reads ATTACKER, the right DEFENDER; after **Swap** they follow the
    units, and the points stay right-aligned in both.
11. **Picker** - clicking the STAT LINE of a unit row now selects it, not just the name.
12. Empty state names the missing side ("Choose the defending unit on the right").
13. Side columns: a long upgrade line WRAPS inside the column, and clicking any line of it still
    toggles the control beside it.
14. **The Army Forge still works** - add a unit with upgrade options and select it. This is the crash
    round 3 shipped; it aborted the process rather than misdrawing, so it cannot half-work.
15. **Tabs** - both columns open with one "(empty)" tab. Choose a unit, press "+": a second tab appears
    holding a copy, already selected. Change an upgrade on it and flip back - the first tab is
    untouched and the numbers change as you flip.
16. The "x" appears only once a column has two or more tabs, and the last tab cannot be closed.
    Middle-clicking a tab does NOT close it.
17. Closing the selected tab leaves the column on its neighbour, not on nothing.
18. **Swap with unequal stacks** - three tabs on A, one on B, then Swap: B should hold all three (same
    tab selected as before) and A the single one.
19. **Distance bands** - two weapons of different reach: green up to the shorter, yellow between them,
    red past the longer. One reach only = green then red, no yellow. Melee-only units paint no bands.
20. The slider stops on whole inches; "-"/"+" move 3in and "--"/"++" move 6in, both clamped to 0-48.
21. **Buttons** - Back, Cancel, Swap and Load list are noticeably bigger, and at a small window size
    (F11 out of fullscreen) nothing in the middle column overlaps or spills.
22. Clicking an army in the picker now lists its units on the FIRST click.

## Outcome
_Open._
