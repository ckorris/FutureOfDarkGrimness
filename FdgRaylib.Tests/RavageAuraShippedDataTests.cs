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

// #376 S3 - Ravage Aura over the DATA as shipped. Owner ruling: a data-only standalone def, NOT an
// argumented grant (the grant path is name-only, LAT-1). It works because ResolveRavageWoundsStage
// groups InvokeDealAutoWounds ops by threshold and SUMS the dice: a Unit-scoped def contributing 1
// die per living model alongside core Ravage(X)'s X-per-carrier is arithmetically identical to every
// model having Ravage(X+1). Champion items fold their rules into unit.SpecialRules at list-compile
// (ListCompiler.AddGrantedRule), so the def fires from the unit at the stage's model-less Actor seat.
[TestFixture]
public class RavageAuraShippedDataTests
{
    private static IReadOnlyList<SpecialRuleDefinition> Supplement() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Books", "AofRuleSupplement.json")));

    private static SpecialRuleDefinition Definition(string name) =>
        Supplement().Single(r => r.Name == name);

    [Test]
    public void RavageAura_DealsOneAutoWoundDiePerModel_AtChargeContact()
    {
        SpecialRuleDefinition rule = Definition("Ravage Aura");
        Assert.That(rule.Scope, Is.EqualTo(ERuleScope.Unit),
            "unit scope makes DealAutoWounds count every living model - the 'and its unit' half.");

        HookEntry entry = rule.Passive.Single();
        Assert.That(entry.HookID, Is.EqualTo(EHookID.Melee_OnChargeContact),
            "core Ravage's hook - the stage only consumes InvokeDealAutoWounds there.");
        var effect = (Effect.DealAutoWounds)entry.Effect;
        Assert.That(((ValueSource.Literal)effect.DiceCountPerModel).Value, Is.EqualTo(1));
        Assert.That(effect.SuccessThreshold, Is.EqualTo(6), "same threshold pool as core Ravage.");
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
            return unit;
        }

        public void AttachCoreRavage(IUnit unit, int rating) =>
            ((UnitData)unit).AttachRuleDefinition(new ResolvedRule("Ravage", CoreRuleCatalog.Ravage,
                new RuleArgument[] { new RuleArgument.Int(rating) }));

        public int DiceAtSix(IUnit attacker, IUnit defender) =>
            _evaluator.EvaluateAll(new ChargeContactContext(attacker, defender),
                    RuleParticipant.Actor(attacker))
                .OfType<RuleOperation.InvokeDealAutoWounds>()
                .Where(op => op.SuccessThreshold == 6)
                .Sum(op => op.DiceCount);
    }
}
