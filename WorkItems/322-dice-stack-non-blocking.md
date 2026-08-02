# 322 — Dice rolls linger without blocking: held-by-default rolls stack instead of evicting

**Status**: in-progress
**Related**: #245 (dice caption strip), #275 (banner tiers — the held-track precedent), #232 (casualty
cascade — the other concurrent track), #238 (attack/dice overlap), #204 follow-up `ea91d68` (the commit
this supersedes), #222 (roll-off pacing, deliberately out of scope)

## Goal

A dice roll should be readable for longer than it blocks the game. Every `DiceRolledBeat` becomes a
**held** beat: the engine paces only its settle lead-in (600ms + 200ms per info-chip row) and moves on,
while the front-end keeps the panel on screen for several more seconds. Because rolls now overlap, the
front-end's single dice slot becomes a **stack** — a second roll appears *above* the first rather than
evicting it — and hovering the stack freezes every panel's timer so a player who wants to interrogate
the history can. Done when a shooting sequence spends ~2s in dice pacing instead of ~5.5-6s while every
roll stays legible for ~4s, and no panel is ever replaced mid-read.

## Notes

- 2026-08-02: Implemented, both suites green (engine 2616, app 917), headless smoke exits 0, GUI launches
  clean on the shootout scenario. Engine `6a6fdc6`; app side is the superproject commit that bumps it.
  Timings as shipped: lead-in 600ms + 200/chip (unchanged), panel lifetime = lead-in + 3.0s + 0.4s per
  info block, so a plain roll is legible for 3.6s while costing the engine 0.6s. Stack caps at 3 panels
  with depth alphas 1.0 / 0.62 / 0.42, both the depth dim and the attack-overlap ghost eased per panel.

  **GUI hand-verify checklist:**
  1. A multi-hit volley: to-hit panel stays up while the save panel appears ABOVE it, neither cut short.
  2. Overall pace: the shooting sequence should feel roughly 3x quicker in its dice while still readable.
  3. Hover the stack mid-sequence — every panel should freeze and brighten to full, then resume on exit.
     Watch for the nuisance case: a pointer resting in the bottom-center of the table during movement
     freezes the stack without the player meaning to.
  4. Three rolls up at once (a Blast/Rending volley): no panel should reach the status HUD strip, and the
     newest must always be fully on screen.
  5. Two weapons in a row where the first WHIFFS: the first attack's tracers should finish rather than
     being cut off when the second weapon fires (the concurrent attack list).
  6. Sound density: cues now land ~3x closer together on dice-heavy sequences.
  7. Roll-offs (game start) still dock to the same bottom anchor and draw over an empty stack.

- 2026-08-02: Filed. Baseline measured from the code: `DiceRoll` = 1800ms + 400ms per info block, and
  **every** emit site is non-held today, so a single weapon firing (to-hit with modifier chips, then two
  save thresholds) burns ~5.5-6s of pure dice pacing. `DiceRolledBeat` already declares `HoldLeadIn`
  (600 + 200/block) and `PresentationPlayer` already has a parked-dice linger path
  (`DiceHoldLingerSeconds = 2.5f`) — the mechanism exists and is simply unused.

## Decisions

- **Why held dice were switched off before, and why that no longer applies.** `ea91d68` (2026-07-11, a
  #204 follow-up) made save rolls non-held because a held beat "lingers until superseded", so a
  two-threshold volley played the first roll in ~600ms and let the last linger through the whole wound
  animation — uneven. That unevenness is a direct consequence of there being exactly one dice slot
  (`_activeDice`), where a second roll *evicts* the first. With a stack, the first roll is pushed up
  rather than replaced and each entry lives its own fixed lifetime, so every threshold gets the same
  on-screen time. The stack is the precondition that makes held dice viable; holding them without it
  would just re-introduce `ea91d68`'s bug.

- **Stack direction: newest on top, oldest keeps the bottom anchor** (owner's call, 2026-08-02). The
  alternative — newest at the fixed anchor, older ones pushed up — keeps the eye's landing zone fixed,
  which is the usual feed/terminal convention, but the owner chose a stable history with growth upward.
  Consequence: the *newest* panel is the one furthest from the anchor, so the overlay must guarantee it
  stays on screen — the layout drops the OLDEST entries when the stack won't fit the vertical budget.

- **Older entries stay full panels, only dimmed** (owner's call). The compact one-line variant was
  offered and declined, so the stack is capped tight (3) and the height guard above does the rest.

- **Uniform hold, not a dice tier system.** Tiering dice the way #275 tiered banners (some rolls stop
  play, some don't) would re-introduce exactly the uneven pacing `ea91d68` hit. Stack position already
  supplies the prominence gradient, so every roll is held and the flag stays available as an explicit
  per-roll opt-out ("this roll should stop the game") that nothing uses today.

- **The attack track has to become concurrent too.** Today the to-hit roll's 1800ms envelope always
  outlasts the longest attack animation (`VolleyMax` 1600ms), so a second `AttackBeat` can never truncate
  the first — `AttackBeatOverlapTests` pins exactly that. Drop dice pacing to 600ms and a *whiffed*
  attack (no saves, no wounds behind it) lets the next weapon's `AttackBeat` arrive at ~600ms and cut the
  previous tracers off mid-flight. So `_activeAttack` becomes a small list, mirroring `_cascading` — a
  regression caused by this change, therefore fixed in this slice rather than deferred.

- **Linger is a fixed per-entry lifetime, not "reset by activity".** The old parked-dice linger was reset
  every time any non-dice beat played, so a panel's lifetime depended on what happened to follow it.
  With a stack that indirection buys nothing and makes the layout unpredictable; each entry now lives
  `lead-in + linger` and ages out on its own.

## Outcome

_(pending)_
