using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FDG;
using FDG.ArmyBuilding;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #376 S3 - Ravage Aura over the DATA as shipped. Owner ruling: NOT an argumented grant (the grant path is name-only,
// LAT-1). It works because ResolveRavageWoundsStage SUMS the dice of one pool: one extra die per living model alongside
// core Ravage(X)'s X-per-carrier is arithmetically identical to every model having Ravage(X+1).
// #434 (3.6, "This model and its unit get Ravage(+1)."): moved to the GDF file (Daemons of Plague names it; AoF merges
// the GDF file first), and re-shaped as a real aura - the bearer grants "Ravage (Aura)", a literal one-die Ravage with
// Ravage's 3.6 Fatigue gate, to its unit, because since #434 the stage counts carriers per ATTACHMENT (#447's template)
// and a token grant is the one attachment that counts every living model, a joined hero included. The stage strips
// the "(Aura)" label, so the die joins the Ravage pool.
[TestFixture]
public class RavageAuraShippedDataTests
{
    private static IReadOnlyList<SpecialRuleDefinition> Supplement() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Books", "GdfRuleSupplement.json")));

    private static SpecialRuleDefinition Definition(string name) =>
        Supplement().Single(r => r.Name == name);

    [Test]
    public void RavageAura_GrantsTheOneDieHelper_ToItsUnit()
    {
        SpecialRuleDefinition aura = Definition("Ravage Aura");
        Assert.That(aura.Passive.Single().HookID, Is.EqualTo(EHookID.Lifecycle_OnUnitCreated));
        Assert.That(aura.Passive.Single().Effect, Is.EqualTo(new Effect.Aura("Ravage (Aura)")));
        Assert.That(aura.Description, Is.EqualTo("This model and its unit get Ravage(+1)."));

        HookEntry entry = Definition("Ravage (Aura)").Passive.Single();
        Assert.That(entry.HookID, Is.EqualTo(EHookID.Melee_OnChargeContact),
            "core Ravage's hook - the stage only consumes InvokeDealAutoWounds there.");
        Assert.That(entry.Condition, Is.EqualTo(new Condition.Not(new Condition.CountsAsFatiguedInMelee())),
            "Ravage's 3.6 'unless this unit is Fatigued'");
        Assert.That(entry.Effect, Is.EqualTo(new Effect.DealAutoWounds(new ValueSource.Literal(1), 6)),
            "one die per carrier, the same 6+ pool as core Ravage");
    }

    [Test]
    public void AuraAlone_ContributesOneDiePerLivingModel()
    {
        var harness = new Harness();
        IUnit attacker = harness.BuildUnit(models: 3, "Ravage Aura");
        IUnit defender = harness.BuildUnit(models: 1);

        Assert.That(harness.DiceAtSix(attacker, defender), Is.EqualTo(3),
            "a unit of 3 receiving Ravage(+1) rolls 3 extra dice.");
    }

    [Test]
    public void AuraOnTopOfCoreRavage_SumsToTheUpgradedRating()
    {
        var harness = new Harness();
        IUnit attacker = harness.BuildUnit(models: 2, "Ravage Aura");
        harness.AttachCoreRavage(attacker, rating: 2);
        IUnit defender = harness.BuildUnit(models: 1);

        Assert.That(harness.DiceAtSix(attacker, defender), Is.EqualTo(6),
            "Ravage(2) x 2 models + aura's 1 x 2 models = the 6 dice Ravage(3) x 2 would roll - " +
            "the stage sums ops within one threshold group.");
    }

    /// <summary>Live resolver + evaluator; the ops collected the way ResolveRavageWoundsStage does.</summary>
    private sealed class Harness
    {
        private readonly GameDataStore _store = GameDataStore.GameDataStoreBuilder.GetDefault();
        private readonly RuleResolver _resolver = new();
        private readonly RuleEvaluator _evaluator;

        public Harness()
        {
            var byName = Supplement().ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);
            _resolver.Register(byName["Ravage Aura"]);
            _resolver.Register(byName["Ravage (Aura)"]);
            _evaluator = new RuleEvaluator(new ProbabilisticDiceRoller(), ruleResolver: _resolver);
        }

        public IUnit BuildUnit(int models, params string[] ruleNames)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                    initialPosition: new Position(), gameDataStore: _store);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Unit", quality: 4, defense: 4,
                modelBindings: bindings);
            foreach (string name in ruleNames) unit.AttachRuleDefinition(_resolver.Resolve(name));
            // The aura lands as UnitCreationRules applies it: a RuleGrant token on the unit.
            OperationApplier.ApplyTokenOperations(_evaluator.Evaluate(unit, ERuleSeat.Actor, new UnitCreatedContext(unit)));
            return unit;
        }

        public void AttachCoreRavage(IUnit unit, int rating) =>
            ((UnitData)unit).AttachRuleDefinition(new ResolvedRule("Ravage", CoreRuleCatalog.Ravage,
                new RuleArgument[] { new RuleArgument.Int(rating) }));

        public int DiceAtSix(IUnit attacker, IUnit defender) =>
            _evaluator.EvaluateAll(new ChargeContactContext(attacker, defender),
                    FDG.Stages.ResolveImpactHitsStage.ChargerParticipant(attacker))
                .OfType<RuleOperation.InvokeDealAutoWounds>()
                .Where(op => op.SuccessThreshold == 6)
                .Sum(op => op.DiceCount);
    }
}
