# 175 — Fear vs Fearless joined-hero gating asymmetry (rulebook check)

**Status**: DONE 2026-08-02 (awaiting GUI hand-verify). Rulebook check 2026-07-22 — the asymmetry is
**correct as designed**; verdict below. The adjacent gap it uncovered (joined hero's Fear never fires)
was signed off and fixed 2026-08-02.
**Related**: #021 (Fear/Fearless implementation), #091 (morale core), #093 (all-models rule gate),
#006 slice F / #183 (hero per-model rule carriage).

## Goal
Determine from the rulebook whether the engine's gating asymmetry is correct:
- **Fear(X)**: `Condition.Always` — no all-models gate.
- **Fearless**: `Condition.AllModelsHaveThisRule` — suppressed unless every model (incl. joined hero) has it.

## Verdict (v3.5.1 core rules, exact quotes)
Source: GF - Core Rules v3.5.1 PDF (onepagerules.com, dated 2025-12-01; matches the v3.5.1 baseline
#031 verified against).

- **Fearless**: "When a unit **where all models have this rule** fails a morale test, roll one die.
  On a 4+ it counts as passed instead." -> The all-models gate is explicit rulebook text.
  `AllModelsHaveThisRule` is CORRECT: a Fearless hero joining a plain unit does not grant it, and a
  plain hero joining a Fearless unit breaks it.
- **Fear(X)**: "**This model** counts as having dealt +X wounds when checking who won melee."
  -> Per-model wording, no all-models clause (contrast Stealth, whose text says "units where all
  models have this rule"). So NO gate on Fear is CORRECT; the asymmetry is faithful, not a bug.
- Checked the OPR community wiki Rules FAQ and searched for official FAQ entries: no ruling
  contradicts the above; no mixed-unit/hero clarification exists for either rule.

## Adjacent gap discovered (not the original question)
A joined hero's Fear(X) currently contributes **nothing** to the who-won-melee check:
- Hero rules ride the hero MODEL after join (`HeroJoinResolver` #006 slice F, hero.Model.AttachRuleDefinition).
- `DetermineMeleeWinnerStage.SumExtraMeleeWounds` dispatches `RuleParticipant.Actor(actor)` with **no
  models list**, so `RuleEvaluator.CollectTagged` never collects per-model rules at
  `Melee_OnMeleeResolution` — only unit-static + granted rules fire.
- Per the per-model rule text above, a hero model with Fear(X) inside a unit should add +X to its
  unit's total. Today: native-unit Fear fires (+X, correct); hero-only Fear fires nothing (gap);
  hero + host both Fear fires host's +X only.
- Candidate fix (needs sign-off - submodule): pass the unit's living models (AnyOwner scope) at the
  melee-winner dispatch site so the hero-model copy is collected; dedup keeps (X)-rules per bearer, so
  host-static + hero-model Fear would sum (+X each), matching the literal per-model reading. Open
  sub-question with no official ruling found: whether N ordinary models with unit-wide Fear(X) should
  count +X once (current engine treatment of unit-level rules) or +X per model; recommend keeping
  once-per-source (unit rule = one source, hero model = one source).
  **-> SHIPPED 2026-08-02 exactly as sketched, once-per-source included. See the 2026-08-02 note.**

## Notes
- 2026-08-02: **Adjacent gap FIXED (user sign-off in session); engine 2561/0, app 872/0, build clean,
  headless smoke exit 0.** One line of behavior change: `DetermineMeleeWinnerStage.SumExtraMeleeWounds`
  now dispatches `RuleParticipant.Actor(actor, models: HeroStatRules.LivingModels(actor))` — the same
  living-models shape `MoraleUtilities`, `AssignWoundsStage`, `DetermineStrikeOrderStage` and the
  hit-roll stages already use, so this site was the outlier, not a new pattern. Blast radius is exactly
  Fear: `RuleFireLint` restricts `Melee_OnMeleeResolution` to `ExtraMeleeWoundCount`, and Fear is the
  only rule in the catalog or the shipped books that emits it.
  - **The open sub-question was resolved as recommended (once-per-source)** and it needed no code: the
    dedup keys attachments per (unit, ResolvedRule) and exempts (X) rules from the argument-less
    no-stack pass, so a host unit's Fear and a hero model's Fear are two sources that sum, while a
    unit-level Fear is collected once from `unit.RuleDefinitions` no matter how many models it covers.
  - **Living models only**: a hero killed during this melee contributes nothing, consistent with every
    other living-models dispatch site. Not a rulebook ruling — no text found either way — but it is the
    engine's existing convention and is now pinned by a test rather than left implicit.
  - +4 tests in `FearRuleIntegrationTests`, all through the REAL stage. Two reproduce the gap (they
    fail on the pre-fix dispatch: hero-only Fear, and host+hero summing to +3); two guard the fix from
    over-reaching (a unit-level Fear on a 3-model unit still counts +1, not +3; a dead carrier adds
    nothing) and pass either way by design.
- 2026-07-22: Started + completed rulebook check. Engine state confirmed: `CoreRuleCatalog.Fear` fires
  on `Always` (Melee_OnMeleeResolution, ExtraMeleeWoundCount(Arg(0)), summed per side in
  `DetermineMeleeWinnerStage`); `CoreRuleCatalog.Fearless` gated on `AllModelsHaveThisRule`
  (Morale_OnMoraleTestComplete reroll; the condition self-evaluates over unit.Models incl. the joined
  hero, so it works at every morale dispatch site). Downloaded GF Core Rules v3.5.1 and quoted both
  rules verbatim; wiki FAQ checked. Existing `FearRuleIntegrationTests`/`FearlessRuleIntegrationTests`
  have no joined-hero cases; `AllModelsRuleGateIntegrationTests` covers the Fearless-style gate via
  Stealth with hero variants.

## Outcome
Two questions, two answers. The one asked — is the Fear/Fearless gating asymmetry a bug? — is **no**:
v3.5.1 gates Fearless on "a unit where all models have this rule" and writes Fear(X) per model, so the
engine's `Always` vs `AllModelsHaveThisRule` split is faithful and no code changed for it. The one the
check uncovered — a joined hero's Fear(X) contributing nothing, because the melee-winner dispatch
passed no models list while hero rules ride the hero model — is fixed: that site now passes the unit's
living models (AnyOwner), host and hero Fear sum as separate (X) sources, and unit-level Fear still
counts once. 4 tests in `FearRuleIntegrationTests`. Nothing deferred; GUI hand-verify outstanding.
