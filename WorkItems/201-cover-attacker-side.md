# 201 — Cover granted by terrain on the attacker's side of the line

**Status**: todo (design fork open — do not build until the rules questions below are answered)
**Related**: #044-#046 (line-of-sight cluster), #150 (base-shape geometry), #055 (rule attribution in resolvers)

## Goal

Shooting *out of* cover must not grant the defender a cover bonus. Today, a unit standing right up
against a wall gets its own wall counted as the defender's cover. "Done" means the cover check only
considers terrain that meaningfully screens the *defender*, per rules answers agreed below, with an
integration test per accepted case.

## Reported symptom

2026-07-08 playtest: Battle Brothers positioned flush against a cover wall, firing outward. The
opposing unit — in the open — received the +1 defense cover bonus.

## Root cause

`ShootStage/FireStage/CoverCheckStage/CoverCheckStage.cs:29-36` asks, for each defending model, whether
*any* attacking model's sight line is `ESightLineEffect.Cover`:

```csharp
ESightLineEffect effect = LineOfSightUtilities.EvaluateSightLine(
    attacker.GetValue().PositionBinding.GetValue(), defPos, terrain);
if (effect == ESightLineEffect.Cover) { modelsInCover++; break; }
```

`LineOfSightUtilities.EvaluateSightLine` (`ShootStage/LineOfSightUtilities.cs:59-80`) folds every
terrain piece intersecting the attacker->defender segment into a worst-effect, with **no notion of
where along the segment the intersection happened**. A cover piece touching the segment one inch from
the *attacker* is indistinguishable from one touching it at the *defender*. The majority rule at
`CoverCheckStage.cs:40` (`modelsInCover * 2 > defenders.Count`) then turns that into +1 defense.

The same positional blindness applies to the model-as-blocker pieces built by `BuildModelBlockers`
(`LineOfSightUtilities.cs:26-58`), and to the mirror queries used for targeting/movement previews.

## Design fork — needs a ruling before any code

1. **Proximity.** How close must a cover piece be to the defender to screen it? Candidates: defender's
   base must be inside or touching the cover zone; within N inches of it (N = ?); or "the last terrain
   piece the segment crosses before reaching the defender."
2. **Depth / shoot-through.** How far can a shot travel *through* cover before the cover stops helping
   (or starts blocking)? Is there a maximum inches-of-cover-traversed, and does exceeding it degrade
   `Cover` to `Blocking`?
3. **Attacker-side exclusion.** Is it simply "ignore cover pieces that contain the attacker's model",
   or a general "ignore anything in the first X% / X inches of the segment"?
4. **Symmetry.** Does the same rule govern the defender shooting back, and does it govern melee /
   spell line-of-sight (`ResolveSpellDamageStage`, `StrafingStage`, `ResolveImpactHitsStage` all build
   `CoverCheckResults` the same way)?
5. **Interaction with existing rules.** `SightRuleQueries.IgnoresCover` (Blast) short-circuits the
   bonus at `CoverCheckStage.cs:44-49`; a proximity rule must compose with it, not duplicate it.

## Notes

- 2026-07-09: Filed from playtest feedback. Root cause confirmed by reading `CoverCheckStage` +
  `EvaluateSightLine`; the segment is evaluated as an unordered set of intersections. Deliberately
  **not** fixed in the same pass as the other playtest bugs — the fix is a rules decision, not an
  engineering one, and a naive "exclude terrain containing the attacker" patch would silently pick an
  answer to questions 1-3.
- Existing tests that pin current behavior: `Tests/CoverMajorityTests.cs`, `Tests/BlastCoverRuleIntegrationTests.cs`.
  Both place defenders in cover with attackers in the open, so neither would catch this; new cases must
  put the *attacker* in cover.

## Decisions

(none yet — see Design fork)

## Outcome

(open)
