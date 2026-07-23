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
using FDG.Utilities;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #197 misc - Protection Feat (9 refs), over the shipped JSON. The engine's
// ProtectionFeatRuleIntegrationTests prove the mechanism; these pin that the data authors it as a single
// once-per-game activation-start ability (Yes/No brace) granting an UntilNextActivation roll-per-wound 5+
// ignore.
[TestFixture]
public class ProtectionFeatShippedDataTests
{
    private static IReadOnlyList<SpecialRuleDefinition> Supplement() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Books", "GdfRuleSupplement.json")));

    private static SpecialRuleDefinition Definition(string name) => Supplement().Single(r => r.Name == name);

    [Test]
    public void ProtectionFeat_IsASingleOncePerGameBrace_GrantingTheGuardUntilNextActivation()
    {
        SpecialRuleDefinition rule = Definition("Protection Feat");
        Assert.That(rule.Activated, Has.Count.EqualTo(1));

        ActivatedAbility ability = rule.Activated.Single();
        Assert.That(ability.TriggerHook, Is.EqualTo(EHookID.Activation_OnActivationStart));
        Assert.That(ability.Cost, Is.InstanceOf<Cost.OncePerGame>());
        Assert.That(ability.AvailableWhen, Is.InstanceOf<Condition.AllModelsHaveThisRule>());

        var grant = (Effect.AddRule)ability.Effect;
        Assert.That(grant.RuleName, Is.EqualTo("Protection Feat Guard"));
        Assert.That(grant.Scope, Is.EqualTo(ELifetime.UntilNextActivation),
            "the brace must survive the opponent's turn, which is when the wounds land.");
    }

    [Test]
    public void TheGuard_IgnoresEachWoundOnAFivePlus()
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var resolver = new RuleResolver();
        resolver.Register(Definition("Protection Feat Guard"));
        var evaluator = new RuleEvaluator(new ProbabilisticDiceRoller(), ruleResolver: resolver);

        IUnit defender = BuildUnit(store, "P1", "Protection Feat Guard");
        IUnit attacker = BuildUnit(store, "P2");

        var sink = new WoundIgnoreSink();
        sink.ApplyFrom(evaluator.EvaluateAll(
            new SaveRollCompleteContext(attacker, defender, Faces(4)), RuleParticipant.Subject(defender)));

        Assert.That(sink.HasIgnore, Is.True);
        Assert.That(sink.Threshold, Is.EqualTo(5));
    }

    [Test]
    public void TheAura_ConfersProtectionFeat()
    {
        HookEntry entry = Definition("Protection Feat Aura").Passive.Single();
        Assert.That(entry.Effect, Is.InstanceOf<Effect.Aura>());
        Assert.That(((Effect.Aura)entry.Effect).RuleName, Is.EqualTo("Protection Feat"));
    }

    private static IUnit BuildUnit(GameDataStore store, string name, params string[] rules)
    {
        var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
            initialPosition: new Position(), gameDataStore: store);
        var unit = new UnitData(new PlayerID(Guid.NewGuid()), name, quality: 4, defense: 4,
            modelBindings: new List<DataBinding<ModelData>>
                { store.GetDataBinding<ModelData>(store.Create(model)) });
        foreach (string r in rules) unit.AttachRuleDefinition(new ResolvedRule(r,
            Supplement().Single(d => d.Name == r)));
        return unit;
    }

    private static DiceResults Faces(params int[] faces)
    {
        var perSide = new float[6];
        foreach (int face in faces) perSide[face - 1] += 1f;
        return new DiceResults(perSide);
    }
}
