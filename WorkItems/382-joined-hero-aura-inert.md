# 382 — Joined hero's creation-time aura never fires (Robot Legions Reanimation inert)

**Status**: done (engine-side; awaiting GUI hand-verify)
**Related**: #006 (hero join), #101 (aura grant applicator), #183 (grants hero-inclusive), #197 P17d (Reanimation), #303 (all-models gate family)

## Goal

A hero whose aura rule ("X Aura" — `Effect.Aura` at `Lifecycle_OnUnitCreated`) joins a host unit and
the aura's granted rule projects over the combined unit, exactly as it does when the aura sits on a
standalone unit. Done = a Robot Lord with the Re-Animator upgrade (Reanimation Aura) joined to a
warrior unit heals/revives at activation start, with an engine integration test pinning the seam.

## Notes

- 2026-08-22: Filed from a player report ("Robot Legions Reanimation isn't working"). Root cause
  verified in source:
  - All 3 corpus references to Reanimation are the AURA form on the three Robot Legions lords
    (`ReanimationShippedDataTests`), so in the natural play pattern (lord joined to a unit) the base
    rule is only reachable through the aura.
  - `HeroJoinResolver.Apply` relocates the hero's unit-scoped rules onto the hero MODEL (#006 slice F),
    and the hero's own standalone unit never passes through `UnitCreationRules` (the join consumes it).
  - `UnitCreationRules.Apply` evaluates `RuleParticipant.Actor(unit)` with NO models, so a
    `Lifecycle_OnUnitCreated` entry now living on the hero model is never walked: the aura's
    `GrantTokenToUnit` is never produced, no `RuleGrant` token lands, and the granted rule never
    fires. Standalone aura carriers are unaffected (the unit-level attachment fires normally).
  - Blast radius: EVERY creation-time aura carried by a joined hero — 67 supplement "X Aura" rules +
    the CoreRuleCatalog UnitAura family — not just Reanimation.
- 2026-08-22: Fix shape: walk the hero model's rules in `UnitCreationRules.Apply` for entries matching
  (`Lifecycle_OnUnitCreated`, `Effect.Aura`) by EFFECT SHAPE and apply their grants to the host —
  the `ResolveJoinedHeroDefense` precedent (conditions not evaluated; creation entries are authored
  `Always`). NOT a general model-rule walk at creation: the hero's other creation-time rules are
  hero-personal (Tough -> HeroAttachment wounds, Armor(X) -> the join's baked defense) and a general
  walk would apply them unit-wide (e.g. the hero's Tough(6) becoming every grunt's max wounds).
  Deduped by granted rule name against grants the unit already holds, so a host that also carries the
  same aura statically does not double-grant.

## Decisions

- Effect-shape matching at the creation pass (mirrors `ResolveJoinedHeroDefense`), not a data change
  and not a general per-model creation walk — see 2026-08-22 note for why the general walk is unsafe.

## Outcome

Fixed in engine commit 8110040: `UnitCreationRules.Apply` now applies the hero model's
(`Lifecycle_OnUnitCreated`, `Effect.Aura`) entries to the host unit, deduped by granted rule name
(a host statically carrying the same aura grants once). Tests: 2 joined-hero cases in
`AuraRuleIntegrationTests` (grant lands + projects; dedup) and the reported scenario end-to-end in
`ReanimationRuleIntegrationTests.JoinedLordWithAura_ReanimatesTheJoinedUnit` (lord + aura joined to a
3-model unit, 2 casualties, both return at activation start). Both new behavior tests verified red
without the fix. Engine suite 2989 green, app suite 1313 green, headless smoke clean.
GUI hand-verify outstanding: a Robot Lord with the Re-Animator upgrade joined to a warrior unit
should log "Reanimation rolled N dice ..." at the combined unit's activation.
