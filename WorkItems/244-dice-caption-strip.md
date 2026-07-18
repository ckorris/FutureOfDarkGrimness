# 244 — Dice roll panel: bottom caption strip + target badge

**Status**: implemented (awaiting GUI hand-verification)
**Related**: #238 (attack/dice overlap — created the occlusion), #232 (casualty cascade — held dice linger through it), #222 (roll-off pacing), #056 (beat stream)

## Goal

Playtest feedback (2026-07-18): since #238 plays the attack animation WHILE the to-hit dice tumble,
the center-screen dice panel can block the action it narrates. Also: finding the target number
means reading the dim "needs X+" line, which vanishes when the dice settle — usually before you've
found it.

Design survey (principles + prior art: BG3 log/modal split, XCOM callouts, Blood Bowl dice tray,
subtitle lower-thirds) picked: **lower-third caption strip + ghost-on-overlap + stakes-based
split** (roll-offs stay centered). Notably `DiceOverlay`'s own doc comment always said
"bottom-center strip" — the implementation had drifted to 45% center.

## Notes

- 2026-07-18: Implemented, app-side only (no engine changes).
  - **Bottom strip**: `DiceOverlay` panels (realistic + probabilistic) dock bottom-center of the
    table viewport (18px margin) instead of 45% center. `DrawRollOff` deliberately stays centered —
    roll-offs are rare, decisive, and play with nothing else animating (stakes-based saliency).
  - **Target badge**: standalone "{threshold}+" chip (40px, gold on dark, gold outline) at the
    panel's left, visible from the first frame through settle — replaces the transient dim
    "needs X+" line. Result line shows dim "..." while tumbling, settled summary after.
  - **Ghost-on-overlap**: `AttackOverlay.ScreenBounds` (From/To pixel bbox + 48px pad) passed from
    the renderer; the strip eases to 35% alpha while the rect reaches it (units fighting at the
    bottom table edge), back to full when clear. Smoothed, render-thread static state.
  - **Stable width**: panel is sized from the settled result text up front (roll data is known at
    beat creation; the tumble is cosmetic) — no reflow at the settle instant.
  - **Tumble throttle**: rolling faces swap at 9Hz (deterministic per die + time slice) instead of
    every frame — no more strobe. Applied to roll-off dice too.
  - **Fade in/out** (`PresentationPlayer` owns the alpha, new out param on `TryGetActiveDice`):
    0.12s ease-in on a fresh roll, 0.35s ease-out over a non-held beat's tail or a held beat's
    linger tail; fade-in skipped when a new roll replaces a still-visible panel (no blink between
    back-to-back rolls). New `DicePanelAlphaTests` (4).
- Verified: engine 1691 green, app 382 green, headless smoke exit 0.

## Decisions

- **Ghost, don't dodge**: on overlap the strip fades in place rather than repositioning —
  consistent spatial anchoring beats occlusion-free-at-any-cost (players learn where to look).
- **Roll-offs keep center stage** (stakes-based split, BG3-style): only routine `DiceRolledBeat`
  rolls move to the strip.
- **Engine beat choreography untouched**: #238's attack/dice concurrency is a pacing win; the fix
  is layout, not sequencing.
- Deferred (explicitly, from the same design survey): redundant success encoding beyond color
  (green/gray dice), and an anticipation animation for the probabilistic bar's instant result.

**Verify by hand:**
- Shoot/melee: dice strip sits at the bottom-center; tracers/swings play unobstructed above it;
  the "4+" badge is readable before the dice settle; no panel width jump at settle.
- Fight near the bottom table edge: the strip ghosts to ~1/3 alpha while shots cross it, recovers after.
- Held dice: linger through the wound/death animations, then fade out (no pop); back-to-back rolls
  swap without a blink.
- Roll-off (game start / objective ties): still centered, dice tumble at a calm rate.
