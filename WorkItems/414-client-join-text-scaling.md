# 414 — Client join window text is not scaled properly

**Status**: filed (2026-10-28), not investigated
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

## Decisions

## Outcome
