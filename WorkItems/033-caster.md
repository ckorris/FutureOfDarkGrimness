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
- **Spell-authoring UI** (army builder) — tracks with #087.

## Notes
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
