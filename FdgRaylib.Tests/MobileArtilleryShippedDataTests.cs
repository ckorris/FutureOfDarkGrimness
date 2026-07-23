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

// #197 misc - Mobile Artillery (2 refs). The offensive arm ("uses a Hold action and shoots at enemies over
// 9in away -> +1 to hit") is pure data on shipped primitives: Not(IsMelee) + Not(AfterMoving) [= Hold, i.e.
// did not move] + AttackedFromOverInches(9), Actor seat, +1 Hit.
//
// The defensive arm ("as long as this unit hasn't moved during the round, enemies shooting from over 9in
// get -2 to hit") is DEFERRED: it needs round-persistent "this unit moved this round" state readable at the
// defensive hit hook (during the ENEMY's activation), which no primitive exposes - HasMoved is per-activation
// and about the acting unit, and a token granted at the move hook is not applied (ExecuteMoveStage only
// consumes grants there). Recorded in the #197 ledger. These tests pin the arm that shipped.
[TestFixture]
public class MobileArtilleryShippedDataTests
{
    private const string RuleName = "Mobile Artillery";
    private const float Far = 24f;   // > 9in
    private const float Near = 3f;   // <= 9in

    private static IReadOnlyList<SpecialRuleDefinition> Supplement() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Books", "GdfRuleSupplement.json")));

    [Test]
    public void OffensiveArm_IsActorSeat_AtTheHitModifierHook_PlusOneHit()
    {
        HookEntry entry = Supplement().Single(r => r.Name == RuleName).Passive.Single();
        Assert.That(entry.HookID, Is.EqualTo(EHookID.Shooting_OnHitRollModifier));
        Assert.That(entry.Seat, Is.EqualTo(ERuleSeat.Actor));
        Assert.That(entry.Effect, Is.InstanceOf<Effect.RollModifier>());
        var mod = (Effect.RollModifier)entry.Effect;
        Assert.That(mod.RollKind, Is.EqualTo(ERollKind.Hit));
        Assert.That(mod.Delta, Is.EqualTo(1));
    }

    [Test]
    public void HoldAndShootBeyondNine_NetsPlusOneToHit()
    {
        var h = new Harness(RuleName);
        Assert.That(h.NetOwnHit(moved: false, isMelee: false, distance: Far), Is.EqualTo(1),
            "Hold (did not move) + shooting a target over 9in away.");
    }

    [Test]
    public void MovingFirst_LosesTheBonus()
    {
        var h = new Harness(RuleName);
        Assert.That(h.NetOwnHit(moved: true, isMelee: false, distance: Far), Is.EqualTo(0),
            "an Advance is not a Hold action.");
    }

    [Test]
    public void ShootingWithinNine_GetsNoBonus()
    {
        var h = new Harness(RuleName);
        Assert.That(h.NetOwnHit(moved: false, isMelee: false, distance: Near), Is.EqualTo(0));
    }

    [Test]
    public void Melee_GetsNoBonus_ItIsAShootingRule()
    {
        var h = new Harness(RuleName);
        Assert.That(h.NetOwnHitMelee(chargeOriginInches: Far), Is.EqualTo(0),
            "'and shoots' - a melee swing, even a long charge, is excluded.");
    }

    private sealed class Harness
    {
        private readonly GameDataStore _store = GameDataStore.GameDataStoreBuilder.GetDefault();
        private readonly RuleResolver _resolver = new();
        private readonly RuleEvaluator _evaluator;
        private readonly IUnit _attacker;
        private readonly IUnit _defender;

        public Harness(string ruleName)
        {
            var byName = Supplement().ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);
            _resolver.Register(byName[ruleName]);
            _evaluator = new RuleEvaluator(new ProbabilisticDiceRoller(), ruleResolver: _resolver);
            _attacker = Build("P1", ruleName);
            _defender = Build("P2");
        }

        private IUnit Build(string playerName, params string[] ruleNames)
        {
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                initialPosition: new Position(), gameDataStore: _store);
            DataBinding<ModelData> binding = _store.GetDataBinding<ModelData>(_store.Create(model));
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), playerName, quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { binding });
            foreach (string name in ruleNames) unit.AttachRuleDefinition(_resolver.Resolve(name));
            return unit;
        }

        public int NetOwnHit(bool moved, bool isMelee, float distance)
        {
            var sink = new RollModifierSink();
            sink.ApplyFrom(_evaluator.EvaluateAll(
                new HitRollModifierContext(_attacker, _defender, distance, AttackerMoved: moved,
                    IsMelee: isMelee),
                RuleParticipant.Actor(_attacker)));
            return sink.Net(ERollKind.Hit);
        }

        public int NetOwnHitMelee(float chargeOriginInches)
        {
            var sink = new RollModifierSink();
            sink.ApplyFrom(_evaluator.EvaluateAll(
                new HitRollModifierContext(_attacker, _defender, DistanceInches: 0.5f, AttackerMoved: true,
                    IsMelee: true, IsCharging: true, ChargeOriginDistanceInches: chargeOriginInches),
                RuleParticipant.Actor(_attacker)));
            return sink.Net(ERollKind.Hit);
        }
    }
}
