# 303 — Instinctive (and the all-models family) vs a joined hero that lacks the rule

**Status**: todo
**Related**: #197 (Instinctive shipped here), #183 (grants made hero-inclusive), #006 (hero join), #304 (the Army Forge warning for the same mechanic)

## Goal

Decide, and then implement, what a joined hero WITHOUT a rule should do to that rule on its host unit.
The trigger is a rules reading (owner, 2026-07-30) that **Instinctive should still fire when a joined
hero doesn't have it** — today it does not. Done = Instinctive behaves per the source text, and the
broader question ("is the all-models gate the right default for every rule that uses it?") has an
explicit answer recorded here, with any re-gating applied rule by rule rather than by a blanket flip.

## Notes

- 2026-07-30: Filed. Current behaviour, verified in source:
  - Instinctive is authored `condition: allModelsHaveThisRule` at `Lifecycle_OnCapabilityQuery`
    (`FdgRaylib/Assets/Books/GdfRuleSupplement.json`).
  - `Condition.AllModelsHaveThisRule.Evaluate` (`FutureOfDarkGrimness/Rules/Definitions/Condition.cs`,
    ~line 106) **deliberately excludes a joined hero from the host's STATIC rules**:
    `hasRule = model.RuleDefinitions.Any(...) || unitGranted || (!isJoinedHero && unitStatic)`.
    So a hero that does not natively carry the rule fails the gate and **the whole unit loses it**.
  - The documented rationale is that the merge relocates the hero's own rules onto the hero model and
    "OPR heroes don't inherit the host unit's rules". That is a statement about INHERITANCE (the hero
    doesn't gain the rule), which is not the same claim as "the host's models stop having it".
- 2026-07-30: **The wording is a strong signal the gate is wrong for this rule.** Instinctive reads
  *"When this MODEL is activated, if it is able to shoot/charge an enemy unit, it must immediately
  attack the closest valid target..."* — per-model wording, authored as a unit-wide gate. A rule about
  what each model must do is not obviously a rule that a single non-carrier switches off.
- 2026-07-30: **Blast radius, measured.** `allModelsHaveThisRule` appears 50 times across **43
  supplement rules** (Ambush Re-Deployment, Entrenched, Guarded, Guardian, Mobile Artillery,
  Protection Feat, Reanimation, Reinforcement, Screened, Self-Repair, Steadfast, Sturdy, Versatile
  Defense, the -born family, ...) and **25 entries in `CoreRuleCatalog`**. `MostModelsHaveThisRule`
  (same file, ~line 170) carries the identical hero carve-out. Any change here is not local.
- 2026-07-30: Prior art for adjusting hero semantics: **#183** already made unit-held GRANTS
  (auras, buff spells) count for every living model *including* the hero, on the reasoning that every
  grant in the vocabulary targets the whole current unit. The static-rule case was left alone.

## Decisions

- (none yet — the fork below is the first thing to settle)

## Open fork to settle before building

1. **Per-rule re-gate (recommended starting position).** Leave `AllModelsHaveThisRule` alone and move
   the rules whose text is per-model onto a different gate. Cheapest, no blast radius, but needs a
   pass over the 43+25 to decide which are which, and possibly a new condition
   (`AnyModelHasThisRule` / `ThisModelHasThisRule`) that does not exist yet.
2. **Change the hero carve-out itself** — let a joined hero count the host's static rules for the gate
   (i.e. the hero stops *breaking* rules it doesn't have, while still not *gaining* them). One-line
   change, but it silently re-enables ~68 rule entries at once, including defensive ones where "all
   models" is load-bearing (Protection Feat's 5+ ignore covering a hero that didn't buy it).
3. **Split the question by valence** — a hero cannot switch OFF a positive unit rule, but also cannot
   benefit from it. Closest to intuition, most machinery: the gate would need to know which models the
   effect applies to, not just whether it fires.

Whichever wins, **#304's warning text depends on the answer** — build 303 first or accept that 304's
copy will need revising.

## Outcome

(open)
