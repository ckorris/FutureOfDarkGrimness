# 243 — Caster self-boost: spend own tokens on the cast roll, in the spell picker

**Status**: in progress (2026-07-18)
**Related**: #033 (Caster framework), #103 (friendly/enemy cast assist), #233 (cast dice-roll beat, built together), #191 A5 (Tactician casting)

## Goal
OPR lets the casting Caster spend additional spell tokens of their own to raise the cast roll's odds
(+1 per token), not just nearby friendly/enemy Casters (#103). Build it INTO the spell-choice step -
no separate opt-in prompt like the assist loop. "Done" = the spell picker offers a boost count with the
spell, boost tokens are spent with the cast cost (regardless of pass/fail), the net modifier stacks with
#103 assists, proven by integration tests and visible in a real game.

## Decisions (resolved 2026-07-18, with the user)
- **Dedicated request type, not `StringSelectionRequest`.** New `ChooseSpellRequest` -> reply
  (spell index, boost tokens). Same rationale as `ChooseAbilityEffectRequest`: the request type is the
  seam AI agents swap in at (`docs/ai-agent-plan.md` A4), and the Tactician currently dispatches the
  spell pick by sniffing the prompt prefix - this removes that.
- **GUI: one panel, live boost.** Selectable spell rows (highlight, not instant-commit) with the
  existing description subtext; disabled rows with a reason (unaffordable / no target); below: a
  [-]/[+] boost stepper, a live "Roll needed: X+" readout, a total-spend line, then Cast / Cancel.
- **Overspend allowed, gated by a context-aware useful cap.** Boost max = min(affordable,
  (base threshold - 1) + in-range enemy hinder tokens). Past guaranteed success the + keeps working
  only while enemy Casters within 18" hold tokens (hedge vs their -1s, which are prompted AFTER the
  caster commits); at the cap the UI says why ("no enemy casters in range" / "capped: N enemy tokens
  in range"). The request carries `HinderTokensInRange` + `BaseThreshold` so the UI computes this.
- **Boost spent with the cast cost** - after target selection (cancelling targets still spends
  nothing), regardless of pass/fail, like #103 assist tokens.
- **Open information:** a nonzero boost fires the same blue banner shape as a friendly assist
  ("X boosts their own cast of Y (+N)") so the enemy hinderers decide with the boost visible.
- **AI defaults conservative:** solo AI + Tactician pick their spell as before with boost 0; CLI EOF
  default = first castable spell, boost 0 (preserves piped/headless behavior). Smarter boost policy is
  a deferred refinement (same bucket as #103's smarter assist policy).

## Deferred (carry forward, don't silently cut)
- Smarter AI/Tactician boost policy (spend when a threshold shift is worth it, hedge vs known
  hinderers).
- Showing the running net modifier inside each #103 assist prompt (hinderers currently infer it from
  the boost/assist banners).

## Notes
- 2026-07-18: **Built** (with #233, same commit pair). Engine: `ChooseSpellRequest` (all army spells as
  rows - castable, or disabled with "need N tokens"/"no valid target" - + caster binding, token pool,
  `BaseThreshold`, `HinderTokensInRange`) replying `ChooseSpellReply(spellIndex, boostTokens)`;
  `CastSpellStage` spends cost + clamped boost together after target selection, announces a nonzero
  boost as a blue banner (open info for the #103 hinderers, who are prompted after), and folds
  `boost + assists` into one threshold shift; breakdown text now itemizes ("base 4+, self +2,
  assists -1"). `AiChooseSpellResolver` (first castable, 0 boost - preserves the old first-option
  default, benchmark-safe) + `TacticianChooseSpellResolver` (planner's value pick via labels, 0 boost,
  solo fallback); the Tactician's prompt-prefix spell dispatch in `TacticianActionResolver` removed.
  App: CLI `ChooseSpellResolver` (numbered castable rows, EOF = first spell + 0 boost, boost prompt
  capped at min(affordable, useful) with the hedge/cap notes) + `GuiChooseSpellResolver` (approved
  one-panel design: highlight-select rows, disabled rows w/ reason, [-]/[+] stepper with the + gated at
  the useful cap + orange why-note, live "Roll needed"/"Total spend", Cast/Cancel); registered in both
  registries + overlay; `docs/ResolverGuide.md` inventory row added. Tests: 3 new
  `CasterRuleIntegrationTests` (boost rescues a failed cast; boost spent on failure; boost stacks with
  enemy hinder + the request reports in-range hinder tokens) + the canned requesters ported to the new
  request. Engine suite 1693/0, full build clean, headless smoke exit 0, and a scripted `--scenario`
  CLI run drove a real +2-boosted cast end-to-end ("rolled 5, needed 2+ (base 4+, self +2); spent 4
  tokens (2 cost + 2 boost)"). **Awaiting GUI hand-verification** of the picker panel.
- 2026-07-18: Filed from user request alongside #233; design forks resolved with the user (one-panel
  picker, overspend hedge with useful-cap gating, dice beat + banner, engine changes authorized).

## Outcome
