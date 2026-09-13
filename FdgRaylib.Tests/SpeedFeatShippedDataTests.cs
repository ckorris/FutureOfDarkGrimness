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

// #197 misc - Speed Feat (5 refs, the Aura form in Orc Marauders), over the shipped JSON. The engine's
// SpeedFeatRuleIntegrationTests prove the mechanism; these pin that the data authors it the way the stage
// expects: a SINGLE once-per-game activation-start ability (so the stage offers Yes/No, not a forced pick)
// granting the boost, and the boost's +2/+4 movement.
[TestFixture]
public class SpeedFeatShippedDataTests
{
    private static IReadOnlyList<SpecialRuleDefinition> Supplement() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Books", "GdfRuleSupplement.json")));

    private static SpecialRuleDefinition Definition(string name) => Supplement().Single(r => r.Name == name);

    [Test]
    public void SpeedFeat_IsASingleOncePerGameActivationStartAbility_GrantingTheBoost()
    {
        SpecialRuleDefinition rule = Definition("Speed Feat");
        Assert.That(rule.Activated, Has.Count.EqualTo(1),
            "exactly one ability -> the stage offers it as an optional Yes/No, not a forced pick.");

        ActivatedAbility ability = rule.Activated.Single();
        Assert.That(ability.TriggerHook, Is.EqualTo(EHookID.Activation_OnActivationStart));
        Assert.That(ability.Cost, Is.InstanceOf<Cost.OncePerGame>());
        Assert.That(ability.AvailableWhen, Is.InstanceOf<Condition.AllModelsHaveThisRule>());
        Assert.That(ability.Effect, Is.InstanceOf<Effect.AddRule>());
        var grant = (Effect.AddRule)ability.Effect;
        Assert.That(grant.RuleName, Is.EqualTo("Speed Feat Boost"));
        Assert.That(grant.Scope, Is.EqualTo(ELifetime.ThisActivation));
    }

    [Test]
    public void SpeedFeatBoost_GivesPlusTwoAdvance_AndPlusFourRushAndCharge()
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var resolver = new RuleResolver();
        resolver.Register(Definition("Speed Feat Boost"));
        var evaluator = new RuleEvaluator(new ProbabilisticDiceRoller(), ruleResolver: resolver);

        var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
            initialPosition: new Position(), gameDataStore: store);
        var unit = new UnitData(new PlayerID(Guid.NewGuid()), "P1", quality: 4, defense: 4,
            modelBindings: new List<DataBinding<ModelData>>
                { store.GetDataBinding<ModelData>(store.Create(model)) });
        unit.AttachRuleDefinition(resolver.Resolve("Speed Feat Boost"));

        Assert.That(Net(evaluator, unit, EActionType.Advance), Is.EqualTo(2f));
        Assert.That(Net(evaluator, unit, EActionType.Rush), Is.EqualTo(4f));
        Assert.That(Net(evaluator, unit, EActionType.Charge), Is.EqualTo(4f));
    }

    [Test]
    public void TheAura_ConfersSpeedFeat()
    {
        HookEntry entry = Definition("Speed Feat Aura").Passive.Single();
        Assert.That(entry.Effect, Is.InstanceOf<Effect.Aura>());
        Assert.That(((Effect.Aura)entry.Effect).RuleName, Is.EqualTo("Speed Feat"));
    }

    private static float Net(RuleEvaluator evaluator, IUnit unit, EActionType action)
    {
        var sink = new MovementModifierSink();
        sink.ApplyFrom(evaluator.EvaluateAll(new MoveActionDeclaredContext(unit, action, BaseDistanceInches: 6f),
            RuleParticipant.Actor(unit)));
        return sink.Net(action);
    }
}
