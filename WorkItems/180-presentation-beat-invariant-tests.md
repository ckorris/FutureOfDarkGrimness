# 180 — Table-driven PresentationBeat invariant test

**Status**: done
**Related**: #056 (presentation beat stream)

## Goal
A single table-driven test proving every concrete `PresentationBeat` subclass satisfies the base
class's contract: `NominalDuration` (and `HoldLeadIn`) is never negative, and `Text` never throws.
Per-type serialization tests already existed (`PresentationBeatSerializationTests`), but nothing swept
the whole family uniformly, and nothing would catch a brand-new beat type shipping with no coverage at all.

## Notes
- 2026-09-03: Implemented. `Tests/PresentationBeatInvariantTests.cs`: one canonical instance per
  concrete beat (all 10: AttackBeat, BannerBeat x3 tiers, DiceRolledBeat, ModelDiedBeat,
  ModelWoundedBeat, RollOffBeat, SaveBeat, SpellEffectBeat, UnitMovedBeat, UnitRoutedBeat) via
  `TestCaseSource`, checked against `NominalDuration >= 0`, `HoldLeadIn >= 0`, and `Text` non-throwing.
  A fourth test (`AllConcreteBeatTypes_AreCoveredByTheTable`) reflects over the `FDG.Presentation.Beats`
  namespace and fails if a new beat type is added without a table entry — the actual guard against
  future regressions. Scoped to that namespace rather than the whole assembly because NUnit test
  doubles (`Tests/Doubles/PresentationDoubles.cs: TestBeat`) currently compile into the same assembly
  (#068) and aren't production beats.

## Decisions
- Reused sample data/shapes from the existing serialization tests where practical, to stay consistent
  with the codebase's existing beat-construction idioms rather than inventing new ones.

## Outcome
Shipped 2026-09-03. `dotnet test` full suite green (3166 passed, pre-existing skip count unchanged).
Pure test addition — no production code touched.
