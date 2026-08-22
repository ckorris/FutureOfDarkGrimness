# 034 — Spell content + the primitives & conferred rules it needs

**Status**: in-progress — **Part 1 (spell-targeting primitives) COMPLETE 2026-06-28**: single-model (#3) + multi-unit (#4) damage (2026-06-25), forced-enemy-movement (#6) + conditional/triggered (#5) (2026-06-28). **Part 3 partially authored 2026-06-28** (implemented-effect subset, local-only). Remaining: Part 2 (conferred rules, #100's catalog) + the rest of Part 3 (keyword-buff spells, blocked on Part 2)
**Related**: #033 (Caster framework — the runway), #100 (conferred-rule catalog — coordinate), #101 (granted-rule/aura dispatch), #087 (custom-rule authoring), #059 (per-army STJ embedding)

## Goal
Make the per-faction spell sets actually playable. #033 shipped the framework (token economy, cast loop,
damage + buff/stat-modifier effect primitives, an army-builder spell editor); #034 fills in everything a
real army's spell list needs to work end-to-end.

## Scope — three distinct parts (different rules apply to each)
1. **Spell-targeting primitives** (engine, committable, collision-free — lives in `CastSpellStage`, untouched
   by other branches). From the #033 survey, ranked by spells unlocked:
   - **#3 single-model damage (~22 spells)** — ✅ **DONE 2026-06-25** (this branch). Resolve a damage spell
     against one chosen model "as a unit of [1]".
   - **#4 multi-unit damage (~19 spells)** — ✅ **DONE 2026-06-25** (this branch). Damage runs the
     save→wound pipeline once per selected unit via the looped child `ResolveSpellDamageStage`.
   - **#6 forced enemy movement (~1 spell)** — ✅ **DONE 2026-06-28** (this branch). `Effect.TriggeredMove`
     wired into `CastSpellStage`'s non-damage path via `OperationExecutor`; the move request routes to the
     rule's **bearer** (the caster), so a "reposition an enemy unit" spell is caster-directed, not victim-chosen.
   - **#5 conditional/triggered (~4 spells)** — ✅ **DONE 2026-06-28** (this branch). `Effect.MoraleTestThen(OnFailure)`:
     `CastSpellStage` runs a per-target morale test and applies the on-fail effect only on a fail. Covers
     Deep Hypnosis (on fail → caster moves the enemy, reusing #6's `TriggeredMove`) and Terrifying Fury
     (on fail → new `Effect.ApplyFatigue` via `FatigueUtilities`). **Part 1 is now complete.**
2. **Conferred-rule implementations** (Evasive, Crack, Shatter, Lacerate, Quick Shot, Melee Evasion,
   Unwieldy, Unpredictable Shooter, faction "Boost" rules, Unstoppable-when-shooting, …). **#100's
   territory** — the general conferred-rule catalog is built there. **Coordinate before adding catalog
   rules here** to avoid repeating the #100/#101 parallel-build collision (reconciliation 6).
3. **Actual per-faction spell JSON** (the real army-book spell lists). **COPYRIGHTED — author locally,
   never commit.** The committable work is the *machinery* (parts 1–2) + original example fixtures; the
   real content stays out of the repo.

## Decisions
- **Single-model targeting is a `TargetSelector` property, not an effect** (2026-06-25): added
  `TargetSelector.SingleModel` (default false). A damage spell with it set picks a model after the unit is
  chosen and confines wounds via the existing `IndividualTargetResult` (Takedown's mechanism) — reuse, no
  new wound-allocation path. Pairs with `MaxCount = 1`. Buff effects ignore it.

## Deferred (recorded — not silently cut)
- **Army-Builder spell-editor toggle for `SingleModel`** — the engine primitive + JSON field exist, but the
  GUI editor doesn't yet expose the flag (author single-model spells via JSON for now). App-side + needs
  GUI hand-verification; fold into a later app slice.
- **Part 1 spell-targeting primitives are all done** (#3/#4/#5/#6). The remaining low-priority primitives the
  #033 survey listed beyond Part 1 (Heal/Summon/terrain-status/random-branch at the spell level) are
  explicitly NOT needed — no castable spell requires them (see #033 survey "Not needed at the spell level").

## Notes
- 2026-06-28: **Part 2 scoped (coordinating with #100) + collision found + a safe non-engine win taken.**
  Cross-referenced the 139 skipped spells against #100's primitive catalog:
  - **⚠️ COLLISION — #034 Part 1 already built two #100 catalog primitives** (the parallel-build hazard the
    ledger keeps flagging): **#034 #5's `Effect.ApplyFatigue` == #100 #9** ("apply fatigue to a target —
    Terrifying Fury") and **#034 #6's caster-directed `Effect.TriggeredMove` == #100 #18** ("move-the-target
    — Deep Hypnosis"). Recorded in #100's ledger too; **#100 must adopt these, not re-build them**, and the
    merge must reconcile (both built on the same engine master, so the effect/op names should line up).
  - **Conferred-rule authoring (Part 2 proper) is #100's catalog territory** — the 139 skips map to: ~28
    combat-kind-scoped grants ("X when shooting/in melee") → **#093** combat-kind condition (+#100 #1);
    Evasive/Melee Evasion/self-modifiers (~25) → #100 "pure data" catalog rules; faction "Boost" rules (~20)
    → #100 #3 (conditions done, effects to author); the rest → Tier-C #100 primitives (#5/#6/#10/#13/#14/#15/
    #20/#23/#102). Per the 034 "coordinate before adding catalog rules here" rule, **none of these were built
    on this branch.**
  - **Safe non-engine win taken now:** the generator over-skipped *every* "gets X once". Refined it to emit a
    plain grant of an already-implemented rule (Furious/Fearless/Stealth/Regeneration/Relentless/Surge) as
    `Effect.AddRule` — works today via #100 #1 (granted-rule read-back, present on this branch). +7 spells
    (143 emitted, 139 skipped). Combat-kind-scoped grants stay skipped (the scope word keeps them from
    matching). Verified: Blood Brothers' Furious-grant spell loads + offers up-to-2 friendly targets + casts
    (headless exit 0). Local content only — never committed.
- 2026-06-28: **Part 3 partially authored (implemented-effect subset) — local-only, never committed.** A
  generator (`gen_spell_armies.py`, kept beside its output) parses the off-repo corpus
  (`/home/chris/Projects/GDF Armies/Special Rules and Spells by Army.md`) and emits one `<Faction> -
  Spells.fdgarmy` per faction into `/home/chris/Projects/GDF Armies/generated-spell-armies/` (outside the
  repo by design — the real content is never committed). Only spells whose effect the engine implements are
  emitted; everything else is logged to `_skipped.md` with a reason. This pass: **47 faction files, 136
  spells emitted** (121 damage incl. single-model/multi-unit, 11 numeric stat-modifier, 4 conditional
  morale-test), **146 skipped** — almost all "target gains keyword X" buffs blocked on Part 2 (#100). Damage
  spells carrying an unimplemented weapon rule keep the (working) damage and drop the rule (noted). Verified:
  a generated file loads in the headless app (`Loaded 'Soul-Snatcher Cults (spells)'`), spells appear in the
  cast menu, and casting runs the real pipeline (exit 0). Re-run the generator after Part 2 to absorb the
  keyword-buff spells.
- 2026-06-28: **Conditional/triggered primitive (#5) done — Part 1 complete** (engine `034-spell-content`,
  commit `60d8ae9`). Characterized the ~4 spells from the off-repo corpus
  (`/home/chris/Projects/GDF Armies/Special Rules and Spells by Army.md`, copyrighted — read for shape, never
  committed): 2 distinct spells, one shape — *pick enemy unit(s); each takes a morale test; on a fail, apply
  effect E*. Deep Hypnosis (E = caster moves it ≤6") and Terrifying Fury ×3 armies (E = becomes Fatigued).
  Built `Effect.MoraleTestThen(Effect OnFailure)` (stage-enacted in `CastSpellStage` because the test is async
  + rolls live state — `MoraleUtilities.TakeMoraleTest` per target, on-fail effect applied via a shared
  `ApplyEffectToTarget` seam) + `Effect.ApplyFatigue` (executable op → `IOperationServices.ApplyFatigue` →
  `FatigueUtilities.ApplyFatigued`, the one fatigue authority). On-fail `TriggeredMove` reuses #6's
  caster-directed move. `SpellText` describes both. 3 new tests (fail→move, fail→fatigue, pass→no-effect;
  Quality-6 target + fixed face 4/6 gives deterministic cast-passes-morale-fails / both-pass control). Suite
  844/0, build clean, headless exit 0. Stated-not-cut: an on-fail `DealHits` would need the child pipeline
  (no corpus conditional spell deals damage on fail). Note: CLAUDE.md's "morale/fatigue absent" stub note is
  stale — both are live (`MoraleUtilities`/`FatigueUtilities`, #091/#020). Unlocks ~4 corpus spell occurrences
  once authored locally.
- 2026-06-28: **Forced enemy movement primitive (#6) done** (engine `034-spell-content`, commit `0e707b7`).
  The cast path's non-damage branch (renamed `ApplyTokenEffect` → `ApplyNonDamageEffect`) now also runs
  `OperationExecutor.Execute`, so an imperative `Effect.TriggeredMove` fires (token ops and executable ops are
  disjoint `OfType` filters, so buff/debuff spells are unaffected). `Effect.TriggeredMove.Apply` stamps the
  move's controller as the rule's **bearer's** player → a self-move (Vanguard/Harassing) is unchanged
  (bearer == moved unit), while a cross-unit spell is **caster-directed** (`MoveUnit` gained an optional
  `controller`, threaded through `InvokeTriggeredMove`; `GameOperationServices` routes the
  `DefineMovementPathRequest` to `controller ?? unit.PlayerID`). `SpellText` describes it in the menu. New
  `CastSpellStage_ForcedMoveSpell_CasterDirectsEnemyMove` (asserts the enemy is displaced AND the request's
  `TargetPlayerID` is the caster). Suite 841/0, build clean, headless exit 0. **GUI hand-verification pending**
  — the caster's movement resolver renders an enemy-owned unit as the mover; eyeball for rendering quirks
  (engine + headless path is correct). Unlocks ~1 corpus spell once authored locally.
- 2026-06-25: **Multi-unit damage primitive (#4) done** (engine, branch `034-spell-content`).
  Restructured `CastSpellStage` to the `ShootStage`/`FireStage` idiom (chosen over a contained
  swappable-metadata wrapper, which was judged a moderate hack — it subverts the context-identity model and
  needs a downcast): a `SpellDamageRunContext` holds the chosen targets + a cursor; the new looped child
  `ResolveSpellDamageStage` builds a fresh `CombatMetadata` per target in its `GetNewChildContext` (pops the
  next target, rolls its hits through the hit-complete fold), and `DetermineMoreSpellTargetsStage` loops it
  until every target is resolved. A damage spell with `MaxCount > 1` now hits every selected unit (each with
  its own AP/Blast/save resolution) instead of only `targets[0]`. Single-model (#3) and buff paths preserved
  unchanged; all 14 prior caster tests stayed green. New `CastSpellStage_MultiUnitDamageSpell_HitsEveryTarget`;
  suite 775/0, build clean, headless exit 0. Unlocks ~19 corpus spells once authored locally.
- 2026-06-25: **Single-model damage primitive (#3) done** (engine, branch `034-spell-content`).
  `TargetSelector.SingleModel` flags the targeting mode; `CastSpellStage` picks a living model in the target
  unit (mandatory `SelectionRequest<ModelData>`, mirroring Takedown's `BuildTargetListStage`) and seeds an
  `IndividualTargetResult` into the damage child pipeline's metadata, so `AssignWoundsStage` confines all
  wounds to that model with no carry-over. New `CasterRuleIntegrationTests.CastSpellStage_SingleModelDamage
  Spell_ConfinesWoundsToOneModel` (3 lethal hits → only 1 of 3 models dies, vs. the Blast test that wipes a
  unit). Suite 774/0, build clean, headless exit 0. Unlocks ~22 corpus spells once authored locally.
- 2026-06-25: Branch `034-spell-content` cut from master in both repos (master had #101 + #022 merged).

## Outcome
(pending)
