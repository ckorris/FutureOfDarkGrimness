# 260 — Five bundled-book rule names differ from the catalog only by case

**Status**: done — **not a defect**; the premise was wrong. The real bug it exposed was in #259's glossary,
fixed here.
**Related**: #259 (filed this, and carried the actual bug), #100 (made rule resolution case-insensitive)

## Goal (as originally filed)

Make five book rule names that differ from the catalog only by case resolve at army load, since they were
believed to be silently inert in play.

## Outcome

**They already resolve. Nothing was inert.** `RuleResolver` backs its registry with a
`Dictionary<string, SpecialRuleDefinition>(StringComparer.OrdinalIgnoreCase)` and has since engine commit
`390ae86` ("#100: case-insensitive rule-name resolution"), so `Bane in Melee` resolves to the catalog's
`Bane in melee`. Verified empirically against `CoreRuleCatalog.CreateResolver()` for all five names before
changing anything. The three-way fork this item was opened to decide (rename / alias / case-insensitive
resolution) is moot — option 3 shipped two years of commits ago.

**What actually was broken:** #259's `RuleGlossary` was written case-SENSITIVE *on purpose*, on the
strength of a stale doc comment, so the Forge tooltip told the user those five rules were "not enforced by
the engine and do nothing in play" — false, and worse than showing nothing. Fixed by matching the
resolver's comparer (`OrdinalIgnoreCase`), which also makes a book definition override a core rule of
differing case exactly as `RegisterOrReplace` does at load.

Regression guard added (`RuleGlossaryTests.Describe_IsSilentExactlyWhenTheResolverCannotResolve`): for a
sample spanning casings, the divergent names, and genuine unknowns, the glossary must be silent exactly
when `RuleResolver.TryResolve` fails. A tooltip can no longer contradict what the engine does.

Corpus coverage restated with the right comparer: **94.3%** of book rule references resolve to a
description (1825/1936), up from the 93.8% measured case-sensitively.

## Decisions

- **The source of the error was trusting a comment over the code.** `CoreRuleCatalog.UnitAura`'s doc block
  still says "the resolver is case-sensitive" — written in `564e37b`, before #100 changed it. It is the
  only place in the tree that asserts case sensitivity, and it is wrong. Left unfixed here because it is
  submodule text and this session has no engine-change authorization; see the follow-up below.
- **Matching the resolver's comparer is the invariant, not case-insensitivity per se.** The glossary's job
  is to describe what will actually happen in play, so it should track `RuleResolver`'s lookup semantics
  whatever they are. The new test pins the relationship rather than the current answer.

## Follow-ups (not done here)

1. **Stale comment in `CoreRuleCatalog.UnitAura`** (submodule, comment-only): delete the "the resolver is
   case-sensitive" clause. It cost this session a wrongly-filed work item and a shipped-then-fixed UI bug.
2. **`SpecialRuleRegistry.GetPickerEntries` uses `StringComparer.Ordinal`** for its override-by-name
   dictionary while army load overrides case-insensitively. A book definition whose casing differs from
   core would show as two entries in the freeform builder's rule picker but override at load. Pre-existing,
   cosmetic, engine-side — worth its own item if the picker ever matters more.
