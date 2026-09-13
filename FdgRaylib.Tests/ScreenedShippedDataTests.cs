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

// #197 misc slice - Screened (1 ref, the Aura form in Wormhole Daemons of Plague). The rule is the exact
// shape of the already-shipped Machine-Fog / Changebound ("shot or charged from over 9 inches away -> -1
// to hit"), so this is pure data on the DONE AttackedFromOverInches gate. These tests prove the data is
// authored the way the mechanism expects, which --validate-rules and RuleFireLint cannot: the first
// checks structure, the second proves an entry CAN fire, neither checks what a player nets nor that a
// Hit delta lands on the modifier hook (a delta emitted at Shooting_OnHitRollComplete, after the dice are
// rolled, nets zero).
[TestFixture]
public class ScreenedShippedDataTests
{
    private const string RuleName = "Screened";
    private const string AuraName = "Screened Aura";

    // Far enough to satisfy the ">9 inches" gate; near enough to fail it.
    private const float Far = 24f;
    private const float Near = 3f;

    private static IReadOnlyList<SpecialRuleDefinition> Supplement() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Books", "GdfRuleSupplement.json")));

    private static SpecialRuleDefinition Definition(string name) =>
        Supplement().Single(r => r.Name == name);

    [Test]
    public void Screened_NetsMinusOneToEnemyHitRolls_OnlyBeyondNineInches()
    {
        var harness = new Harness(RuleName);
        IUnit defender = harness.BuildUnit("P1", RuleName);
        IUnit attacker = harness.BuildUnit("P2");

        Assert.That(harness.NetHit(attacker, defender, Far), Is.EqualTo(-1),
            "Screened must reach the hit roll - it sits at the modifier hook, before the dice.");
        Assert.That(harness.NetHit(attacker, defender, Near), Is.EqualTo(0),
            "'when shot or charged from over 9 inches away'.");
    }

    [Test]
    public void Screened_FiresForALongCharge_NotJustForShooting()
    {
        var harness = new Harness(RuleName);
        IUnit defender = harness.BuildUnit("P1", RuleName);
        IUnit attacker = harness.BuildUnit("P2");

        // A charge declared from 20 inches, resolving in base contact - the arm that a live-distance gate
        // could never pass (melee resolves at ~2 inches). Reads the charge launch distance.
        var charged = new HitRollModifierContext(attacker, defender, DistanceInches: 0.5f,
            AttackerMoved: true, IsMelee: true, IsCharging: true, ChargeOriginDistanceInches: 20f);
        var sink = new RollModifierSink();
        sink.ApplyFrom(harness.Evaluate(defender, ERuleSeat.Subject, charged));

        Assert.That(sink.Net(ERollKind.Hit), Is.EqualTo(-1),
            "'shot OR charged from over 9 inches away' must fire for a long charge.");
    }

    [Test]
    public void TheAura_ConfersTheRuleItself()
    {
        HookEntry entry = Definition(AuraName).Passive.Single();

        Assert.That(entry.HookID, Is.EqualTo(EHookID.Lifecycle_OnUnitCreated));
        Assert.That(entry.Effect, Is.InstanceOf<Effect.Aura>());
        Assert.That(((Effect.Aura)entry.Effect).RuleName, Is.EqualTo(RuleName),
            "the single corpus reference is the Aura form, so a broken link makes the rule unreachable.");
    }

    [Test]
    public void TheEffect_ReachesTheUnit_WhenConferredThroughTheAura()
    {
        // End to end over shipped data: the aura confers Screened, and the conferred rule reads back into a
        // live -1 to enemy hit rolls beyond 9 inches.
        var harness = new Harness(AuraName, RuleName);
        IUnit defender = harness.BuildUnit("P1", AuraName);
        IUnit attacker = harness.BuildUnit("P2");

        harness.Apply(harness.Evaluate(defender, ERuleSeat.Actor, new UnitCreatedContext(defender)));

        Assert.That(harness.NetHit(attacker, defender, Far), Is.EqualTo(-1),
            "aura -> conferred rule -> a modifier the hit stage actually reads.");
        Assert.That(harness.NetHit(attacker, defender, Near), Is.EqualTo(0));
    }

    /// <summary>Minimal stand-in for the engine's internal TestRuleHarness (the VersatileDefense /
    /// BoostRuleComposition precedent): a live resolver + evaluator carrying the real shipped definitions,
    /// so what is asserted is the data as authored.</summary>
    private sealed class Harness
    {
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
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 2; i++)
            {
                var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                    initialPosition: new Position(), gameDataStore: _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(Guid.NewGuid()), playerName, quality: 4, defense: 4,
                modelBindings: modelBindings);
            foreach (string name in ruleNames) unit.AttachRuleDefinition(_resolver.Resolve(name));
            return unit;
        }

        public IReadOnlyList<RuleOperation> Evaluate(IUnit unit, ERuleSeat seat, IHookContext context) =>
            seat == ERuleSeat.Actor
                ? _evaluator.EvaluateAll(context, RuleParticipant.Actor(unit))
                : _evaluator.EvaluateAll(context, RuleParticipant.Subject(unit));

        public void Apply(IReadOnlyList<RuleOperation> operations) =>
            OperationApplier.ApplyTokenOperations(operations);

        public int NetHit(IUnit attacker, IUnit defender, float distanceInches)
        {
            var sink = new RollModifierSink();
            sink.ApplyFrom(Evaluate(defender, ERuleSeat.Subject,
                new HitRollModifierContext(attacker, defender, distanceInches)));
            return sink.Net(ERollKind.Hit);
        }
    }
}
