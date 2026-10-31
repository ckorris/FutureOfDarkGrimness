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

// #376 S1 - the AoF Grounded Speed / Grounded Protection (+ Aura) trio over the DATA as shipped, the
// AoF twin of GroundedShippedDataTests. The engine's GroundedSpeedProtectionRuleIntegrationTests prove
// the two new IHasTerrain carriers; these prove the supplement authors each rule the way that mechanism
// expects. Speed's per-action gate matters doubly: the budget queries fire the movement hook once per
// action type onto one sink, so an ungated entry would count three times (the Ethereal shape).
[TestFixture]
public class GroundedAofShippedDataTests
{
    private static IReadOnlyList<SpecialRuleDefinition> Supplement() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Books", "AofRuleSupplement.json")));

    private static SpecialRuleDefinition Definition(string name) =>
        Supplement().Single(r => r.Name == name);

    // ---- Structure ----------------------------------------------------------------------------------

    [TestCase(EActionType.Advance, 2f)]
    [TestCase(EActionType.Rush, 4f)]
    [TestCase(EActionType.Charge, 4f)]
    public void GroundedSpeed_EachEntry_GatesOnActionAndTerrain(EActionType action, float bonus)
    {
        SpecialRuleDefinition rule = Definition("Grounded Speed");
        Assert.That(rule.Passive, Has.Count.EqualTo(3));

        HookEntry entry = rule.Passive.Single(e =>
            e.Effect is Effect.MovementBonus mb && mb.ActionType == action);
        Assert.That(entry.HookID, Is.EqualTo(EHookID.Movement_OnMoveActionDeclared));
        Assert.That(entry.Seat, Is.EqualTo(ERuleSeat.Actor));
        Assert.That(((Effect.MovementBonus)entry.Effect).DistanceInches, Is.EqualTo(bonus));

        var and = (Condition.And)entry.Condition;
        Assert.That(and.Left, Is.InstanceOf<Condition.ActionTypeIs>(),
            "without the per-action gate, the three-action budget fold counts the entry three times.");
        Assert.That(((Condition.ActionTypeIs)and.Left).ActionType, Is.EqualTo(action));
        var gate = (Condition.And)and.Right;
        Assert.That(gate.Left, Is.InstanceOf<Condition.AllModelsHaveThisRule>());
        Assert.That(gate.Right, Is.InstanceOf<Condition.MostModelsWithinInchesOfTerrain>());
        Assert.That(((Condition.MostModelsWithinInchesOfTerrain)gate.Right).DistanceInches, Is.EqualTo(1f));
    }

    [Test]
    public void GroundedProtection_IgnoresWoundsOnFivePlus_AtSaveComplete_SubjectSeat()
    {
        HookEntry entry = Definition("Grounded Protection").Passive.Single();
        Assert.That(entry.HookID, Is.EqualTo(EHookID.Shooting_OnSaveRollComplete));
        Assert.That(entry.Seat, Is.EqualTo(ERuleSeat.Subject));

        var and = (Condition.And)entry.Condition;
        Assert.That(and.Left, Is.InstanceOf<Condition.AllModelsHaveThisRule>());
        Assert.That(and.Right, Is.InstanceOf<Condition.MostModelsWithinInchesOfTerrain>());
        Assert.That(((Condition.MostModelsWithinInchesOfTerrain)and.Right).DistanceInches, Is.EqualTo(1f));

        Assert.That(entry.Effect, Is.InstanceOf<Effect.IgnoreWoundOnRoll>());
        Assert.That(((Effect.IgnoreWoundOnRoll)entry.Effect).MinRoll, Is.EqualTo(5));
    }

    [Test]
    public void GroundedProtectionAura_ConfersTheBaseRule()
    {
        HookEntry entry = Definition("Grounded Protection Aura").Passive.Single();
        Assert.That(entry.HookID, Is.EqualTo(EHookID.Lifecycle_OnUnitCreated));
        Assert.That(entry.Effect, Is.InstanceOf<Effect.Aura>());
        Assert.That(((Effect.Aura)entry.Effect).RuleName, Is.EqualTo("Grounded Protection"));
    }

    // ---- Net effect over shipped data, gated on terrain ---------------------------------------------

    [Test]
    public void GroundedSpeed_NetsItsBonus_OnlyWhenMostModelsAreInTerrain()
    {
        var harness = new Harness("Grounded Speed");
        IUnit unit = harness.BuildUnit("P1", "Grounded Speed"); // models at origin

        Assert.That(harness.NetMove(unit, EActionType.Advance, Harness.OriginTerrain), Is.EqualTo(2f));
        Assert.That(harness.NetMove(unit, EActionType.Rush, Harness.OriginTerrain), Is.EqualTo(4f));
        Assert.That(harness.NetMove(unit, EActionType.Charge, Harness.OriginTerrain), Is.EqualTo(4f));
        Assert.That(harness.NetMove(unit, EActionType.Advance, null), Is.EqualTo(0f),
            "no terrain near the unit -> no bonus.");
    }

    [Test]
    public void GroundedSpeed_AdvanceDeclaration_YieldsOnlyTheAdvanceBonus()
    {
        var harness = new Harness("Grounded Speed");
        IUnit unit = harness.BuildUnit("P1", "Grounded Speed");

        // The action gate keeps the Rush/Charge entries silent on an Advance-declared firing - the
        // sink nets per action, so cross-talk would surface as a Rush net on this evaluation.
        var sink = new MovementModifierSink();
        sink.ApplyFrom(harness.Evaluate(unit,
            new MoveActionDeclaredContext(unit, EActionType.Advance, 6f, Harness.OriginTerrain)));
        Assert.That(sink.Net(EActionType.Advance), Is.EqualTo(2f));
        Assert.That(sink.Net(EActionType.Rush), Is.EqualTo(0f));
        Assert.That(sink.Net(EActionType.Charge), Is.EqualTo(0f));
    }

    [Test]
    public void GroundedProtection_FoldsAFivePlusIgnore_OnlyWhenMostModelsAreInTerrain()
    {
        var harness = new Harness("Grounded Protection");
        IUnit defender = harness.BuildUnit("P1", "Grounded Protection");
        IUnit attacker = harness.BuildUnit("P2");

        WoundIgnoreSink near = harness.WoundIgnore(attacker, defender, Harness.OriginTerrain);
        Assert.That(near.HasIgnore, Is.True);
        Assert.That(near.Threshold, Is.EqualTo(5));
        Assert.That(harness.WoundIgnore(attacker, defender, null).HasIgnore, Is.False);
    }

    /// <summary>Live resolver + evaluator over the shipped AoF definitions, models at the origin so
    /// OriginTerrain covers them - the same harness shape as GroundedShippedDataTests.</summary>
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

        public IReadOnlyList<RuleOperation> Evaluate(IUnit unit, IHookContext context) =>
            _evaluator.EvaluateAll(context, RuleParticipant.Actor(unit));

        public float NetMove(IUnit unit, EActionType action, IReadOnlyList<ITerrain>? terrain)
        {
            var sink = new MovementModifierSink();
            sink.ApplyFrom(Evaluate(unit, new MoveActionDeclaredContext(unit, action, 6f, terrain)));
            return sink.Net(action);
        }

        public WoundIgnoreSink WoundIgnore(IUnit attacker, IUnit defender,
            IReadOnlyList<ITerrain>? terrain)
        {
            var sink = new WoundIgnoreSink();
            sink.ApplyFrom(_evaluator.EvaluateAll(
                new SaveRollCompleteContext(attacker, defender, Faces(1, 2, 3), TerrainPieces: terrain),
                RuleParticipant.Subject(defender)));
            return sink;
        }

        private static DiceResults Faces(params int[] faces)
        {
            var perSide = new float[6];
            foreach (int face in faces) perSide[face - 1] += 1f;
            return new DiceResults(perSide);
        }
    }
}
