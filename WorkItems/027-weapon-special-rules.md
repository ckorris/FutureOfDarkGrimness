# 027 — Weapon-scoped special rules

**Status**: in-progress
**Related**: #042 (architecture), #026 (unit-level army-list resolution), #055 (resolver attribution — per-weapon accuracy depends on this), #032 (weapon rule implementations), JSON loader sequencing (must land before the loader/army creator per the 2026-06-11 decision recorded on #027's index entry)

## Goal

The #042 rule system attaches `ResolvedRule` definitions only at the unit level, so a unit's Blast/Indirect/Rending/etc. apply to *all* its weapons — a model with one Blast weapon and one plain rifle can't be represented. Make weapons first-class rule carriers: weapons carry `ResolvedRule`s resolved from the army file's per-weapon rule names (`WeaponFileEntry.SpecialRules`, which already exists in the schema), and dispatch evaluates per-weapon. Reconcile the legacy `ISpecialRule_Weapon` system with #042.

## Plan (agreed 2026-06-11)

Four slices, engine-side (submodule branch `027-weapon-special-rules`), submodule-first commits:

1. **Data model + load path** — `ERuleScope` (Unit/Weapon) on `SpecialRuleDefinition`; catalog tagged; `Weapon.RuleDefinitions` + `AttachRuleDefinition` mirroring `UnitData`; army load resolves `WeaponFileEntry.SpecialRules` via the resolver threaded into the `UnitData(playerID, unitFileEntry, store, resolver)` ctor (replacing the `GetRealWeaponSpecialRulesFromEntries` stub); shared attach helper warns + skips scope-misattached rules in both directions.
2. **Per-weapon dispatch** — participant tuples gain an optional `IWeapon`; `CollectTagged` walks unit ∪ weapon rules; `RuleInvocation.Weapon`; same-definition dedup (rulebook: instances of the same rule don't stack unless (X)); fire-pipeline stages pass `metaData.WeaponType`. Mixed-armament integration test.
3. **Defender weapons + sight queries** — Counter (weapon-scoped per rulebook: "strikes first with this weapon") evaluated over the defender's distinct melee weapons at strike-order/charge-contact; `SightRuleQueries` finally uses its forward-seam weapon param, making cover/LoS-ignore per-weapon and #055's UI attribution accurate.
4. **Legacy teardown** — delete `ISpecialRule_Weapon` + stub rule classes, remove `Weapon.SpecialRules` HashSet, resolve `WeaponComparer` TODOs.

## Decisions

- **2026-06-11**: Load-time resolution threaded into the `UnitData` constructor path (resolver param), not an FDGServer post-process — weapons are born with their rules; weapons have no ID so an entry→instance mapping would be fragile. (User sign-off.)
- **2026-06-11**: **Strict scope enforcement at army load**: a weapon-scoped rule named at unit level (or vice versa) is warned about and skipped, NOT attached — chosen over silent unit-wide back-compat. The test harness attaches directly to units/weapons, bypassing the loader, so the existing suite is unaffected; enforcement lives only at the load seam. (User sign-off — chose warn/reject over keep-working.)
- **2026-06-11**: Legacy `ISpecialRule_Weapon` removal happens in this ticket (slice 4), not a follow-up. (User sign-off.)
- **2026-06-11**: Scope classification is taken from the rulebook PDF's own wording — "this weapon"/"weapons with this rule" ⇒ Weapon; "this model"/"models with this rule" ⇒ Unit. **Weapon**: Blast, Deadly, Indirect, Reliable, Rending, Takedown, Bane, Unstoppable, Surge, Counter (+ AP as a stat, Limited when implemented). **Unit**: Stealth, Artillery, Fast, Very Fast, Slow, Relentless ("this model" — deliberately unlike Surge), Furious, Thrust, Impact, Regeneration, Tough, Scout, Ambush, Vanguard, Martial Prowess, Strafing. Counter being weapon-scoped is what forces defender-side weapon evaluation (slice 3).

## Notes

- 2026-06-11: **Slice 1 done (suite 389/0, headless smoke exit 0).** `ERuleScope` (Foundation) + `SpecialRuleDefinition.Scope` (default Unit); 10 catalog rules tagged Weapon (Blast, Deadly, Indirect, Reliable, Rending, Takedown, Bane, Unstoppable, Surge, Counter). `Weapon.RuleDefinitions` + `AttachRuleDefinition` ([JsonIgnore], names-are-the-persisted-form, mirroring `UnitData`). New `SaveLoad/ArmyListRuleResolution.ResolveForScope` is the single resolve+scope-enforce helper — FDGServer (unit level) and the `UnitData` file-entry ctor (weapon level, new optional `IRuleResolver` param replacing the `GetRealWeaponSpecialRulesFromEntries` stub) both use it; `DescribeRuleEntry` moved there from FDGServer. Rules resolve once per `WeaponFileEntry` and are shared across the quantity's instances. 6 tests in `Tests/WeaponRuleAttachmentTests.cs`. **App-side**: the built-in test army had Surge/Blast(3)/Counter/Takedown at unit level — moved onto Heavy Rifle/Fists/Rifle weapon entries. **Known mid-ticket state: weapon-attached rules don't fire yet** (dispatch still walks only unit rules) — the test army's Blast/Surge/Counter/Takedown are inert until slice 2 lands; before this ticket they incorrectly fired unit-wide.
- 2026-06-11: Work started. Repos synced (submodule fast-forwarded 18 commits — the Phase 8 batch), branches created in both repos. Surveyed the seams: `WeaponFileEntry.SpecialRules` already exists (schema is weapon-capable); `UnitData` ctor builds one `Weapon` instance per quantity (round-robin to models) with the stub returning an empty rule set; every fire-pipeline stage has exactly one weapon in scope via `ICombatMetadata.WeaponType`; `SightRuleQueries` already takes the weapon as an unused forward seam; `RuleEvaluator` participants are `(IUnit, ERuleSeat)` tuples with origin-tagged suppression.

## Outcome

(pending)
