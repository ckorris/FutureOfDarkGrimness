# 318 — "Waiting on" line in the status HUD

**Status**: in-progress

## Goal
Restore visibility into what other players are doing while the local player waits (removed with the
old draggable "Outstanding Tasks" ImGui window). A second, smaller line under the top-center
`StatusHudOverlay` strip lists each *non-local* player's outstanding task ("<pip> Bob: Place Unit
Models"), player-color-coded, click-through, zero footprint when nothing is outstanding. Local
players' tasks are filtered out (the resolver panel already shows those); consequence: pure-hotseat
games never show the line, by design (agreed 2026-08-02).

## Notes
- 2026-08-02: Implemented. Engine d2aed58: `IFDGGame.LocalPlayerIDs` on both flavors +
  `LocalPlayerIDs_ExposedOnBothGameFlavors` test (suite 2594 green). App: `GuiOutstandingTaskDisplay`
  reworked into a read model (`GetWaitingOnOthers()`, local-ID filter, old ImGui Draw deleted);
  `StatusHudOverlay` grew the waiting lines (font 20, cap 3 + "+N more", dim prefix + player-colored
  name); renderer feeds it in `DrawStatusHud` and the commented-out old draw call is gone. Headless
  smoke exit 0. Remaining: hand-verify in a networked GUI game (line appears while the other player
  decides, disappears when resolved, absent in hotseat).
- 2026-08-02: Filed. Infrastructure survey: engine `OutstandingTaskLister` still streams
  `OutstandingTaskInfo` (works networked); `GuiOutstandingTaskDisplay` still subscribes and is wired
  end-to-end — only its draw call is commented out (`RaylibRenderer.cs` ~554). Plan: expose
  `LocalPlayerIDs` on `IFDGGame` (engine, exists privately), snapshot getter on the display class,
  thread local IDs through `GameGuiWiring`, render via extended `StatusHudOverlay`.

## Decisions
- Second HUD line over restoring the ImGui window (permanent chrome, screen cost), a console line
  (buried among log history), or change-toasts (it's a state, not an event).
- Filter local tasks: the line appears exactly when "why is nothing happening?" has the answer
  "someone else is deciding".

## Outcome
(open)
