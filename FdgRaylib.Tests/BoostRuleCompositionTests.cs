using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FDG;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Data;
using FDG.Utilities;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #197 — the guard that #196 needed and didn't have.
//
// A "Boost" rule in the corpus is written as the BOOSTED rule ("gets extra hits on 5-6, instead of only
// on 6"), but the engine composes it with its base ADDITIVELY: HitInjectionSink adds, RollModifierSink
// adds, MovementModifierSink adds. So a Boost authored as the boosted value double-counts wherever it
// overlaps the base - Devout + Devout Boost gave TWO extra hits on a natural 6. A Boost must therefore be
// authored as the INCREMENT: only the face, magnitude, or range band its base does not already cover.
//
// Neither existing gate catches that. --validate-rules checks structure; RuleFireLint proves each entry
// CAN fire but, by its own documentation, does not check that the ops it emits are consumed at that hook,
// nor what several rules sum to. These tests drive the real supplement definitions through the real
// evaluator and the real sinks and assert the NET effect a player would see.
//
// They also catch the wrong-hook class: a rule whose op is never consumed (a Hit delta emitted at
// Shooting_OnHitRollComplete, after the dice are rolled) nets zero, so its base-only case fails here.
[TestFixture]
public class BoostRuleCompositionTests
{
    private static IReadOnlyList<SpecialRuleDefinition> Supplement() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Books", "GdfRuleSupplement.json")));

    /// <summary>Minimal stand-in for the engine's internal TestRuleHarness: a live resolver + evaluator
    /// carrying the real shipped supplement definitions, so what is asserted is the data as authored.</summary>
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
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                initialPosition: new Position(), gameDataStore: _store);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));

            var unit = new UnitData(new PlayerID(Guid.NewGuid()), playerName, quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            foreach (string name in ruleNames) unit.AttachRuleDefinition(_resolver.Resolve(name));
            return unit;
        }

        public IReadOnlyList<RuleOperation> Evaluate(IUnit unit, ERuleSeat seat, IHookContext context) =>
            seat == ERuleSeat.Actor
                ? _evaluator.EvaluateAll(context, RuleParticipant.Actor(unit))
                : _evaluator.EvaluateAll(context, RuleParticipant.Subject(unit));
    }

    /// <summary>A d6 histogram with exactly one die on each named face.</summary>
    private static DiceResults Faces(params int[] faces)
    {
        var perSide = new float[6];
        foreach (int face in faces) perSide[face - 1] += 1f;
        return new DiceResults(perSide);
    }

    private static Harness HarnessWith(params string[] ruleNames) => new(ruleNames);

    // Far enough to satisfy every ">9 inches" gate, whether it reads the live or the launch distance.
    private const float Far = 24f;
    private const float Near = 3f;

    private static float ExtraHitsOnANaturalSix(Harness harness, IUnit attacker, IUnit target,
        float distance)
    {
        var context = new HitRollCompleteContext(attacker, target, Faces(6), DistanceInches: distance);
        var sink = new HitInjectionSink();
        sink.ApplyFrom(harness.Evaluate(attacker, ERuleSeat.Actor, context));
        return sink.TotalExtraHits;
    }

    private static float ExtraHitsOnANaturalFive(Harness harness, IUnit attacker, IUnit target,
        float distance)
    {
        var context = new HitRollCompleteContext(attacker, target, Faces(5), DistanceInches: distance);
        var sink = new HitInjectionSink();
        sink.ApplyFrom(harness.Evaluate(attacker, ERuleSeat.Actor, context));
        return sink.TotalExtraHits;
    }

    // ---- Trigger-widening Boosts: the boosted trigger set is {5,6}, one extra hit each ---------------

    [TestCase("Devout", "Devout Boost", true)]
    [TestCase("Ferocious", "Ferocious Boost", true)]
    [TestCase("Primal", "Primal Boost", false)]
    [TestCase("Clan Warrior", "Clan Warrior Boost", false)]
    public void TriggerWideningBoost_AddsTheNewFaceOnly_AndNeverDoublesTheSharedFace(
        string baseRule, string boostRule, bool rangeGated)
    {
        Harness harness = HarnessWith(baseRule, boostRule);
        IUnit attacker = harness.BuildUnit("P1", baseRule, boostRule);
        IUnit target = harness.BuildUnit("P2");

        Assert.That(ExtraHitsOnANaturalSix(harness, attacker, target, Far), Is.EqualTo(1f),
            $"'{baseRule}' already scores on a 6; '{boostRule}' must not score again on the same die.");
        Assert.That(ExtraHitsOnANaturalFive(harness, attacker, target, Far), Is.EqualTo(1f),
            $"'{boostRule}' widens the trigger to include 5s.");

        if (rangeGated)
        {
            Assert.That(ExtraHitsOnANaturalFive(harness, attacker, target, Near), Is.EqualTo(0f),
                $"'{boostRule}' only widens the trigger beyond 9 inches.");
            Assert.That(ExtraHitsOnANaturalSix(harness, attacker, target, Near), Is.EqualTo(1f),
                $"...but the base '{baseRule}' still scores on a 6 up close.");
        }
    }

    [Test]
    public void TriggerWideningBoost_WithoutItsBase_DoesNothingAtAll()
    {
        // "If this model has Devout, ..." - the Boost gates on its base, so a unit that somehow received the
        // Boost alone gets neither the widened trigger nor the original one. Asserted so the composition
        // above can't be mistaken for the Boost "being" the boosted rule: it is purely the increment.
        Harness harness = HarnessWith("Devout", "Devout Boost");
        IUnit attacker = harness.BuildUnit("P1", "Devout Boost");
        IUnit target = harness.BuildUnit("P2");

        Assert.That(ExtraHitsOnANaturalSix(harness, attacker, target, Far), Is.EqualTo(0f));
        Assert.That(ExtraHitsOnANaturalFive(harness, attacker, target, Far), Is.EqualTo(0f));
    }

    // ---- Gate-removal Boosts: the magnitude is unchanged, only the range band widens ----------------

    private static int NetToHitAgainst(Harness harness, IUnit attacker, IUnit defender, float distance)
    {
        var context = new HitRollModifierContext(attacker, defender, distance);
        var sink = new RollModifierSink();
        sink.ApplyFrom(harness.Evaluate(defender, ERuleSeat.Subject, context));
        return sink.Net(ERollKind.Hit);
    }

    [TestCase("Changebound", "Changebound Boost")]
    [TestCase("Machine-Fog", "Machine-Fog Boost")]
    public void GateRemovalBoost_KeepsTheMagnitude_AndOnlyWidensTheBand(string baseRule, string boostRule)
    {
        Harness harness = HarnessWith(baseRule, boostRule);
        IUnit attacker = harness.BuildUnit("P1");
        IUnit defender = harness.BuildUnit("P2", baseRule, boostRule);

        Assert.That(NetToHitAgainst(harness, attacker, defender, Far), Is.EqualTo(-1),
            $"'{baseRule}' and '{boostRule}' must not stack to -2 beyond 9 inches.");
        Assert.That(NetToHitAgainst(harness, attacker, defender, Near), Is.EqualTo(-1),
            $"'{boostRule}' extends the -1 inside 9 inches; it does not add a second -1.");
    }

    [Test]
    public void GateRemovalBase_OnItsOwn_AppliesOnlyBeyondTheBand()
    {
        // Also pins the hook: a Hit delta emitted at Shooting_OnHitRollComplete is never read (the dice have
        // already been rolled), so this would read 0 at both distances if the rule sat on the wrong hook.
        Harness harness = HarnessWith("Changebound");
        IUnit attacker = harness.BuildUnit("P1");
        IUnit defender = harness.BuildUnit("P2", "Changebound");

        Assert.That(NetToHitAgainst(harness, attacker, defender, Far), Is.EqualTo(-1),
            "Changebound must actually reach the hit roll - it sits at the modifier hook, before the dice.");
        Assert.That(NetToHitAgainst(harness, attacker, defender, Near), Is.EqualTo(0));
    }

    [Test]
    public void GateRemovalBoost_ReadsTheChargeLaunchDistance_NotTheContactDistance()
    {
        Harness harness = HarnessWith("Changebound");
        IUnit attacker = harness.BuildUnit("P1");
        IUnit defender = harness.BuildUnit("P2", "Changebound");

        // A charge declared from 20 inches, resolving in base contact.
        var charged = new HitRollModifierContext(attacker, defender, DistanceInches: 0.5f,
            AttackerMoved: true, IsMelee: true, IsCharging: true, ChargeOriginDistanceInches: 20f);
        var sink = new RollModifierSink();
        sink.ApplyFrom(harness.Evaluate(defender, ERuleSeat.Subject, charged));

        Assert.That(sink.Net(ERollKind.Hit), Is.EqualTo(-1),
            "'shot or charged from over 9 inches away' must fire for a long charge, not just for shooting.");
    }

    // ---- Magnitude Boosts: the increment, not the boosted total -------------------------------------

    private static float NetMovementBonus(Harness harness, IUnit unit, EActionType action)
    {
        var context = new MoveActionDeclaredContext(unit, action, BaseDistanceInches: 6f);
        var sink = new MovementModifierSink();
        sink.ApplyFrom(harness.Evaluate(unit, ERuleSeat.Actor, context));
        return sink.Net(action);
    }

    [Test]
    public void LustboundBoost_ComposesToThePublishedTotal_NotTheSum()
    {
        Harness harness = HarnessWith("Lustbound", "Lustbound Boost");
        IUnit unit = harness.BuildUnit("P1", "Lustbound", "Lustbound Boost");

        // Lustbound is +1/+3; boosted is "+2 on Advance and +6 on Rush/Charge", not +3/+9.
        Assert.That(NetMovementBonus(harness, unit, EActionType.Advance), Is.EqualTo(2f));
        Assert.That(NetMovementBonus(harness, unit, EActionType.Rush), Is.EqualTo(6f));
        Assert.That(NetMovementBonus(harness, unit, EActionType.Charge), Is.EqualTo(6f));
    }

    [Test]
    public void LustboundBase_OnItsOwn_IsUnboosted()
    {
        Harness harness = HarnessWith("Lustbound", "Lustbound Boost");
        IUnit unit = harness.BuildUnit("P1", "Lustbound");

        Assert.That(NetMovementBonus(harness, unit, EActionType.Advance), Is.EqualTo(1f));
        Assert.That(NetMovementBonus(harness, unit, EActionType.Rush), Is.EqualTo(3f));
    }
}
