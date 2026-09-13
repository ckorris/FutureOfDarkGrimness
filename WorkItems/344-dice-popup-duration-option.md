# 344 — Dice roll popup duration setting

**Status**: in-progress (implemented + tested; awaiting GUI hand-verify)
**Related**: #327 (the roll stack this scales), #245 (chip-stretched panels), #246 (Options panel)

## Goal
An Options-menu control for how long dice-roll popups stay on screen: today's timing as the default,
a third of it at the low end, twice it at the high end.

## Notes

- 2026-08-05: Implemented. `ViewSettings.DiceLingerScale` (1/3 .. 2, default 1) + an Options slider
  ("Dice popup time", `%.2fx`). `PresentationPlayer.RollPanel` multiplies the LINGER by it at
  construction. Engine untouched. Tests in `FdgRaylib.Tests/DiceStackTests.cs`; full suites green
  (2842 engine / 1101 app).

## Decisions

- **Only the linger scales, never the paced part.** A roll panel's lifetime is
  `paced + linger`, where `paced` is the engine's own wait on `PresentationBeat.NominalDuration` — the
  window during which the dice are still tumbling and the engine is blocked. Scaling that would retire
  the panel *while the roll it depicts was still being paced*, i.e. the dice would vanish mid-tumble
  (and on a networked client, out of step with a host still waiting). So the knob moves the part that
  is purely "how long you get to re-read it", which is what the setting is for. Consequence worth
  knowing: at the 1/3 setting the total on-screen time is not a third — it is `1.8s + 1.0s` rather
  than `1.8s + 3.0s`. That is the correct behaviour, not a shortfall.

- **Read at panel construction, not per frame.** Moving the slider mid-roll does not yank the panel
  already on screen out from under a reader; the next roll picks up the new value.

- **Clamped inside `RollPanel`** as well as by the slider: the field is public and a stray 0 would
  make panels vanish the frame they appeared.

- **Session-scoped, not persisted.** Deliberately matches every other `ViewSettings` flag (grid,
  labels, fireworks); `UserConfig` holds lobby/host settings, not display toggles. If the owner wants
  it to survive a restart, that is a one-line addition to `UserConfig` — flagged, not done.

## Outcome
_(pending)_
