# 285 — Banner announcing what a spell's effect actually did

**Status**: in-progress
**Related**: #274 (spell visuals), #275 (banner tiers), #033/#034 (spell effects), `SpellText`

## Goal
A successful cast currently announces the ROLL ("X cast Bless: rolled 5, needed 4+...") and plays the
#274 visuals, but nothing says what the spell DID. Emit one `EBannerTier.Notice` banner per resolved
spell naming the effect and the units it landed on — buff/debuff grants, forced moves, fatigue, target
marks, and the conditional (morale-test) outcome. The damage path is excluded: its attack/dice/wound
beats already narrate the result.

Done when: casting a non-damage spell shows a Notice banner naming the effect and the affected units,
ASCII only, and an integration test asserts the banner text for at least a grant and a conditional.

## Notes
- 2026-07-26: filed from a play session ("when a spell goes off, some banner text of an appropriate
  tier should say what the effect did").
- 2026-07-26: design fork resolved with the user — **one Notice per spell** (not a Toast per target).

## Decisions
- Past-tense effect text is a new `SpellText.DescribeApplied` sibling to the existing present-tense
  `DescribeEffect` used by the spell picker: the picker says "grants Rending (this round)", the banner
  says "Bless grants Rending to Knight Brothers (this round)".

## Outcome
