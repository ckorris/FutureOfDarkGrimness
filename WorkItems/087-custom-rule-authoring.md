# 087 — Custom special-rule authoring + standalone rules files

**Status**: not started (broken out of #059 on 2026-06-13)
**Related**: #059 (embedded-rules pipeline + STJ `kind`-schema this builds on), #058 (all-STJ migration), #042 (rule architecture)

## Goal
Let users create and share special rules as data, beyond picking from the engine catalog:

1. **Author new rules in the army builder** — a UI to define a brand-new `SpecialRuleDefinition` (name, passive hook entries: condition × effect × lifetime × seat, and/or activated abilities) and attach it to the open army's `RuleDefinitions`, not just select existing core rules. The authored rule round-trips via the existing STJ `kind`-schema and is validated at load (#059 workstream 3).
2. **Import/export rules as a standalone file** — a rules-only file (separate from any `.fdgarmy`) holding a set of `SpecialRuleDefinition`s, so a shared/house rule set can be loaded **independently of which armies are in play**. This is the deferred #059 **workstream 8** ("standalone global rules override layer"): a global layer that overrides core rules regardless of army selection.

## Why separate from #059
#059 delivered the *consumption* pipeline (rules embedded in an army, registered core-first then override-by-name, validated, dispatched). #087 is the *authoring/distribution* side — a meatier, design-heavy UI + file-format feature. The user explicitly asked to track it on its own.

## Open design forks (surface before building)
- **Authoring UX**: full condition/effect tree editor (powerful, complex ImGui) vs a guided "rule template" picker (parameterize a few known shapes). Likely start guided.
- **Standalone file**: new extension (e.g. `.fdgrules`) holding `List<SpecialRuleDefinition>` via `RuleJson.Options`; load precedence vs army-embedded rules (global layer registers first? last? — needs a defined order with the existing core-first/override-by-name registration in `FDGServer.CreateArmies`).
- **Scope authoring**: an authored rule must declare `ERuleScope` (Unit vs Weapon); the builder must make that explicit (ties into the #059 scope-aware picker work).

## Notes
- 2026-06-13: Created. Broke out of #059 per user request after #059 workstream 6 (picker reconciliation). The scope-aware picker fix (weapon rules only on weapons) is being finished under #059, not here.

## Outcome
_(open)_
