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

// #197 misc - the Grounded family (Grounded Reinforcement / Precision / Stealth, 8 refs), over the DATA as
// shipped. The engine's GroundedTerrainRuleIntegrationTests prove the mechanism and geometry; these prove
// the supplement authors each rule the way that mechanism expects - the right hook and seat (a Hit delta on
// the wrong hook nets zero; the wrong seat aims the modifier at the wrong side), the terrain gate, and that
// each Aura confers its base rule.
[TestFixture]
public class GroundedShippedDataTests
{
    private static IReadOnlyList<SpecialRuleDefinition> Supplement() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Books", "GdfRuleSupplement.json")));

    private static SpecialRuleDefinition Definition(string name) =>
        Supplement().Single(r => r.Name == name);

    // ---- Structure: the terrain gate and the right hook/seat/effect ---------------------------------

    [TestCase("Grounded Reinforcement", EHookID.Shooting_OnHitRollComplete, ERuleSeat.Subject,
        ERollKind.Save, 1)]
    [TestCase("Grounded Precision", EHookID.Shooting_OnHitRollModifier, ERuleSeat.Actor,
        ERollKind.Hit, 1)]
    [TestCase("Grounded Stealth", EHookID.Shooting_OnHitRollModifier, ERuleSeat.Subject,
        ERollKind.Hit, -1)]
    public void EachRule_GatesOnTerrain_AndAppliesTheRightModifierAtTheRightHookAndSeat(
        string name, EHookID hook, ERuleSeat seat, ERollKind roll, int delta)
    {
        HookEntry entry = Definition(name).Passive.Single();

        Assert.That(entry.HookID, Is.EqualTo(hook), "a Hit/Save delta on the wrong hook is never consumed.");
        Assert.That(entry.Seat, Is.EqualTo(seat));

        Assert.That(entry.Condition, Is.InstanceOf<Condition.And>());
        var and = (Condition.And)entry.Condition;
        Assert.That(and.Left, Is.InstanceOf<Condition.AllModelsHaveThisRule>(),
            "'a unit where all models have this rule'.");
        Assert.That(and.Right, Is.InstanceOf<Condition.MostModelsWithinInchesOfTerrain>(),
            "'... has most of them within 1in of terrain'.");
        Assert.That(((Condition.MostModelsWithinInchesOfTerrain)and.Right).DistanceInches, Is.EqualTo(1f));

        Assert.That(entry.Effect, Is.InstanceOf<Effect.RollModifier>());
        var mod = (Effect.RollModifier)entry.Effect;
        Assert.That(mod.RollKind, Is.EqualTo(roll));
        Assert.That(mod.Delta, Is.EqualTo(delta));
    }

    [TestCase("Grounded Reinforcement Aura", "Grounded Reinforcement")]
    [TestCase("Grounded Precision Aura", "Grounded Precision")]
    public void EachAura_ConfersItsBaseRule(string auraName, string baseName)
    {
        HookEntry entry = Definition(auraName).Passive.Single();
        Assert.That(entry.HookID, Is.EqualTo(EHookID.Lifecycle_OnUnitCreated));
        Assert.That(entry.Effect, Is.InstanceOf<Effect.Aura>());
        Assert.That(((Effect.Aura)entry.Effect).RuleName, Is.EqualTo(baseName));
    }

    // ---- Net effect over shipped data, gated on terrain ---------------------------------------------

    [Test]
    public void GroundedReinforcement_NetsPlusOneDefense_OnlyWhenMostModelsAreInTerrain()
    {
        var harness = new Harness("Grounded Reinforcement");
        IUnit defender = harness.BuildUnit("P1", "Grounded Reinforcement"); // models at origin
        IUnit attacker = harness.BuildUnit("P2");

        Assert.That(harness.NetSave(attacker, defender, Harness.OriginTerrain), Is.EqualTo(1));
        Assert.That(harness.NetSave(attacker, defender, null), Is.EqualTo(0),
            "no terrain near the unit -> no bonus.");
    }

    [Test]
    public void GroundedStealth_NetsMinusOneToEnemyHit_OnlyWhenMostModelsAreInTerrain()
    {
        var harness = new Harness("Grounded Stealth");
        IUnit defender = harness.BuildUnit("P1", "Grounded Stealth");
        IUnit attacker = harness.BuildUnit("P2");

        Assert.That(harness.NetEnemyHit(attacker, defender, Harness.OriginTerrain), Is.EqualTo(-1));
        Assert.That(harness.NetEnemyHit(attacker, defender, null), Is.EqualTo(0));
    }

    [Test]
    public void GroundedPrecision_NetsPlusOneToOwnHit_OnlyWhenMostModelsAreInTerrain()
    {
        var harness = new Harness("Grounded Precision");
        IUnit attacker = harness.BuildUnit("P1", "Grounded Precision");
        IUnit defender = harness.BuildUnit("P2");

        Assert.That(harness.NetOwnHit(attacker, defender, Harness.OriginTerrain), Is.EqualTo(1),
            "Precision buffs the bearer's OWN hit rolls (Actor seat).");
        Assert.That(harness.NetOwnHit(attacker, defender, null), Is.EqualTo(0));
    }

    /// <summary>Live resolver + evaluator over the shipped definitions, with terrain populated on the hit
    /// context and models left at the origin (so OriginTerrain covers them).</summary>
    private sealed class Harness
    {
        public static readonly IReadOnlyList<ITerrain> OriginTerrain =
            new ITerrain[] { new TerrainData(ETerrainType.Cover, new CircularZone(new Float2(0f, 0f), 3f)) };

        private readonly GameDataStore _store = GameDataStore.GameDataStoreBuilder.GetDefault();
        private readonly RuleResolver _resolver = new();
        private readonly RuleEvaluator _evaluator;

        public Harness(params string[] ruleNames)
        {
            var byName = Supplement().ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);
            foreach (string name in ruleNames) _resolver.Register(byName[name]);
            _evaluator = new RuleEvaluator(new ProbabilisticDiceRoller(), ruleResolver: _resolver);
        }

        public IUnit BuildUnit(string playerName, params string[] ruleNames)
        {
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                initialPosition: new Position(), gameDataStore: _store);
            DataBinding<ModelData> binding = _store.GetDataBinding<ModelData>(_store.Create(model));
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), playerName, quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { binding });
            foreach (string name in ruleNames) unit.AttachRuleDefinition(_resolver.Resolve(name));
            return unit;
        }

        public int NetSave(IUnit attacker, IUnit defender, IReadOnlyList<ITerrain>? terrain)
        {
            var sink = new RollModifierSink();
            sink.ApplyFrom(_evaluator.EvaluateAll(
                new HitRollCompleteContext(attacker, defender, Faces(4), DistanceInches: 6f,
                    TerrainPieces: terrain),
                RuleParticipant.Subject(defender)));
            return sink.Net(ERollKind.Save);
        }

        public int NetEnemyHit(IUnit attacker, IUnit defender, IReadOnlyList<ITerrain>? terrain)
        {
            var sink = new RollModifierSink();
            sink.ApplyFrom(_evaluator.EvaluateAll(
                new HitRollModifierContext(attacker, defender, DistanceInches: 6f, TerrainPieces: terrain),
                RuleParticipant.Subject(defender)));
            return sink.Net(ERollKind.Hit);
        }

        public int NetOwnHit(IUnit attacker, IUnit defender, IReadOnlyList<ITerrain>? terrain)
        {
            var sink = new RollModifierSink();
            sink.ApplyFrom(_evaluator.EvaluateAll(
                new HitRollModifierContext(attacker, defender, DistanceInches: 6f, TerrainPieces: terrain),
                RuleParticipant.Actor(attacker)));
            return sink.Net(ERollKind.Hit);
        }

        private static DiceResults Faces(params int[] faces)
        {
            var perSide = new float[6];
            foreach (int face in faces) perSide[face - 1] += 1f;
            return new DiceResults(perSide);
        }
    }
}
