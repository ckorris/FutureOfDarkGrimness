# 095 — Special rules not re-attached on save/load resume (HIGH PRIORITY)

**Status**: todo
**Related**: #052 (save/load — this is a gap in it), #042 (rule framework), #035 (Transport — disembark + capacity depend on this surviving a resume)

> **Renumbered 094→095 (2026-06-21).** Filed as #094 this session, but origin/master had meanwhile assigned #094 to "group-move coherency repair" (merged). Per the never-reuse rule the unmerged item yields; this is now #095. The `035-transport.md` cross-references were updated; commit messages that say "#094" predate the renumber.

## Goal
After loading a mid-game save, every unit's `RuleDefinitions` is **empty**, so all runtime special-rule behavior is silently lost on resume. Fix the resume path so a resumed game behaves identically to the game that was saved — every unit (and weapon, and per-model hero) carries the same `#042` rules it had before the save.

## The bug (verified 2026-06-21)
`RuleDefinitions` is `[JsonIgnore]` (a unit's rules are meant to resolve from army-list rule *names* at army-load, not serialize), and the resume path never re-attaches them:
- `GameSaveSerializer.Load` rebuilds the `GameDataStore` and rewires only *wound* subscriptions — no rule rehydration.
- `FDGServer`'s resume constructor is explicit: *"does NOT recreate teams/armies/models or re-apply creation rules"*, and calls `BuildContextAndLaunch(..., applyCreationRules: false)`. It never calls `CreateArmies` / `AttachRulesFromArmyList`.
- The only three live `AttachRuleDefinition` call sites are all army-load (`UnitData` ctor for weapon rules, `AttachRulesFromArmyList`, `HeroJoinResolver`) — none run on resume.
- No rehydration in `GameProgressUtilities`; no save/load test asserts a rule survives.

**Effect:** after a save→resume, runtime rule evaluation (`RuleEvaluator` reading `IUnit.RuleDefinitions`) finds nothing, so Stealth/Furious/Rending/Indirect/… stop firing, and (for #035) a transport loses its `Transport` rule (capacity/identity → `IsTransport` false, `GetCapacity` 0) and an embarked unit loses its `Disembark` ability. Tough's *effect* survives only because max-wounds is baked into serialized `ModelData`; the Tough rule object is gone like the rest.

## Why it's not trivial
Re-attaching rules needs to map each loaded unit back to its army-list entry (whose `SpecialRules` names drive resolution), but loaded units carry no back-reference to their `UnitFileEntry`. Options to weigh:
- **(a) Persist the resolved rule *names* on the unit** (a serialized `List<string>` of requested rule names per unit/weapon/model) and re-resolve them against a fresh `CoreRuleCatalog`/embedded-rules resolver on load. Most robust; small schema addition; survives renames/reorders better than positional matching. Note STJ vs Newtonsoft: the rule *graph* is STJ but the save/store layer is Newtonsoft (#058) — names are plain strings, so this stays Newtonsoft-friendly.
- **(b) Re-run army-load resolution on resume** using the saved `ArmyListFile`s in the player slots, matched to loaded units by a stable key (would need a unit↔entry key that survives save; Hero already has `Id`/`JoinsUnitId`, but most units have none).
- Universal/engine-attached rules (e.g. #035's `Disembark`, attached to every unit) can be re-attached on resume with **no** mapping at all — so even a partial fix unblocks #035's disembark-on-resume.

## Acceptance
- A save→load round-trip test that attaches a runtime rule (e.g. Stealth) to a unit, saves, loads, and asserts the rule still fires (and `RuleDefinitions` is repopulated) — the regression that currently has zero coverage.
- Weapon-scoped (#027) and per-model hero (#006 slice F) rules rehydrate too.
- #035: a resumed game can still disembark an embarked unit, and a transport still reports its capacity.

## Notes
- 2026-06-21: Opened (HIGH PRIORITY) after verifying the gap while designing #035 slice C (disembark). The disembark ability is rules-supplied, so it depends on this fix to survive a resume; #035 attaches `Disembark` universally precisely so a minimal version of this fix (re-attach engine/universal rules) restores it without the unit↔entry mapping.
