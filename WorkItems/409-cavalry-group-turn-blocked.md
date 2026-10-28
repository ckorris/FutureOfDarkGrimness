# 409 — Cavalry is very hard to manoeuvre: a packed unit of long bases can barely turn as a group

**Status**: todo
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

Recommendation: option 1, possibly with 3 as the fallback when the wheel itself is blocked.

## Decisions

## Outcome
