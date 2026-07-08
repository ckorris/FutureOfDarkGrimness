# 185 — Replace RuleEvaluator participant tuples with a `RuleParticipant` struct

**Status**: open — spec ready, mechanical refactor (surfaced during #183)
**Related**: #093 (added the 5-field model-aware form), #027 (added the weapon form), #183 (widened many call sites to 5-tuples)
**All work is engine-side (submodule)** — treat as authorized when this item is picked up (it only touches the rule-dispatch API and its call sites).

## Goal
Replace the positional `ValueTuple` participant descriptors passed to `RuleEvaluator.EvaluateAll` /
`EvaluateAllNamed` with a single `readonly struct RuleParticipant`. This is a **pure mechanical,
behavior-preserving refactor** — no rule semantics change. The full engine test suite (1308 tests as of
#183) plus a headless smoke are the proof.

## Why (no runtime penalty)
`ValueTuple` is already a struct, so a `readonly struct RuleParticipant` with the same fields has
**identical** memory layout and cost — inline in the `params` array, no heap allocation, no boxing. (A
*class* would add one heap allocation per participant per evaluation on a hot path — do NOT use a class.)

The tuples force an overload+shim explosion purely to fake optional fields: there are currently **6 public
overloads** (`EvaluateAll` ×3, `EvaluateAllNamed` ×3) and **2 private shims** (`WithoutWeapons`,
`WithModels`) in `Rules/Dispatch/RuleEvaluator.cs`, all to pad 2-field → 3-field → 5-field tuples. A struct
with defaulted fields collapses each trio into ONE method and deletes both shims. It also erases the
`(IReadOnlyList<IModel>?)null, EModelRuleScope.AnyOwner` cast-noise #183 sprinkled across the call sites.

## The struct (new file `Rules/Dispatch/RuleParticipant.cs`)
```csharp
namespace FDG.Rules.Dispatch;

/// <summary>
/// One participant in a rule evaluation: a unit playing a seat (Actor/Subject), optionally contributing a
/// weapon's rules (#027) and/or specific models' per-model rules composed per EModelRuleScope (#093/#183).
/// A readonly struct so it costs exactly what the old ValueTuple did (inline, no allocation).
/// </summary>
public readonly struct RuleParticipant
{
    public IUnit Unit { get; }
    public ERuleSeat Seat { get; }
    public IWeapon? Weapon { get; }
    public IReadOnlyList<IModel>? Models { get; }
    public EModelRuleScope ModelScope { get; }

    public RuleParticipant(IUnit unit, ERuleSeat seat, IWeapon? weapon = null,
        IReadOnlyList<IModel>? models = null, EModelRuleScope modelScope = EModelRuleScope.AnyOwner)
    {
        Unit = unit; Seat = seat; Weapon = weapon; Models = models; ModelScope = modelScope;
    }

    public static RuleParticipant Actor(IUnit unit, IWeapon? weapon = null,
        IReadOnlyList<IModel>? models = null, EModelRuleScope modelScope = EModelRuleScope.AnyOwner)
        => new(unit, ERuleSeat.Actor, weapon, models, modelScope);

    public static RuleParticipant Subject(IUnit unit, IWeapon? weapon = null,
        IReadOnlyList<IModel>? models = null, EModelRuleScope modelScope = EModelRuleScope.AnyOwner)
        => new(unit, ERuleSeat.Subject, weapon, models, modelScope);
}
```
(Needs `using FDG.Rules.Foundation;` for `IUnit`/`IModel`/`ERuleSeat`, and `System.Collections.Generic`.)

## RuleEvaluator changes (`Rules/Dispatch/RuleEvaluator.cs`)
1. **Collapse the 6 overloads into 2** (keep the XML docs, merged):
   ```csharp
   public IReadOnlyList<RuleOperation> EvaluateAll(IHookContext context, params RuleParticipant[] participants)
       => CollectSurviving(context, log: true, participants).Select(t => t.Op).ToList();

   public IReadOnlyList<(RuleOperation Op, string RuleName)> EvaluateAllNamed(IHookContext context,
       params RuleParticipant[] participants)
       => CollectSurviving(context, log: false, participants).Select(t => (t.Op, t.Origin.RequestedName)).ToList();
   ```
2. **Delete** `WithoutWeapons` and `WithModels` (lines ~147-170).
3. **`CollectSurviving`** signature → `params RuleParticipant[] participants`. Its `foreach` deconstruct
   (line ~205) becomes field access:
   ```csharp
   foreach (RuleParticipant p in participants)
       CollectTagged(p.Unit, p.Seat, p.Weapon, p.Models, p.ModelScope, context, tagged, seen, grantsToConsume, trace);
   ```
   The trace line (~190) already uses `p.Weapon`/`p.Seat`/`p.Unit` named-tuple access — with the struct's
   identically-named properties it compiles unchanged. **Keep that trace string byte-for-byte** (RuleTraceTests
   match on it).
4. Leave the single-participant `Evaluate(IUnit unit, ERuleSeat seat, IHookContext context, ...)` (line ~53)
   **unchanged** — it takes positional params, not a tuple, and is already readable. Out of scope.

## Call-site migration (mechanical — let the compiler drive it)
Delete the overloads first; every multi-participant call site becomes a compile error. Convert each:
- `(unit, ERuleSeat.Actor)` → `RuleParticipant.Actor(unit)`
- `(unit, ERuleSeat.Subject)` → `RuleParticipant.Subject(unit)`
- `(unit, ERuleSeat.Actor, weapon)` → `RuleParticipant.Actor(unit, weapon)`
- `(defender, ERuleSeat.Subject, (IWeapon?)null)` → `RuleParticipant.Subject(defender)`
- `(attacker, ERuleSeat.Actor, weaponType, HeroStatRules.LivingWeaponBatchOwners(...), EModelRuleScope.AllOwners)`
  → `RuleParticipant.Actor(attacker, weaponType, HeroStatRules.LivingWeaponBatchOwners(...), EModelRuleScope.AllOwners)`
- `(defender, ERuleSeat.Subject, (IWeapon?)null, HeroStatRules.LivingModels(defender), EModelRuleScope.AnyOwner)`
  → `RuleParticipant.Subject(defender, models: HeroStatRules.LivingModels(defender))`
  (AnyOwner is the default, so it drops out — the cast noise disappears.)

Known site clusters (non-test): `RangeRuleQueries`, `MovementRuleQueries`, `DetermineHitRollStage`,
`RollToHitStage`, `AssignWoundsStage`, `ResolveSpellDamageStage`, `UnitDestructionNotifier`,
`ResolveImpactHitsStage`, and the helper `DetermineStrikeOrderStage.SubjectWithMeleeWeapons` (which returns
the 5-tuple array today — change its return type to `RuleParticipant[]` and its two callers accordingly).
~78 non-test + ~45 test references to `EvaluateAll`/`EvaluateAllNamed` exist, but MANY are the
single-participant `Evaluate` form (unchanged) — only the multi-participant ones convert. Tests use the same
forms; migrate them the same way.

Optional nicety (only if it reduces churn cleanly): the `SubjectWithMeleeWeapons` list can be built as
`List<RuleParticipant>` directly.

## Verification (per CLAUDE.md — never commit red)
- `dotnet test FutureOfDarkGrimness/FutureOfDarkGrimness.csproj` green (expect the same 1308 as #183 — no
  test should need a semantic change; only participant-construction syntax updates).
- Full `dotnet build` clean.
- Headless smoke: `printf "2\n2\n" | dotnet run --project FdgRaylib/FdgRaylib.csproj -- --headless` exits 0.
- Submodule-first commit cadence: commit the engine, then bump the superproject pointer.

## Notes
- 2026-07-08: Spec written during #183 (whose slice-2 wiring made the tuple noise obvious). Deliberately kept
  as its own item so #183 stayed a focused feature diff and this stays a pure, separately-reviewable refactor.
