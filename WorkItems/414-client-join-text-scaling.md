# 414 — Client join window text is not scaled properly

**Status**: implemented (2026-10-28); awaiting owner GUI verify (fullscreen + windowed)
**Related**: #405 (lobby buttons), HostModal (the sibling dialog that scales)

## Report (Chris, 2026-10-28)

The text in the Client join window (main menu -> Client) is not scaled properly. No screenshot yet;
grab one when picking this up, and check it both fullscreen and windowed.

## Goal

The Client window's text scales the same way the Host window's does, at any window size and UI scale,
with nothing clipped or overflowing. "Done" = owner-verified in the GUI at fullscreen and windowed.

## Lead (not yet confirmed)

`HostModal` sets `ImGui.SetWindowFontScale(... * scale)` at several points through its draw
(around lines 101-154). `ClientModal` sets it only in one helper (around line 375), so most of its
text probably draws at an unscaled 1.0. Compare the two before changing anything.

## Notes

- 2026-10-28: Owner screenshot after the first fix: "much better", but "Players" was truncated, a
  blank column showed at the right, and "Your Name" sat right of its input. Fixed columns are now
  measured from header + widest value (`WidestText`) at the current font scale. The blank column is the
  per-row JOIN button column, now headed "Join" and sized to the button. The table is placed at `pad`
  so it is centred. The "Your Name" label now comes before the input. App build green.

- 2026-10-28: Lead confirmed. ImGui keeps a window's `SetWindowFontScale` across frames, and
  `ClientModal` only set it inside `DrawLabeledInput` (Direct Connect tab). The Server Browser tab, the
  default whenever a list server is configured, therefore drew its name field, tab labels, table, status
  line and buttons at 1.0 until Direct Connect had been opened once. Its column widths and button
  heights scaled regardless, so text and layout disagreed (overflowing columns in a small window,
  undersized text in a big one). The password popup is a separate window with fixed 260/120px sizes,
  so it never scaled. Fix: `SetWindowFontScale(scale)` once right after the dialog's `BeginChild`; the
  popup sets its own scale and scales its widths. `HostModal` gets the same up-front call so its title
  no longer relies on the previous frame's leftover scale (a first-frame flash). App build green; no
  screenshot taken (desktop control not granted).
- Possible follow-up, not done: both modals derive `scale` from `min(w/1920, h/1080)`, but fonts are
  baked per monitor at startup and the lobby uses `WindowToMonitorScale`. On a 4K display fullscreen the
  modals therefore apply 2x on top of a 4K-baked font. The owner reports Host looks right, so this was
  left alone; revisit if both modals look oversized at 4K.

## Decisions

## Outcome
