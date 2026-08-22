# 293 — Banner announcing what a spell's effect actually did

**Status**: in-progress
**Related**: #274 (spell visuals), #275 (banner tiers), #033/#034 (spell effects), `SpellText`

## Goal
A successful cast currently announces the ROLL ("X cast Bless: rolled 5, needed 4+...") and plays the
#274 visuals, but nothing says what the spell DID. Emit one `EBannerTier.Notice` banner per resolved
spell naming the effect and the units it landed on — buff/debuff grants, forced moves, fatigue, target
marks, the conditional (morale-test) outcome, **and damage** (hit count + type).

Done when: every successful cast shows a Notice banner naming the effect and the affected units, ASCII
only, with integration tests covering a grant, a conditional and a damage spell.

## Notes
- 2026-07-26: filed from a play session ("when a spell goes off, some banner text of an appropriate
  tier should say what the effect did").
- 2026-07-26: design fork resolved with the user — **one Notice per spell** (not a Toast per target).
- 2026-07-26 (follow-up): the damage path, initially excluded, was added at the user's request for
  consistency — "at least how many hits and of what type".

## Decisions
- Past-tense effect text is a new `SpellText.DescribeApplied` sibling to the existing present-tense
  `DescribeEffect` used by the spell picker: the picker says "grants Rending (this round)", the banner
  says "Bless grants Rending to Knight Brothers (this round)".

## Outcome
Shipped 2026-07-26 (engine `fc2a1d9`). `SpellText.DescribeApplied` (past-tense verb phrase with the
affected units folded in) + `DescribeConditionalApplied` (one line covering both the failures and the
passes) + `JoinNames`; `CastSpellStage` emits them through a new `AnnounceEffect` helper on the
non-damage path and at the end of `ResolveConditionalSpell`, in a violet `EffectBannerColor` distinct
from the blue cast-result line. Six new tests in `CasterRuleIntegrationTests` (three pure-composer, three
through the real stage: buff, conditional, and a failed cast that must report nothing).

Damage banner added the same day (engine `1138461`) at the user's request, closing the last gap: the
report names the hit count and the type (AP + weapon rules) via a `DescribeHitModifiers` composer now
shared with the picker's advertisement, so the player is told the same thing twice in the same words.
It is emitted after any single-model pick and BEFORE `base.Enter` — the stage hands off to the child
pipeline and returns, so there is no "after" to announce from. That is also why this one phrase is
present tense ("deals") while its siblings are past: it precedes the dice it describes. Two more tests:
the exact banner text, and that the banner lands before the damage dice.

Surprise worth recording: `Announce`'s tier parameter defaults to `Notice`, so the cast success/failure
banners are Notices too — the effect banner cannot be identified by tier alone. Tests match on exact
text and assert the count is 1 (the load-bearing claim: report once per spell, not once per target).

Engine suite 2199/2199 green; headless smoke exits 0. Awaiting GUI hand-verify.
