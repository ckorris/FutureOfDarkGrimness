# 258 — Rule-definition identity breaks after save/load resume (Sniper Team shoot crash)

**Status**: done (engine `13908c7`)
**Related**: #095 (rule attachment persistence), #209 (weapon-identity nondeterminism family), #256 (found during its re-probe)

## Goal

A resumed game must behave like the live game it was saved from wherever the engine compares
rule definitions. Done = (a) `SpecialRuleDefinition` identity is its canonical `Name` (the
documented registry key), so `rule.Definition == CoreRuleCatalog.X` and the WeaponComparer's
rule matching survive rehydration; (b) the WayTooManyInBack forward-run no longer faults when
the Sniper Team shoots; (c) a regression test pins each.

## Evidence (2026-07-22, WayTooManyInBack.fdgsave forward-run during the #256 re-probe)

- Headless `--scenario WayTooManyInBack.fdgsave` faults in round 3:
  `ChooseRangedAttackStage.BuildWeaponOptions` throws "An item with the same key has already
  been added. Key: Sniper Rifle" the moment the Sniper Team shoots.
- Probe of the loaded save: the three sniper rifles are value-identical (30" A1 AP1,
  [Reliable, Takedown, Surge when Shooting]) but every rule attachment carries its own
  `SpecialRuleDefinition` instance, and the rehydrated core rules fail `== CoreRuleCatalog.X`
  (verified for Reliable/Takedown).
- Chain: `RuleAttachmentPersistence.Deserialize` (#095) deliberately rebuilds definitions from
  the embedded blob (no resolver on the resume path) -> `SpecialRuleDefinition` is a plain
  record whose `IReadOnlyList<>` members compare by reference, so a deserialized copy is never
  `==` anything -> `WeaponComparer.HaveSameRules` (`r.Definition == rule.Definition`) stops
  grouping same-named weapons -> `CombatActionContext.GetTypeSortedWeapons` yields one key per
  model -> `BuildWeaponOptions` keys by `Weapon.Name` and throws on the duplicate.
- Blast radius beyond the crash: EVERY `Definition ==` site is silently false on resumed games
  (Transport/embark checks, Hero joins, Caster detection, Condition/Effect rule matching,
  Effect.cs counter-models...). The crash is just the loudest symptom.
- One caller (`HasAnyFireableTarget`, #200) already dedupes by name to dodge exactly this,
  with a comment acknowledging the name-key collision - the guard papered over the identity bug.

## Fix (agreed 2026-07-22, Chris: "root fix now" over the minimal BuildWeaponOptions patch)

Override record equality on `SpecialRuleDefinition`: `Equals`/`GetHashCode` compare `Name`
only - per its own doc, Name is "the canonical identifier ... used as the lookup key in the
rule registry", and the registry allows one definition per name per game, so name identity IS
definition identity. One change; every `Definition ==` site inherits it.

## Notes

- 2026-07-22: implemented same-day (engine `13908c7`): `SpecialRuleDefinition.Equals`/`GetHashCode`
  compare `Name`. Risk scan first: no `HashSet<SpecialRuleDefinition>`/definition-keyed dictionaries
  anywhere; all 20 `Definition ==` sites want name identity. Pins added to
  `RuleRehydrationOnResumeTests`: a rehydrated attachment compares `==` to the catalog definition,
  and `WeaponComparer` groups two same-named weapons whose rehydrated rule definitions are distinct
  instances (the crash precondition); the fixture's stale "identity-match would fail" doc updated.
  Verified: 1813/1813 green at the time (1815 after #256 S4), and the WayTooManyInBack forward-run
  that faulted in round 3 now plays to completion.
- 2026-07-22: filed from the #256 re-probe.

## Decisions

- Root fix at the definition-equality level rather than patching WeaponComparer or
  BuildWeaponOptions call sites (Chris, 2026-07-22, over the presented minimal alternative).

## Outcome

Root-fixed in one place: `SpecialRuleDefinition` equality is its canonical `Name` (engine
`13908c7`), restoring every `Definition ==` check and the WeaponComparer's weapon grouping on
resumed games. The Sniper Team crash is gone - the WayTooManyInBack save plays rounds 3-4 to
completion. Two regression pins in `RuleRehydrationOnResumeTests`.
