# 095 — Special rules not re-attached on save/load resume (HIGH PRIORITY)

**Status**: implemented (Approach B) — engine green, build clean, headless exit 0; awaiting commit + GUI hand-verification
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
- 2026-07-23 (found while verifying #197 P6 in play): **residual - GRANTED supplement rules die on resume.**
  Approach B rehydrates rules *attached* to a carrier, but a `RuleGrant` token names a rule the evaluator
  must look up in the shared `RuleResolver`, and on resume that resolver is built from the per-slot
  `ArmyListFile`s - which this file already records as vestigial. So only CORE definitions are registered,
  and a resumed game logs `Granted rule 'X' on <unit> has no definition in the registry - the grant does
  nothing`. Class-wide, not specific to any one rule: proved with a scenario carrying the shipped
  `Precision Fighter Buff` (grants supplement `Precision Fighter` -> dead) beside `Speed Debuff` (grants
  core `Slow` -> works). Affects all 15 shipped `* Buff` rules plus #197 P6's `Piercing Debuff`. The
  natural fix is the same shape as Approach B: persist the embedded definitions with the save (or blob
  them per army) so `BuildRuleResolver` has something real to register on resume. Not attempted here -
  recorded so P6's data choice isn't mistaken for the cause.
- 2026-06-22: Started. Branch `095-rules-rehydration-on-resume` (both repos), off master `b5e71aa` / submodule `a0ab822`.
  - **Failing tests written** (`FutureOfDarkGrimness/Tests/RuleRehydrationOnResumeTests.cs`, real `GameSaveSerializer` round-trip): unit-scoped (`Stealth`), weapon-scoped (`Surge`, #027), and per-model hero (`Furious`, #006 slice F) rules **all come back empty after save→load** — 3 RED, confirming the bug. Two control tests are **GREEN**: a self-owned `Shaken` token and the cross-unit `EmbarkedIn` token (#035, incl. its `OwnerUnitID` back-ref) both survive — so **tokens are NOT in scope** (they live on the `[JsonProperty] TokenContainer`, which round-trips even the polymorphic `ClearTrigger`). Not committed (red).
  - **Key constraint discovered:** on resume the per-slot `ArmyListFile` is **vestigial** (`LobbyViewModel_Host.cs:400-403` — falls back to a temp test army), so re-running army-load resolution / rebuilding the embedded-rules resolver from slots (option b) is **not viable**. The fix must be self-contained in the save.
  - The three live `AttachRuleDefinition` call sites all funnel through one method per carrier (`UnitData`/`ModelData`/`Weapon.AttachRuleDefinition`), and `ResolvedRule` already carries `RequestedName` + `Arguments` (the only heavy part is `Definition`). `FDGServer.AttachRulesFromArmyList:232` already anticipates this fix ("restored by the … resume rehydration").
  - **Plan DECIDED (signed off 2026-06-22): Approach B — per-carrier STJ blob, custom rules IN scope.**
    - Each carrier (`UnitData`/`ModelData`/`Weapon`) gains a `[JsonProperty] string _ruleDefinitionsJson`, kept current inside the single `AttachRuleDefinition` each one already routes through. It holds an STJ blob (`RuleJson.Options`, the proven `ArmyListUpdateMessage` precedent) of `PersistedResolvedRule { RequestedName, Definition, int[] args }` — Newtonsoft sees only an opaque string, STJ owns the rule graph. `RuleDefinitions` stays `[JsonIgnore]`.
    - Rehydration lives in **`GameSaveSerializer.Load`** (alongside the existing `RewireSubscriptions`), NOT `FDGServer` — so it covers any load path and the store handed to the resume ctor is already rehydrated (no FDGServer change). Walk units + models + each model's weapons; `RehydrateRules()` parses the blob back into `_ruleDefinitions`, idempotent (only when the live list is empty), so shared instances / double-calls are safe.
    - Approach B restores the actual resolved definition (core + custom + alias/override) with no resolver rebuild and no new store type. Args persisted as `int[]` (only `RuleArgument.Int` exists; guard throws on any other arg type so it can't silently drop).
    - **Test-assertion note:** deserialized `SpecialRuleDefinition`s are fresh instances, and record value-equality compares array members by reference, so `Does.Contain(CoreRuleCatalog.X)` won't match post-round-trip under B — assert by `Definition.Name` / `RequestedName` instead (the graph round-trip itself is already covered by `SpecialRuleDefinitionSerializationTests`). Will adjust the 3 red tests accordingly when the fix lands, + add custom-def / alias / Int-arg survival tests.
    - **All changes are engine-side (submodule).** Per convention, awaiting the go-ahead to modify the submodule before writing fix code (tests + branch were directly requested, so already in).
  - **IMPLEMENTED 2026-06-22 (Approach B).** Engine files: new `Rules/Serialization/RuleAttachmentPersistence.cs` (`Serialize`/`Deserialize` of `PersistedResolvedRule{RequestedName, Definition, int[] args}` via `RuleJson.Options`; hard-fails on a non-Int arg); `UnitData`/`ModelData`/`Weapon` each gained a `[JsonProperty] string? _ruleDefinitionsJson` kept current in `AttachRuleDefinition` + an idempotent `RehydrateRules()`; `GameSaveSerializer.Load` now calls `RehydrateRuleDefinitions(store)` (walks units + models + each model's weapons) next to `RewireSubscriptions`. Tests (`RuleRehydrationOnResumeTests`, 10): unit/weapon/per-model-hero/**custom-embedded**/**alias**/**Int-arg** rules survive + 2 token controls + **`RehydratedRule_FiresThroughHitRollStage`** (a loaded Stealth defender raises the hit threshold 4→5 through the REAL `DetermineHitRollStage`/`RuleEvaluator` — proves *functions*, not just *persists*) + **`FullSaveWithTeamsArmiesAndProgress_RehydratesRulesAndResumeState`** (rules rehydrate amid a realistic save: teams + armies + `GameProgressData`, resume state intact). **Verified:** engine suite **709/709**, full `dotnet build` clean (0 errors), headless smoke exit 0 (full game to "It's a tie!"). Confirmed the only production `GameSaveSerializer.Load` caller is `Program.cs:72`, whose store feeds the `FDGServer` resume ctor — so the round-trip tests exercise the real resume mechanism. The Tough concern is settled: the rule object rehydrates (so a future GUI shows `Tough(3)`) while the `Lifecycle_OnUnitCreated` max-wounds effect stays gated to fresh-game creation (`FDGServer` `applyCreationRules`), so no wound doubling — `IntArgument_SurvivesSaveLoad` pins it.
    - Not committed yet (awaiting commit go-ahead). Remaining: commit submodule-first + bump pointer; GUI hand-verification of a real save→resume; optional #035 end-to-end disembark-on-resume integration test (core regression already covered by the unit round-trip).
- 2026-06-21: Opened (HIGH PRIORITY) after verifying the gap while designing #035 slice C (disembark). The disembark ability is rules-supplied, so it depends on this fix to survive a resume; #035 attaches `Disembark` universally precisely so a minimal version of this fix (re-attach engine/universal rules) restores it without the unit↔entry mapping.
