# 033 — Caster(X) subsystem (framework)

**Status**: in-progress
**Related**: #010 (custom-action seam — the runway), #042 (rule architecture), #059 (per-army STJ embedding), #034 (spell content — separate), #094 (friendly-Caster ±1 assist — spun off from this item), #093 (per-model activated abilities while joined)

## Goal
The Caster framework: per-round spell-token economy (Caster(X) grants X tokens/round, cap 6, carry
over), an in-game casting loop (Cast → pick spell → pick target within range+LoS → 4+ decisive roll
→ apply effect), and the two spell-effect execution primitives (deal-hits damage via the synthetic-
hit pipeline; grant-temporary-rule buff/debuff via the RuleGrant token). A spell is authored purely
as serialized JSON (a data record over a C# effect/cost/target vocabulary), embedded per-army like
#059's rule definitions. "Done" = a Caster casts a representative damage spell and a representative
buff spell end-to-end in a real game, proven by integration tests, with full content (all 18 army
spells + conferred rules) left to #034.

## Decisions
- **Army-wide spell list + Cast menu** (user fork, 2026-06-21): spells are a JSON array on the army;
  any unit with Caster(X) casts any of the army's spells. One "Cast" action → spell picker → target
  picker. (Alternative — per-unit spell abilities surfaced as individual actions — rejected: doesn't
  match OPR or the army-forge source shape, crowds Choose Action.)
- **Spell tokens carry over between rounds, cap 6** — so the Caster grant uses `ManualOnly` clear
  (not `RoundEnd`), and the cap is clamped at grant time in `StartOfRoundExtraActionStage`, not by
  clearing. Matches OPR ("gets X per round, can't hold more than 6").
- **±1 friendly-Caster assist = a tracked follow-up slice** (user fork): build the core casting loop
  first; the assist (hook `Casting_OnSpellAssistOffered`, per-friendly-Caster spend decision) lands
  after.
- **#033 framework first; #034 = content** (user fork): prove the framework with representative
  spells built from already-implemented rules; author all 18 spells + the ~12 conferred buff/weapon
  rules (Shred/Crack/Shatter/Lacerate/Quick Shot/Evasive/Melee Evasion/Unwieldy/Unpredictable
  Shooter/Highborn+Lustbound Boost/Unstoppable-when-shooting) under #034.

## Deferred (recorded — not silently cut)
- **±1 friendly-Caster assist within 18"** — spun off to its own item **#094** (2026-06-21).
- **Single-model targeting** ("resolved as if the target was a unit of [1]" — Total Seizure,
  Psy-Destruction): reuses the Takedown `IndividualTargetResult` + a `SelectionRequest<ModelData>`
  pick. Small follow-on; lands with #034's single-model damage spells.
- **#034** — full per-army spell content + conferred-rule implementations.
- **Spell-authoring UI** (army builder) — basic editor shipped (see Notes); richer custom-rule authoring
  still tracks with #087.

## Spell-primitive coverage survey (2026-06-21)

Surveyed the full GDF army spell corpus (~282 castable spells across 47 armies; local copyrighted
reference, not reproduced here) against the framework's two effect primitives. **~63% (~179 spells) are
already expressible**: ~65 are single-enemy-unit damage (`Effect.DealHits`) and ~114 are keyword grants to
friendly/enemy unit(s) (`Effect.AddRule`). The remaining **~37% (~103 spells) need new primitives**, ranked
by how many spells each unlocks:

1. **Pre-save hit-stage rules on dealt hits (~36 spells)** — ✅ **DONE** (primitive 1, engine `b8fef9c`).
   `CastSpellStage` rolls the hits as real dice and runs the hit-complete fold before the save flow, so
   Blast multiplies and on-6 rules fire on spell hits.
2. **Numeric stat-modifier effect (~23 spells)** — ✅ **DONE** (primitive 2, engine `3d02665`).
   `Effect.StatModifier(ERollKind, Delta, Lifetime)` grants a roll modifier (Hit/Save/Morale) read at the
   roll stages. (Casting/AP/range/move stat kinds aren't covered — they'd need their own sinks; rare.)
3. **Single-model damage target (~22 spells)** — resolve DealHits against one chosen model ("as a unit of
   [1]") via the existing Takedown `IndividualTargetResult` + a `SelectionRequest<ModelData>` pick. (Already
   in Deferred above; the survey quantifies it.)
4. **Multi-unit damage (~19 spells)** — run the DealHits pipeline against EACH selected unit. The target
   selector already supports up-to-N; `CastSpellStage` currently runs the damage pipeline once (target[0]),
   so it needs looping per target. (Already deferred; quantified here.)
5. **Conditional / triggered effect (~4 spells)** — run a test (e.g. a morale test) and branch the effect
   on the outcome.
6. **Forced enemy movement (~1 spell)** — reposition an enemy unit (`InvokeTriggeredMove` exists as an
   executable op but isn't wired into the cast path). Lowest priority.

**Not needed at the spell level:** no castable spell requires Heal, Summon/spawn, terrain/objective,
random-branch, or token-manipulation primitives (those mechanics live only in unit special rules, never in
the six castable spells). A handful of "counts as in dangerous/difficult terrain" spells are currently
treated as `AddRule` keyword grants — a judgment call; if terrain-status wants its own primitive they'd
move into the list above.

**Caveat on the ~114 `AddRule` spells:** they're *authorable* today, but most confer army-specific keywords
(faction "Boost" rules, Evasive, Quick Shot, etc.) that aren't implemented yet — implementing those
conferred rules is #034 content, separate from the effect primitives above.

## Notes
- 2026-06-21: **Primitive 2 — numeric stat-modifier spell effect done** (engine `3d02665`, bump pending).
  `Effect.StatModifier` repurposed (its dead `EStatKind` decl) to `(ERollKind Roll, int Delta, ELifetime)`:
  grants the target a signed modifier to a roll (Hit/Save/Morale) for a duration — "+1 to hit / -1 to
  defense / -1 to morale". Roll kind encoded in the token TYPE (HitRollModifier/SaveRollModifier/
  MoraleRollModifier — Foundation strings, so rolls don't merge and Tokens needn't reference ERollKind);
  `TokenPayload.StatModifier(Delta)` carries the value. `GrantedRollModifiers.ConsumeNet` folds the
  bearer's grants into the hit/save/morale stages' existing modifier math (same sign), consuming
  FirstTrigger ("next time") grants on use; duration grants swept by `TokenClearService`. Removed dead
  `RuleOperation.ApplyStatModifier`. Tests: grant+consume a +1 hit buff; duration grant persists. Unlocks
  ~23 corpus spells. Suite 639/0. **Edge (recorded):** two grants of the SAME roll kind from the same
  owner merge in the container (delta×count assumes equal deltas) — rare; the robust fix is the granted-
  effect store noted in #101.
- 2026-06-22: **Stat-modifier in the Army-Builder spell editor.** The spell editor's effect-kind dropdown
  gained a third option, **Stat modifier** (roll: To-hit / Defense / Morale, a +/- modifier, and a
  duration), authoring `Effect.StatModifier`. `SpellText.Describe` renders it (e.g. "+1 to hit rolls (next
  time)") in the cast menu and the editor's live preview. So all three effect kinds (Damage / Buff / Stat
  modifier) are now GUI-authorable. Engine `096d003`; app build clean; suite 639/0.
- 2026-06-21: **Primitive 1 — pre-save hit rules on spell damage done** (engine `b8fef9c`, bump pending).
  `CastSpellStage` now rolls the spell's hits as real dice and runs the hit-complete fold
  (HitInjection/HitMultiplier/save-mod sinks) before the save pipeline — reusing RollToHitStage's
  machinery — so **Blast multiplies, "on an unmodified 6" rules add hits, and Rending promotes AP** on
  spell hits (the old cosmetic all-on-6 seed skipped that phase). Faces don't gate the hits (all
  automatic); they only feed on-6 rules. Test: Blast(3) turns 2 hits → 6, wiping a 6-model unit. Added
  `FixedFaceDiceRoller` test double (honors rollCount; `FixedDiceRoller.TotalRolls` is always 1, which
  collapses multi-hit save rolls) + an optional dice-roller arg on `TriggeredMoveTestContext`. Unlocks
  ~36 corpus spells (per the survey). Suite 637/0; headless caster game exits 0.
- 2026-06-21: **Spell-authoring UI (Army Builder)** — app-only. New "Spells" section in
  `ArmyBuilderScreen`: add/edit/remove army-wide spells (name, tokens-to-cast, target range / max-count /
  affinity [Enemy/Friendly/Any/Self] / requires-LoS, and effect = **Damage** {hits, AP, weapon-rules} or
  **Buff** {grants rule, duration}). Weapon-rules use a catalog-backed picker (weapon-scoped, numeric like
  Blast(3) supported) rather than a free-text field; a live `SpellText.Describe` preview shows the exact
  cast-menu subtext. Fixed a pre-existing `ArmyBuilderScreen.Load`/New bug that dropped embedded
  `RuleDefinitions` (and would have dropped `Spells`) on load/new — both now round-trip. Spells persist via
  the existing `Save` (STJ `RuleJson.Options`). App build clean. **GUI hand-verification pending** (can't
  render ImGui headlessly). Partly realizes what #087 anticipated for spell authoring; defined per-army
  spell *content* is still #034.
- 2026-06-21: **Slice 4 — spell-menu UX** (engine `aa57cf8`, app + bump pending). The spell picker now
  shows, under each spell, a one-line effect summary ("grants Furious (next time) — up to 2 friendly
  units within 12\"") and puts the caster's current token count in the prompt ("Choose a spell to cast —
  Magus has 3 spell tokens"). Added optional `StringSelectionRequest.OptionDescriptions` (null for the
  action menu / custom actions), `SpellText.Describe` (engine-side, so CLI + GUI render identical
  subtext), and rendering in `GuiStringSelectionResolver` (smaller + dimmed) and the CLI
  `StringSelectionResolver`. Suite 636/0; headless shows the subtext + live token count; default-army
  action menu unaffected (regression smoke exit 0). User-requested follow-up to the framework.
  **Polish (engine `067eded`, app `7b566b0`):** the default ImGui font has no em-dash (rendered as
  `?`), so the prompt + `SpellText` use an ASCII `" - "` separator and `Describe` is sentence-cased
  ("Grants …"); `GuiStringSelectionResolver` now measures text (`CalcTextSize`) to size the dialog and
  each option row, so a wrapped description no longer overflows into the next option.
- 2026-06-21: **Slice 3 done — framework complete** (engine `cc04efd`, bump pending). Spell-effect
  execution: `CastSpellStage` is now a `ParentStage` (mirrors `StrafingStage`); on a successful cast it
  applies **buff/debuff** spells (`Effect.AddRule` etc.) by granting a `RuleGrant` token to each target
  (polymorphic `Effect.Apply` + `OperationApplier`), and resolves **damage** spells (`Effect.DealHits`)
  through the shared save→wound→assign→apply child pipeline against the target as a synthetic AP-carrying
  attack, with the spell's pre-resolved weapon rules attached. Extracted `SpellTargeting` (shared by
  `GetCanCast` + the cast stage): Cast is offered, and a spell listed, only when it has a legal target —
  **this fixed an infinite cast loop** (no-target spell re-picked → stack overflow) that the headless
  caster smoke caught. Committed `armies/example-caster.fdgarmy` (Caster(3) + damage + buff spell),
  pinned by `ExampleArmyFileTests`. Tests: buff grants the RuleGrant token; AP(3) damage kills a 1-wound
  target through the real pipeline. **Suite 635/0; headless caster game exits 0** — round-start grant
  fires each round, Haste buffs 2 friendly units, Fire Bolt deals damage, failed casts still spend
  tokens, game completes 4 rounds.
  **Deferred (recorded):** ±1 friendly-Caster assist (next tracked slice); multi-target damage (only
  target[0] hit — Strafing has the same single-target limit); single-model targeting ("unit of [1]");
  pre-save weapon rules on spell hits (Blast hit-multiply / Surge) — the synthetic pipeline starts at the
  save stage, so only AP + save/wound-phase rules (Bane/Deadly/Regeneration) fire.
- 2026-06-21: **Slice 2 done** (engine `78189c9`, bump pending). Cast action + control flow:
  `ChooseActionStage.GetCanCast`/`ToCast` surface a first-class **Cast** for a unit carrying Caster(X)
  with an affordable spell (mirrors `GetCanShoot`); "Cast" reserved in the #010 collision guard only
  for casters (a non-caster custom action may still be named "Cast"). New `CastSpellStage`: pick spell
  (`StringSelectionRequest` + Cancel) → build eligible targets from the spell's `TargetSelector`
  (affinity + range + LoS, reusing the `ChooseRangedAttackStage` per-model filters) → pick target(s) up
  to `MaxCount` (`SelectionRequest<UnitData>`) → spend `Threshold` tokens on the attempt → 4+
  `RollDecisive` → loop back **layered** (no HasMoved/HasAttacked; can cast again until tokens run out).
  Effect application stubbed (logged) pending slice 3. Tests: Cast surfaces+routes; tokens spent +
  layered loop-back. Suite 632/0.
- 2026-06-21: **Slice 1 done** (engine `f361f57`, bump pending). Army-wide spell list:
  `SpellDefinition(Name, Threshold, TargetSelector, Effect)` embedded in `ArmyListFile.Spells` (STJ
  kind-schema). `ArmyListSpellResolution.ResolveSpells` (called from `FDGServer.CreateArmyDataFromArmyFile`,
  resolver live) pre-resolves each damage spell's `WithRules` → weapon-scoped `ResolvedRule`s on a
  `RuntimeSpell`, stored `[JsonIgnore]` on `ArmyData` (host-side; client gets options in the request).
  `SpecialRuleEntryParser` parses "Name"/"Name(N)". `Effect.DealHits`/`InvokeDealHits` gained an
  `ArmorPenetration` field (AP is a weapon stat, default 0). **Refinement vs plan**: WithRules resolved
  at load (the resolver isn't reachable from a stage — confirmed `GameContext` exposes no resolver), so
  `RuntimeSpell` carries pre-resolved rules rather than the cast stage resolving at cast time. Suite 630/0.
- 2026-06-21: **Slice 0 done** (engine `af34606`, bump `ec5bc8b`). Caster(X) token economy:
  `Caster` added to `CoreRuleCatalog` (Round_OnRoundStart → GrantToken SpellTokens=Arg(0), ManualOnly
  so tokens carry over); `StartOfRoundExtraActionStage.GrantSpellTokens()` fires the round-start hook
  for every living unit each round (incl. round 1; resume path skips it, no double-grant) and clamps
  to `GameWideConstants.MAX_SPELL_TOKENS` (6). `CasterRuleIntegrationTests` (grant / carry-over+cap /
  non-Caster). Fixed `SpecialRuleRegistryTests` (Caster was its "unimplemented" example). Suite 627/0.
- 2026-06-21: Item opened. Branch `033-caster` cut in both repos off master (#010 `d56167b` in
  history). Plan approved. Spell content confirmed against three army-forge books (High Elf Fleets,
  Battle Brothers, Wormhole Daemons of Lust) via API `army-books/<id>?gameSystem=2`: each has 6
  spells, two each at threshold 1/2/3; archetypes = deal-N-hits-with-rules and grant-rule-once.

## Outcome
(pending)
