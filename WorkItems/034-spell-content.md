# 034 — Spell content + the primitives & conferred rules it needs

**Status**: in-progress — single-model damage primitive done (2026-06-25)
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
   - **#4 multi-unit damage (~19 spells)** — run the damage child pipeline once per selected unit (today
     `CastSpellStage` hits only `targets[0]`). Needs the `ParentStage` to drive N sequential children.
   - #5 conditional/triggered (~4), #6 forced enemy movement (~1) — low priority.
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
- **Multi-unit damage (#4)** — next primitive slice (see Scope).
- Conditional/triggered (#5) and forced-movement (#6) primitives.

## Notes
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
