# 300 — Dice panel: successes wear the category accent; Magic category; 25% larger

**Status**: done
**Related**: #245 (the caption strip and its category accents), #233 (the cast roll's dice beat)

## Goal

The dice caption strip color-coded the roll's *category* (an accent stripe + ATTACK/SAVE badge word)
but colored the *dice* on a separate axis entirely — every success green, every failure gray,
regardless of what was being rolled. Two color languages in one panel. Make the settled successes
wear the same accent as the stripe and the badge word so the panel says one thing, give the neutral
gray `Misc` bucket a real color, and scale the whole strip up 25% for readability.

## Notes

- 2026-07-28: Built, then two rounds of owner feedback, then GUI-verified by Chris.
  - Round 1: `Misc` gray -> amethyst; successes take `AccentFor(category)`; `PanelScale = 1.25f`.
  - Round 2 (owner): make `Defense` green, and give spells their own category. Blue freed up by the
    green swap went to the new `Magic`, which is where Chris expected spells to be all along.
  - Verified in the running app 2026-07-28 (Chris).

## Decisions

- **Amethyst for `Misc`, not orange.** Orange sits next to the ember `Offense` accent, so at a
  peripheral glance — the entire point of a 4px stripe — attack and misc rolls would read as the same
  category. The final four hues sit roughly 70-90 degrees apart: ember `(215,95,60)` attacks, green
  `(80,190,95)` saves, arcane blue `(85,170,225)` casts, amethyst `(160,115,210)` everything else.

- **Only the cast attempt is `Magic`.** A spell's damage roll stays `Offense` and the save against it
  stays `Defense`. The category tracks what the die decides, not what produced it — otherwise a whole
  spell's resolution would be one undifferentiated color and the damage/save reads would be lost.
  Documented on the enum itself, since that is where the next person will look.

- **New enum value appended (`Magic = 3`)**, so the existing serialized ordinals are unchanged for
  networked clients.

- **One scale knob, not 25% sprinkled everywhere.** Every panel dimension is a design number run
  through `Sc()`, so type, dice, chips, padding and badge grow together instead of drifting apart the
  next time someone resizes one of them. `BottomMargin` deliberately stays unscaled — it is the dock
  gap, not panel geometry. The `260px` side reserve became `SideReserve = Sc(260)` so the dice row
  still yields room to the now-wider badge column.

- **Roll-off dice left alone** (green won / yellow tied / gray lost). `RollOffBeat` carries no
  category and that panel has no stripe or badge, so there is nothing for it to match.

## Outcome

Shipped. Engine: `ERollBeatCategory.Magic` + `CastSpellStage` passing it, pinned by an added
assertion in `CasterRuleIntegrationTests.CastSpellStage_CastRoll_EmitsDiceRolledBeat`. Client:
`DiceOverlay` accent palette (Defense green, new Magic blue, Misc amethyst), successes drawing in
`AccentFor(beat.Category)` in both the realistic dice row and the probabilistic bar fill, the `CAST`
badge word, and `PanelScale = 1.25f`. Engine 2246/2246, headless smoke exits 0, GUI-verified.

**Open question deliberately left to playtest**: attack successes are ember-red while save successes
are green, so the attacker's good dice are red and the defender's good dice are green. That encodes
*whose roll it is* rather than *good/bad*, which is the intended reading but inverts a strong
convention. Revisit if it misreads in play.

**Not exercised end-to-end**: the smoke's default army has no caster and none of the five bundled
scenarios include one, so the `CAST` panel is covered by the integration test rather than a scripted
run. A caster scenario under `Scenarios/` would close that gap.
