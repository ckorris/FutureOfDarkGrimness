# 033 — Caster(X) subsystem (framework)

**Status**: in-progress
**Related**: #010 (custom-action seam — the runway), #042 (rule architecture), #059 (per-army STJ embedding), #034 (spell content — separate), #093 (per-model activated abilities while joined)

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
- **±1 friendly-Caster assist within 18"** (own slice in #033).
- **Single-model targeting** ("resolved as if the target was a unit of [1]" — Total Seizure,
  Psy-Destruction): reuses the Takedown `IndividualTargetResult` + a `SelectionRequest<ModelData>`
  pick. Small follow-on; lands with #034's single-model damage spells.
- **#034** — full per-army spell content + conferred-rule implementations.
- **Spell-authoring UI** (army builder) — tracks with #087.

## Notes
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
