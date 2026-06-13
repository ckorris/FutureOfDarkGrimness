# 059 — JSON rule-definition loader

**Status**: in-progress (serialization foundation landed on master; loader consumption side underway on branch `059-json-rule-loader`)
**Related**: #042 (this is the "JSON loader" sub-stream of the special-rules architecture, now broken out), #026 (army-load rule resolution), #027 (weapon-scope), #058 (the eventual all-STJ migration)

## Goal
The engine can load special-rule *definitions* from a JSON file at startup and register them into the rule resolver alongside `CoreRuleCatalog.All`, so new/army-specific rules can be authored as data without a rebuild. "Done" = a `.json` rules file is read into `SpecialRuleDefinition[]`, validated at load (capability/hook correctness via the existing `RuleValidator`), and registered at `FDGServer` rule-resolver construction; a malformed file is rejected with a clear message rather than throwing at dispatch time.

## Notes
- 2026-06-13: **Serialization foundation complete and on master** (engine commits `5154c5a` + `af3f5bd`, superproject bumps `a9333e7`/`90ac830`). The full `SpecialRuleDefinition` tree round-trips through System.Text.Json as clean `kind`-tagged JSON:
  - Six closed sum types decorated with `[JsonPolymorphic]`/`[JsonDerivedType]`: `ValueSource`, `Condition`, `Effect`, `RerollCondition`, `TokenClearTrigger`, `DiceExpression`, `Cost` (~60 stable tags).
  - `Rules/Serialization/RuleJson.cs` — shared `JsonSerializerOptions` (camelCase, string enums, indented) + a type-info modifier that strips get-only computed properties (`RequiredCapabilities`, `DiceExpression.Sides`).
  - Tests per hierarchy + a **reflection guard** (`PolymorphicRuleRegistrationTests`) asserting registration completeness and ctor-parameter survival, so the hand-maintained tag list can't silently drift.
  - **Corpus proof:** `EveryCatalogRule_RoundTripsStructurally` round-trips all ~27 `CoreRuleCatalog` rules via JSON idempotence — the vocabulary covers every live `Condition`/`Effect`/`Cost`/nested shape.
  - Library decision (see #058): STJ for the new rules format (closed non-generic hierarchies = STJ's sweet spot); the message/save layer stays on Newtonsoft `TypeNameHandling.Auto` (open generic polymorphism = STJ's weak spot). Mixing is correct here, not laziness.

## Remaining workstreams
1. ~~Serialize rule definitions~~ — DONE (above).
2. **File → registry**: read a rules `.json` → `SpecialRuleDefinition[]` (via `RuleJson.Options`) → register into the `RuleResolver` at `FDGServer` (~line 162, where `CoreRuleCatalog.CreateResolver()` is built today). Decide precedence vs. core rules (override by name? additive only?).
3. **Validate at load**: run each loaded definition through `RuleValidator` + `HookContextCatalog` so capability/hook mismatches are caught at load with a message, not a runtime throw.
4. Argument expressiveness (only `RuleArgument.Int` is exercised today; loader may surface Str/Float/Enum needs).
5. Networking/save registry-hash consistency: host and client must load the same rule set (a registry fingerprint check), and saves must reference rules the loading build knows.
6. Reconcile the app-side `SpecialRuleRegistry` (army-builder UI) with the engine catalog + retire legacy `GetRealSpecialRulesFromArmyList`.
7. Army creator: scope-aware rule picker from the unified registry. Open fork: "creator" = (A) assign existing rules vs (B) author new rules tool.

## Decisions
- 2026-06-13: Broke the JSON loader out of #042 into its own number — #042 is a sprawling umbrella and the loader is a distinct, sizable chunk with its own branch. The serialization half shipped to master directly (incremental, low-risk); the consumption half (#2/#3) gets a feature branch.

## Outcome
_(open)_
