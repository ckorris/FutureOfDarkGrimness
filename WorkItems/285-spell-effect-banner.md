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
Shipped 2026-07-26 (engine `fc2a1d9`). `SpellText.DescribeApplied` (past-tense verb phrase with the
affected units folded in) + `DescribeConditionalApplied` (one line covering both the failures and the
passes) + `JoinNames`; `CastSpellStage` emits them through a new `AnnounceEffect` helper on the
non-damage path and at the end of `ResolveConditionalSpell`, in a violet `EffectBannerColor` distinct
from the blue cast-result line. Six new tests in `CasterRuleIntegrationTests` (three pure-composer, three
through the real stage: buff, conditional, and a failed cast that must report nothing).

**Deferred deliberately** (signed off with the user): the DAMAGE path emits no effect banner — its
attack/dice/wound beats already narrate the result, and a banner there would have to fire *before* the
child pipeline resolves.

Surprise worth recording: `Announce`'s tier parameter defaults to `Notice`, so the cast success/failure
banners are Notices too — the effect banner cannot be identified by tier alone. Tests match on exact
text and assert the count is 1 (the load-bearing claim: report once per spell, not once per target).

Engine suite 2196/2196 green; headless smoke exits 0. Awaiting GUI hand-verify.
