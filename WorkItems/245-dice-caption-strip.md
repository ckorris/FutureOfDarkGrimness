# 245 — Dice roll panel: bottom caption strip + target badge

> Filed and built as **244**; renumbered 244 -> 245 at push time (reconciliation 16 — a parallel
> session's merged caster self-boost item took 244). Commit messages were rewritten pre-push;
> nothing published references the old number.

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

- 2026-07-18 (v3, glance metadata - ENGINE + app, engine change user-authorized): the roll panel now
  answers "what kind of roll, who, why this number, what procced" at a cursory look.
  - **Engine** (submodule `57b9dd9`; originally `753cdeb`, rebased onto the caster work at push): `DiceRolledBeat` gains optional `ERollBeatCategory`
    (Offense/Defense/Misc), `Context` ("Warriors -> Heavy Gunners"), `ModifierTags`
    (["Quality 4+", "Stealth -1"]) and `ProcTags` (["Furious +2 on 6s"]) - all display-ready
    strings composed at the emitting stage, serialized for networked clients. Each info block
    stretches the beat +400ms (held lead-in +200ms) so there is time to read it (user request).
    - `DetermineHitRollStage` + `MoraleUtilities` switch `EvaluateAll` -> `EvaluateAllNamedLive`
      (#204's named twin, behavior-identical) to attribute threshold modifiers.
    - `RollToHitStage` presents its dice beat AFTER the hit-roll-complete evaluation (pure
      computation; RNG order and grant spends unchanged) so proc chips ride the beat; also
      composes `SaveModifierTags` naming Shielded/Thrust/Fortified for the save side.
    - Save arithmetic chips ("Defense 4+ | AP 2 | Cover +1") compose in
      `DetermineSaveRollsNeededStage`; save beats stamp Defense + "X saves" context.
    - Bane Re-roll + Regeneration beats stamp Defense + defender context. Unmodified rolls carry
      NO chips (no noise, no stretched beat). `DiceBeatGlanceMetadataTests` x10.
  - **App**: category = 4px accent stripe down the panel's left edge (ember=attack,
    steel=save, neutral=misc) + the word ATTACK/SAVE under the target badge (redundant,
    colorblind-safe channel); dim context line under the header; neutral modifier chips row; gold
    proc chips row; top-face successes get a gold rim when procs fired; probabilistic panel gets
    the same treatment minus the rim. Held panels with chips linger +0.4s per info block
    (`PresentationPlayer`; new linger test).
  - Deferred, recorded: world-link pulse on the participating units (redundant with tracers/save
    pings today); per-die reroll animation (histogram beats have no die identity - the engine's
    existing chained "Bane Re-roll" beat is the vocabulary for now); proc chips only show rules
    that FIRED (a Furious roll with no 6s shows nothing - "could have procced" needs a pre-roll
    rule query that doesn't exist yet).
- 2026-07-18 (v2, same-day playtest feedback): two revisions.
  - **Roll-offs join the bottom strip.** The objective-count roll (DiceRolledBeat, bottom) is
    immediately followed by the first-turn roll-off (was: centered) — back-to-back rolls hopping
    between center and bottom read as jarring. Placement continuity beats stakes-based prominence;
    the v1 "roll-offs stay centered" decision is REVERSED. `DrawRollOff` bottom-docks and gains a
    progress-driven fade envelope (in 6% / out last 10% of the beat) so tie re-rolls read as fresh
    rolls. No ghost logic (nothing else animates during a roll-off), no player-side state needed.
  - **Bottom-left table toolbar restacked vertically.** The 5-column layout (~600px wide) reached
    into the caption zone. Now a single thin column (~140px, widest-label width) hugging the left
    edge — keeps the learned corner anchor rather than relocating, and clears the centered strip
    on any window size. Button order/behavior unchanged (`TableTooltipOverlay`).
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
- **Roll-offs keep center stage** — REVERSED same day (see v2 note): placement continuity for
  back-to-back rolls beats stakes-based prominence; everything dice now lives in the caption zone.
- **Toolbar keeps its corner, changes shape**: restacked vertical rather than moved to another
  corner — preserving the learned bottom-left anchor while freeing the caption zone.
- **Engine beat choreography untouched**: #238's attack/dice concurrency is a pacing win; the fix
  is layout, not sequencing.
- Deferred (explicitly, from the same design survey): redundant success encoding beyond color
  (green/gray dice), and an anticipation animation for the probabilistic bar's instant result.

**Verify by hand (v3 additions):**
- Shoot with a plain unit: ember stripe + ATTACK word + "A -> B" context; NO chips (unmodified roll).
- Shoot with Furious (or similar): gold "Furious +N on 6s" chip; rolled 6s that hit get a gold rim;
  the beat visibly lasts longer than a plain roll.
- Save roll into cover / with AP: steel stripe + SAVE word + "X saves" context + "Defense 4+ | AP n |
  Cover +1" chips matching the badge arithmetic.
- Morale with a modifier (e.g. spell debuff): "Quality 4+ | <rule> -1" chips; plain morale = no chips.
- Networked client: same chips/colors on the client's panel.

**Verify by hand:**
- Shoot/melee: dice strip sits at the bottom-center; tracers/swings play unobstructed above it;
  the "4+" badge is readable before the dice settle; no panel width jump at settle.
- Fight near the bottom table edge: the strip ghosts to ~1/3 alpha while shots cross it, recovers after.
- Held dice: linger through the wound/death animations, then fade out (no pop); back-to-back rolls
  swap without a blink.
- Roll-off (game start / objective ties): bottom-docked like the dice strip — the objective-count
  roll then the first-turn roll-off appear in the same place; fades between tie re-rolls.
- Bottom-left toolbar: single thin vertical column; all buttons still work; no overlap with the
  strip even on a narrow window / wide roll.
