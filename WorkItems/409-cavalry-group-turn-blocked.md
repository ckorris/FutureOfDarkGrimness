# 409 — Cavalry is very hard to manoeuvre: a packed unit of long bases can barely turn as a group

**Status**: done (2026-10-28)
**Related**: #399 A (group self-overlap gate), #366 (`ValidateNoSelfOverlap`), #282 / #362 (group rotation and one shared group heading), #277 (formation cycling), #150 / #341 (oriented rectangular bases, swept rotation), #226 (in-game bug reports)

## Goal

A unit of long rectangular bases (cavalry, bikes) can be turned and redirected in group mode
without the player fighting the controls. A rank of riders standing almost touching can wheel
to face a new direction in one step, and the result is legal. "Done" = the report's save
(below) can turn its Iron Tusk-Beast Riders 90 degrees in group mode and move off, with a test
for the turn on a packed rank of rectangles.

## Source

In-game bug report (#226), fetched 2026-10-28 from the list-server drop box:
`tools/list-server/reports/2026-09-29-154246-b87f631c-7244-4cbd-bbb3-9c8511e10933.json`
(gitignored; contains a player's name and save - don't commit it).

- Reporter: "vic", host, Windows 10, build `0.3.0+opr.3.5.1.git-0174787-20260913` (the
  v0.3.0 tag), filed 2026-09-29.
- Description, verbatim: "even with the rotating command, it's very hard to maneuver with cavalry"
- Unit: **Iron Tusk-Beast Riders**, 11 models (10 riders + a joined Orc Boss), Q5 D5, 245 pts.
- The report's `save` is replayable through Load Game - extract it with the snippet in
  `~/.config/fdg/bug-reports-howto.md`.

## Notes

- 2026-10-28: Owner hand-verified ("It works!"). The checkbox is renamed "Optimize formation for
  distance", with the tooltip "Will automatically change the order of units in formation to maximize how
  far the unit can travel." (owner's wording). Closed and archived.
- 2026-10-28: **Does the default fix the report? Verified against the engine.** A scratch test loaded
  vic's save and ran the riders through the engine's Done validation, `ValidatePaths`, with the real
  terrain (9 pieces), enemies (34 models) and friendlies (23), on an 8" Fast Advance:
  - Wheel 90 then move off 0 to 2.5": valid.
  - Wheel -90: valid. Moving off 1" or more is invalid because it rides into difficult terrain and
    hits the genuine 6" cap.
  - Wheel 45 or -45 then 0 to 2.5": valid.
  - Wheel 180: valid.
  Before, the wheel alone was legal, but moving off after it stacked the bases (the carried facing
  offset, plus bases turning to the travel direction). The save holds player data, so the committed
  test `TheReportedRank_WheelsAsOneBody_AndMovesOff_TheEngineAgrees` uses the riders' geometry only.
- 2026-10-28: **Owner: with the checkbox on, the spacing varied wildly; Ctrl+Wheel too.** Two causes:
  - **Opened shapes.** The "opened" candidates stretched by the minimum each exact heading needed, so
    the spacing slid with the cursor. They are gone. The only unlaid-out candidate is the current shape,
    unchanged, when it already clears.
  - **Ctrl+Wheel.** Picks were laid out by circumscribing radii, spacing a 35x60mm rider like a 70mm
    round base, in world axes. The movement catalog is now `GroupReform.LegalPartitions`, at true extents
    and 0.1" gaps. A pick is laid out by `GroupReform` across one heading: the unit's facing turned by
    the wheel, or the steadied travel direction with the checkbox on. The old layout stays as a fallback
    if a mixed-base pick can't lay out.
  So every shape, re-form or pick, sits 0.1" edge to edge at any heading. Tests:
  `EveryFormation_HasTheStandardSpacing_AtAnyHeading` and `Ctrl_Wheel_Catalog_UsesTrueExtents`. App 3020
  green, build clean, smoke exit 0.
- 2026-10-28: **Owner: keep the formation the same while moving it; make the solver an option.** Even
  steadied, the default re-form was awkward: a slight mouse move tipped the travel heading past the
  stacking angle and the ranks visibly re-spaced (screenshots 20:23:45 and 20:23:58). Now:
  - **Default: rigid body.** For the current shape, the drag slides the unit and Wheel/R turns it, with
    each base keeping its departing facing turned by the wheel. It cannot stack, and the shape never
    changes as the cursor moves. This is the mild fix for the report: wheel the riders, click, and move
    off cleanly. A 90-degree wheel costs vic's end riders about 5.4" of an 8" Advance.
  - **Opt-in.** The travel-heading plus re-form behaviour sits behind the panel checkbox "Face the move
    (re-form to turn)", off by default. It is the start of the "solve the best way" option the owner
    wants to try later.
  - **Formation morphs** (Ctrl+Wheel picks) and coherency repairs keep the shared travel heading,
    unchanged.
  - **Previews.** Live group facings are published per model, so the remote ghost preview and the
    standoff check use what the phantoms show.
  - **Verification.** App 3012 green, build clean, smoke exit 0.
- 2026-10-28: **Hand-test feedback: the re-form flickered.** Owner: correct but visually displeasing. The
  formation changed rapidly as the mouse moved, worst near the unit's centre and when dragging past max
  range. Three causes, three fixes:
  - **Heading spin near the centre.** The heading is the centre-to-cursor direction. Within 2"
    (`ReformSteadyRadius`) the last steady direction is held, and the wheel still turns it.
  - **Cursor-dependent assignment.** Slots are now assigned for the turn alone, in `Prepare` (cached
    per heading), so a moving cursor only slides the block. The previous heading's assignment is kept
    while it is within 0.5" of the new optimum.
  - **Near-tie formations.** Dragging out along the heading alternated 3-3-3-2 and 4-4-3 three times in
    20". Last frame's shape is now kept unless another reaches 1" further or cuts the worst travel by
    1".
  - **Tests.** `DraggingOut_TheShapeDoesNotFlicker` and `MovingTheCursor_NeverReshufflesWhoStandsWhere`.
    App 3012 green.
- 2026-10-28: **Implemented (re-form, respace folded in).**
  - **Cause, confirmed from the code and the save.** The hypothesis holds and there is a second half.
    The riders already stood about 20 degrees off their ranks' normal, so their 0.1" gaps were used up:
    any further turn stacked. A group step faces every model along the travel direction plus the wheel
    offset, while the wheel rotates the CENTRES rigidly. So only a drag straight along the formation's
    own front stayed clean. Worse, `_groupFacingAngle` survived a committed step, so after wheeling 90
    degrees and committing, dragging toward the new front faced the bases 90 degrees off it again. That
    is "even with the rotating command".
  - **Re-form.** New `GroupReform` (app, pure geometry). When the rigid step would newly stack (the
    #399 test) or the wheel's arc is over an outer model's budget, it builds candidates facing the step's
    heading:
    - the current shape opened up across, along, or both, just enough to clear (option 2);
    - every `FormationLibrary` row partition at the bases' true extents.
    Each is assigned by bottleneck assignment, tie-broken by least total travel, which keeps relative
    order. A joined hero is pinned to front-rank centre unless that costs over 1" more. The winner reaches
    furthest toward the cursor within every budget, then has the smallest worst travel. Its slots
    become the base of the existing two-array `PlanGroupMove` with no rotation, so terrain, enemy, table
    edge, impassible and #399 gates all still run on the result.
  - **Heading reset.** `_groupFacingAngle` now resets with `_groupRotation` on each committed step; the
    step keeps its own offset (#282).
  - **Panel.** "Turning: re-forming as 4-4-3" (or "current shape, opened") when it happens.
  - **Cost.** The candidate build (about 2.5 ms) is cached per whole-degree heading and per unit
    positions. The per-frame pick is about 1.5 ms (Release), and runs only while a step would stack.
  - **On vic's unit (8" Fast Advance).** Small turns (10 to 45 degrees) open the ranks slightly, worst
    rider moving 0.2" at most. A 90-degree turn in place becomes 4-4-3 with the worst rider moving 1.8",
    against 5.4" for the rigid wheel. Turning 90 and riding 6" fits inside the Advance.
  - **Tests.** `GroupReformTests` (11), on the reporter's own positions and bases. App 3010 and engine
    3381 (1 skipped) green, build clean, headless smoke exit 0.
  - **Not done, deliberately.** The rigid wheel still wheels whenever it fits, so a circle-based line can
    still be swung round as before. Placing the formation beyond following the cursor isn't optimised:
    the budget solve already pulls it back as far as it must. The AI's movement is untouched.
- 2026-10-28: Design fork settled with the owner: option 3 (re-form), option 2 folded in. Ready to build.
- 2026-10-28: Filed. Number 409 taken from `origin/master` at `0174787` after `git fetch`:
  index + archive max **405**, plus this clone's unpushed 406-408. No collision.
- 2026-10-28: The log records outcomes, not input, so it can't show the failed attempts. It
  shows the riders twice opening a move and leaving it without moving ("did not move -
  returning to Choose Action", once followed by backing out of the activation), and on other
  turns moving normally.
- 2026-10-28: **Every related fix was already in the reporter's build.** #282, #362, #150, #277
  and #399's group self-overlap gate are all ancestors of `0174787`, so this is not a missing
  fix.

## Leading hypothesis (unconfirmed - replay the save first)

The riders stand in two tight ranks: centres about 1.48" apart side by side and about 2.46"
apart front to back. That fits roughly 1.38" x 2.36" bases (35x60mm-class) facing forward,
with about 0.1" between neighbours.

A group step turns every model to ONE shared heading about its own centre while the centres
keep their spacing (#362, #399 A). Two same-heading rectangles 1.48" apart stay clear only
while 1.48 x cos(turn) >= 1.38, which is a turn of about 21 degrees. Past that, every base swings
into its neighbour. Before #399 that silently stacked the models. Since #399 the preview
flags them "Models Would Stack" and the engine refuses the step. So in 0.3.0 a packed cavalry
rank effectively cannot turn more than about 20 degrees per group step, which matches
"even with the rotating command".

The workarounds are all poor for 11 models: single-model mode one rider at a time, many small
group turns, or opening the ranks first.

## Other contributors to check during the replay

- **Swept rotation (#341).** Turning along the path is checked against terrain and other
  units at every leg, so a long base clips things a round base would clear.
- **The 1" enemy standoff and the forced-charge band (#206 / #334)**, measured
  base-to-base, bite earlier on long bases.
- **The joined hero's base**, if it differs from the riders', breaks a clean grid.
- **Coherency repair or table-edge clamp** pulling models while turned.
- **The rotate controls themselves** (wheel = mouse, R = keyboard, #362): whether a new player
  can find or understand them.

## Options (design fork - needs sign-off before building)

1. **Wheel the formation, not just the models.** A group turn rotates the centres about the
   group's pivot as well as each base, the way a real unit wheels, so spacing is preserved in
   the new frame and nothing stacks. The most natural fix; it changes how existing group turns
   look and move, and the swept path of the outer models gets longer.
2. **Respace on turn.** Keep turning in place but push the centres apart along the new
   width axis just enough to clear (re-pack through `FormationLibrary`). Keeps the pivot,
   but the unit's footprint grows and may clip terrain or break coherency.
3. **Auto-reform.** When a turn would stack, offer the nearest legal formation (#277 cycling)
   instead of just refusing. Least change to the mechanics, but more of a hint than a fix.

**Owner chose option 3 on 2026-10-28, with option 2 folded into it.** See Decisions.

## Decisions

- **2026-10-28 - option 3: re-form, with option 2 folded in (owner sign-off).** Wheeling
  (option 1) was rejected because it charges the outer models for the arc they travel. vic's
  front rank of six spans about 7.4", so a 90-degree wheel about its centre costs the end riders
  about 5.8", and about one end costs the far rider about 11.6", more than a Fast move. The
  engine checks each model's distance against its own allowance, so the outer models run out
  first and hold back the unit. OPR has no wheeling rule either: each model moves up to its
  allowance and may end facing any way. Re-forming pays the same per-model cost, but the
  arrangement is ours to choose, so the cost can be optimised:
  - **Slot assignment** that minimises the worst single model's distance (bottleneck
    assignment).
  - **Placement** of the formation, shifted where that lowers the worst distance.
  - **Formation choice**: for a 90-degree turn, two ranks of six can become a narrow column,
    far cheaper than keeping a wide rank.
  Option 2 (keep the shape, push centres apart along the new width axis) is the special case
  "same formation, nearest legal spacing". For a full 90-degree turn it moves the end riders
  about 2" outward, so it is a candidate the optimiser weighs, not a separate mode.
- **Constraints carried into the build.**
  - The re-form is part of the move, not a step before it, so no model pays for the turn twice.
  - Assignments stay predictable: keep the models' relative order unless a swap clearly cuts
    the worst-case distance, so riders don't shuffle at random on the table.
  - A joined hero gets a sensible slot (centre of the front rank by default).
  - Every candidate still passes the existing gates: per-model budget, the #366 / #399
    self-overlap rule, swept terrain (#341), enemy standoff, coherency.

## Outcome

**Done 2026-10-28, hand-verified by the owner** on vic's save.
- **Default group move.** The current shape moves as one rigid body: the drag slides it, Wheel/R
  turns centres and bases together, so it can never stack. A long-based unit can wheel and move off,
  confirmed by the engine's Done validation on the save itself.
- **Opt-in.** "Optimize formation for distance" faces the move and re-forms the unit to maximise reach
  (`GroupReform`: bottleneck assignment, steadied heading, sticky shape and assignment).
- **Spacing.** Every shape, re-form or Ctrl+Wheel pick, uses the same 0.1" spacing at true base extents.
- **Not done.** The AI's movement is untouched.
