# 386 — Dice panels freeze mid-tumble while play continues (hover-freeze captures a parked cursor)

**Status**: done (awaiting user confirmation while recording with OBS)
**Related**: #327 (the roll stack + hover-freeze affordance this constrains), #245 (dice caption design), #344 (linger slider - same "never touch the paced part" principle)

## Goal
Reported 2026-08-23: when (and only when) recording the whole screen with OBS, dice-roll timing goes
"whack" - the roll animation gets stuck tumbling while wounds/deaths are audibly resolving, and the
settled dice + result line appear several seconds late.

Root cause is not OBS itself: nothing in raylib/GLFW timing changes under capture, and every clock in
the presentation pipeline (engine `Task.Delay` pacing, active-slot animation, cascade track, sound
cues at dequeue) is wall-clock and shared - EXCEPT the #327 roll-stack hover-freeze, the one
mechanism that can hold a dice panel's tumble while everything else (audio, deaths on the table, the
engine's own pacing) keeps running. That is exactly the reported symptom.

The freeze captures a cursor that isn't hovering deliberately:
1. **A parked cursor gets caught.** While recording, the natural move is to park the mouse so it
   doesn't wiggle through the footage - often low-center over the table. The caption stack anchors
   bottom-center and grows UPWARD as rolls stack (up to ~56px from the top of the screen), so the
   stack grows to meet a stationary cursor. Once "hovered", panels never retire, the bounds stay
   tall, and the freeze self-perpetuates.
2. **A cursor that LEFT the window freezes it forever.** GLFW only updates the mouse position from
   in-window motion events, so `GetMousePosition()` returns the last in-window position after the
   pointer moves to another window/monitor (e.g. over to OBS to hit record). If that stale point
   lies where the stack grows, the stack freezes with nobody's pointer anywhere near the game.
3. **The freeze held the PACED part, not just the linger.** The engine waits out a roll's envelope
   in real time regardless of the pointer, so freezing a panel before its 30% settle point shows
   dice still rolling while the wounds they caused are already being narrated - the reported desync.

## What changed (app)
- **`PresentationPlayer`**: hover now freezes the LINGER only. A panel's `Elapsed` always advances
  to the end of its paced part (`RollPanel.PacedSeconds`, the engine's own wait on that roll), so
  the tumble settles and the result line lands in lockstep with the engine even under a parked
  cursor; only the retire countdown pauses. Mirrors #344's rule: display preferences scale the
  linger, never the paced part.
- **`RaylibRenderer`**: the hover test now requires `Raylib.IsCursorOnScreen()`, so a stale
  last-known position from a pointer that left the window entirely can never hold the stack.

## Notes
- 2026-08-23: implemented + tested. `DiceStackTests`: `Hovering_FreezesEveryPanel` re-pinned as
  `Hovering_FreezesTheLinger_ButTheRollStillSettles` (paced part advances to settle under hover;
  linger resumes on release) + new `Hovering_MidLinger_HoldsThePanelThere` (the re-read affordance
  is intact: a settled panel holds at full strength). The `IsCursorOnScreen` guard is raylib-bound
  and not unit-testable; it is a one-line conjunct at the only call site.
- User confirmation wanted: record with OBS again and watch a shooting exchange - dice should
  settle ~0.5s into each roll with the result line following, wounds/deaths narrating after, mouse
  parked or not.
