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

// #376 S5 - Bloodthirsty Fighter over the DATA as shipped. The engine's
// BloodthirstyFighterRuleIntegrationTests prove the mechanism (real follow-up batch, no chaining,
// dead-defender lapse) and CombatMathPinTests the AI pricing; these prove the supplement authors
// the rule the way that mechanism expects: melee-gated addBonusAttack at save-complete, Actor
// seat, Unit scope (both carrier units in the corpus hold it as a base unit rule, which the
// stage's model-less Actor seat sees).
[TestFixture]
public class BloodthirstyShippedDataTests
{
    private static IReadOnlyList<SpecialRuleDefinition> Supplement() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Books", "AofRuleSupplement.json")));

    [Test]
    public void BloodthirstyFighter_EarnsAttacksFromBlockOnes_InMeleeOnly()
    {
        SpecialRuleDefinition rule = Supplement().Single(r => r.Name == "Bloodthirsty Fighter");
        Assert.That(rule.Scope, Is.EqualTo(ERuleScope.Unit));

        HookEntry entry = rule.Passive.Single();
        Assert.That(entry.HookID, Is.EqualTo(EHookID.Shooting_OnSaveRollComplete));
        Assert.That(entry.Seat, Is.EqualTo(ERuleSeat.Actor));
        Assert.That(entry.Condition, Is.InstanceOf<Condition.IsMelee>(),
            "the shared save hook serves shooting too; the melee gate is the rule's own text.");

        var effect = (Effect.AddBonusAttack)entry.Effect;
        Assert.That(effect.OnRollValue, Is.EqualTo(1));
        Assert.That(effect.Count, Is.EqualTo(1));
    }

    [Test]
    public void ShippedDef_EarnsOnePerNaturalBlockOne_ThroughTheLiveEvaluator()
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var resolver = new RuleResolver();
        resolver.Register(Supplement().Single(r => r.Name == "Bloodthirsty Fighter"));
        var evaluator = new RuleEvaluator(new ProbabilisticDiceRoller(), ruleResolver: resolver);

        IUnit BuildUnit(params string[] rules)
        {
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                initialPosition: new Position(), gameDataStore: store);
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Unit", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>>
                    { store.GetDataBinding<ModelData>(store.Create(model)) });
            foreach (string name in rules) unit.AttachRuleDefinition(resolver.Resolve(name));
            return unit;
        }

        IUnit bearer = BuildUnit("Bloodthirsty Fighter");
        IUnit enemy = BuildUnit();
        var perSide = new float[6];
        perSide[0] = 2f; // two natural 1s
        perSide[3] = 1f;

        float Earned(bool isMelee)
        {
            var sink = new BonusAttackSink();
            sink.ApplyFrom(evaluator.EvaluateAll(
                new SaveRollCompleteContext(bearer, enemy, new DiceResults(perSide), IsMelee: isMelee),
                RuleParticipant.Actor(bearer)));
            return sink.TotalBonusAttacks;
        }

        Assert.That(Earned(isMelee: true), Is.EqualTo(2f));
        Assert.That(Earned(isMelee: false), Is.EqualTo(0f), "shooting blocks earn nothing.");
    }
}
