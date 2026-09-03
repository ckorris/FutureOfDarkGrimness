# 067 — Content-parser tests + displayable errors

**Status**: done
**Related**: #265/#186 (allowlisted deserialization the terrain loader rides on)

## Goal
Test coverage for the engine's content parsers (per the 2026-06-10 audit: `ArmyListParser` splits,
`TerrainLayoutLoader`, `SpecialRuleRegistry` error paths), and confirm their error paths surface
displayable messages rather than throwing raw exceptions at callers.

## Notes
- 2026-09-03: Re-scoped against current code, since the audit doc this was filed from
  (`Audit-2026-07-06-New-Subsystems.md`) was deleted 2026-08-12 and the codebase moved on:
  - **`ArmyListParser`** no longer exists under that name — army lists are pure structured JSON
    (`ArmyListFile`) today, no free-text splitting/parsing step to test. Treating this facet as stale/
    superseded rather than inventing coverage for it.
  - **`SpecialRuleRegistry`** already has thorough dedicated coverage (`Tests/SpecialRuleRegistryTests.cs`,
    8 tests including an explicit rejection path — `Transport` with no numeric value). Nothing to add.
  - **`SpecialRuleEntryParser`** (`SaveLoad/SpecialRuleEntryParser.cs`) — the actual flat-string content
    parser in this area ("Bane", "Blast(3)", "Spawn(Spores [5])") — had zero tests. Added
    `Tests/SpecialRuleEntryParserTests.cs`, 10 tests covering all three parsed shapes, the
    space-before-paren vs hugging-paren distinction (#197 P17), and the malformed-input fallback
    contract (never throws, degrades to a plain core name).
  - **`TerrainLayoutLoader.TryLoadFromFile`** already returns a clean `(result, error)` pair with
    displayable messages for every failure (file not found, null deserialize, exception text) — but had
    no dedicated tests at all. Added `Tests/TerrainLayoutLoaderTests.cs`, 4 tests: missing file,
    malformed JSON, JSON `null` literal, and a valid round-trip through the polymorphic `IZone` shape
    (proving the allowlisted binder resolves it correctly end to end).

## Decisions
- No production code changes were needed: both real parsers already fail gracefully with displayable
  errors (`TerrainLayoutLoader`) or a documented degrade-not-throw contract (`SpecialRuleEntryParser`).
  This item turned out to be pure test-debt, not a bug fix.

## Outcome
Shipped 2026-09-03. Two new test files (14 tests total), full suite green (3166 passed). The
`ArmyListParser` facet from the original audit is stale — no equivalent surface exists in the current
codebase — and is being closed out rather than carried forward as a phantom TODO.
