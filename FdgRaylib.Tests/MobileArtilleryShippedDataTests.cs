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
using FDG.Rules.Tokens;
using FDG.Utilities;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #197 misc - Mobile Artillery (2 refs). The offensive arm ("uses a Hold action and shoots at enemies over
// 9in away -> +1 to hit") is pure data on shipped primitives: Not(IsMelee) + Not(AfterMoving) [= Hold, i.e.
// did not move] + AttackedFromOverInches(9), Actor seat, +1 Hit.
//
// The defensive arm ("as long as this unit hasn't moved during the round, enemies shooting from over 9in
// get -2 to hit") shipped 2026-07-30 on the new TokenType.MovedThisRound: Stealth's exact shape, but gated
// Not(TokenPresent(MovedThisRound)) instead of on a per-attack condition. It had to be a token because the
// hook fires during the ENEMY's activation - HasMoved is per-activation and about the ACTING unit, and
// Condition.AfterMoving reads the attacker, not the bearer. MovementStage stamps the token when a declared
// move resolves; the round-end sweep clears it.
//
// Note the two arms read "moved" at different scopes ON PURPOSE, because the rule text does: the offensive
// arm is "uses a Hold ACTION" (this activation, AfterMoving) and the defensive one is "hasn't moved during
// the ROUND" (the token). A unit that moved earlier in the round and Holds now gets +1 but not -2.
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
        HookEntry entry = Supplement().Single(r => r.Name == RuleName).Passive
            .Single(e => e.Seat == ERuleSeat.Actor);
        Assert.That(entry.HookID, Is.EqualTo(EHookID.Shooting_OnHitRollModifier));
        Assert.That(entry.Effect, Is.InstanceOf<Effect.RollModifier>());
        var mod = (Effect.RollModifier)entry.Effect;
        Assert.That(mod.RollKind, Is.EqualTo(ERollKind.Hit));
        Assert.That(mod.Delta, Is.EqualTo(1));
    }

    [Test]
    public void DefensiveArm_IsSubjectSeat_AtTheSameHook_MinusTwoHit()
    {
        HookEntry entry = Supplement().Single(r => r.Name == RuleName).Passive
            .Single(e => e.Seat == ERuleSeat.Subject);
        Assert.That(entry.HookID, Is.EqualTo(EHookID.Shooting_OnHitRollModifier));
        Assert.That(entry.Effect, Is.InstanceOf<Effect.RollModifier>());
        var mod = (Effect.RollModifier)entry.Effect;
        Assert.That(mod.RollKind, Is.EqualTo(ERollKind.Hit));
        Assert.That(mod.Delta, Is.EqualTo(-2), "'they get -2 to hit rolls'");
        Assert.That(entry.Condition.ToString(), Does.Contain(TokenType.MOVED_THIS_ROUND_ID),
            "the round-scoped gate must be the token, not a per-attack AfterMoving read");
    }

    // The headline: a bearer that has not moved this round is -2 to be shot at from beyond 9in. Evaluated
    // with the participant shape DetermineHitRollStage really uses (attacker Actor + defender Subject).
    [Test]
    public void HasNotMovedThisRound_ShotFromBeyondNine_IsMinusTwoToHit()
    {
        var h = new Harness(RuleName, ruleOnDefender: true);
        Assert.That(h.NetIncomingHit(distance: Far), Is.EqualTo(-2));
    }

    [Test]
    public void HavingMovedThisRound_TheDefensiveBonusIsOff()
    {
        var h = new Harness(RuleName, ruleOnDefender: true);
        h.MarkDefenderMoved();
        Assert.That(h.NetIncomingHit(distance: Far), Is.EqualTo(0),
            "'as long as this unit hasn't moved during the round'");
    }

    [Test]
    public void ShotFromWithinNine_GetsNoDefensiveBonus()
    {
        var h = new Harness(RuleName, ruleOnDefender: true);
        Assert.That(h.NetIncomingHit(distance: Near), Is.EqualTo(0),
            "'from over 9 inches away' - a close-range shot is unaffected.");
    }

    // Mirrors Stealth, which carries no Not(IsMelee) either: a melee swing resolves in base contact, so the
    // live-distance gate can never pass there. Pinned so nobody "fixes" the missing melee exclusion.
    [Test]
    public void MeleeSwing_GetsNoDefensiveBonus_TheDistanceGateExcludesIt()
    {
        var h = new Harness(RuleName, ruleOnDefender: true);
        Assert.That(h.NetIncomingHit(distance: 0.5f, isMelee: true), Is.EqualTo(0));
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

    // #308: the "Moved" chip is hidden on every unit EXCEPT one carrying a rule that reads the token.
    // Mobile Artillery is that rule, so this is the end-to-end check that the hiding didn't take the one
    // unit that needs the chip down with it - against shipped book data, not a hand-authored stand-in.
    [Test]
    public void ShippedMobileArtillery_IsSeenAsAReaderOfTheMovedToken()
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
            initialPosition: new Position(), gameDataStore: store);
        var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Artillery", quality: 4, defense: 4,
            modelBindings: new List<DataBinding<ModelData>>
            {
                store.GetDataBinding<ModelData>(store.Create(model)),
            });
        unit.AttachRuleDefinition(new ResolvedRule(RuleName,
            Supplement().Single(r => r.Name == RuleName), Array.Empty<RuleArgument>()));

        Assert.That(TokenReadership.IsReadByAnyRule(unit, TokenType.MovedThisRound), Is.True,
            "the artillery piece must keep its Moved chip - it explains why its -2 switched off.");
        Assert.That(TokenDisplay.ResolveProminence(
                TokenDefinitionCatalog.Create(TokenType.MovedThisRound), unit),
            Is.EqualTo(ETokenProminence.Normal));
    }

    private sealed class Harness
    {
        private readonly GameDataStore _store = GameDataStore.GameDataStoreBuilder.GetDefault();
        private readonly RuleResolver _resolver = new();
        private readonly RuleEvaluator _evaluator;
        private readonly IUnit _attacker;
        private readonly IUnit _defender;

        public Harness(string ruleName, bool ruleOnDefender = false)
        {
            var byName = Supplement().ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);
            _resolver.Register(byName[ruleName]);
            _evaluator = new RuleEvaluator(new ProbabilisticDiceRoller(), ruleResolver: _resolver);
            _attacker = ruleOnDefender ? Build("P1") : Build("P1", ruleName);
            _defender = ruleOnDefender ? Build("P2", ruleName) : Build("P2");
        }

        /// <summary> What MovementStage stamps when a declared move resolves. </summary>
        public void MarkDefenderMoved() =>
            _defender.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.MovedThisRound));

        // The defensive read, with DetermineHitRollStage's real participant shape: the attacker in the
        // Actor seat and the defender's living models in the Subject seat (which is what lets the bearer's
        // AllModelsHaveThisRule gate see them).
        public int NetIncomingHit(float distance, bool isMelee = false)
        {
            var sink = new RollModifierSink();
            sink.ApplyFrom(_evaluator.EvaluateAll(
                new HitRollModifierContext(_attacker, _defender, distance, AttackerMoved: false,
                    IsMelee: isMelee),
                RuleParticipant.Actor(_attacker),
                RuleParticipant.Subject(_defender, models: HeroStatRules.LivingModels(_defender))));
            return sink.Net(ERollKind.Hit);
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
