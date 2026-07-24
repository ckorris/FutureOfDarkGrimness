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

// #197 misc - Hazardous. Only the AP(4) half ships as data (a weapon-scoped Actor-seat save modifier, the
// Thrust-AP pattern). The self-wound-on-unmodified-1 half is DEFERRED: it needs a mid-attack wound
// application against the ATTACKER's own unit (a new effect + executable + IOperationServices wound method +
// an execution point in RollToHitStage), which is a wound-subsystem hook, not a small primitive - recorded
// in the #197 ledger, and flagged because until it lands Hazardous is upside-only. These tests pin the AP.
[TestFixture]
public class HazardousShippedDataTests
{
    private static SpecialRuleDefinition Hazardous() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Assets", "Books", "GdfRuleSupplement.json")))
            .Single(r => r.Name == "Hazardous");

    [Test]
    public void Hazardous_IsWeaponScoped_GrantingAPFourAsAnActorSaveModifier()
    {
        SpecialRuleDefinition rule = Hazardous();
        Assert.That(rule.Scope, Is.EqualTo(ERuleScope.Weapon));

        HookEntry entry = rule.Passive.Single();
        Assert.That(entry.HookID, Is.EqualTo(EHookID.Shooting_OnHitRollComplete),
            "AP folds at the save-complete hook, like Thrust's AP.");
        Assert.That(entry.Seat, Is.EqualTo(ERuleSeat.Actor), "the ATTACKER worsens the defender's save.");
        Assert.That(entry.Effect, Is.InstanceOf<Effect.RollModifier>());
        var mod = (Effect.RollModifier)entry.Effect;
        Assert.That(mod.RollKind, Is.EqualTo(ERollKind.Save));
        Assert.That(mod.Delta, Is.EqualTo(-4), "AP(4) = -4 to the enemy save.");
    }

    [Test]
    public void Hazardous_NetsMinusFourToTheDefendersSave()
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var resolver = new RuleResolver();
        resolver.Register(Hazardous());
        var evaluator = new RuleEvaluator(new ProbabilisticDiceRoller(), ruleResolver: resolver);

        IUnit attacker = BuildUnit(store, "P1");
        IUnit defender = BuildUnit(store, "P2");
        var weapon = new Weapon("Plas-Burst", rangeInches: 24f, attacks: 1, armorPenetration: 0);
        weapon.AttachRuleDefinition(resolver.Resolve("Hazardous"));

        var sink = new RollModifierSink();
        sink.ApplyFrom(evaluator.EvaluateAll(
            new HitRollCompleteContext(attacker, defender, Faces(4), DistanceInches: 6f),
            RuleParticipant.Actor(attacker, weapon)));

        Assert.That(sink.Net(ERollKind.Save), Is.EqualTo(-4));
    }

    private static IUnit BuildUnit(GameDataStore store, string name)
    {
        var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
            initialPosition: new Position(), gameDataStore: store);
        DataBinding<ModelData> binding = store.GetDataBinding<ModelData>(store.Create(model));
        return new UnitData(new PlayerID(Guid.NewGuid()), name, quality: 4, defense: 4,
            modelBindings: new List<DataBinding<ModelData>> { binding });
    }

    private static DiceResults Faces(params int[] faces)
    {
        var perSide = new float[6];
        foreach (int face in faces) perSide[face - 1] += 1f;
        return new DiceResults(perSide);
    }
}
