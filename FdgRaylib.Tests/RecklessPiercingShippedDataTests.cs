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
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #376 S4 - Reckless Piercing (+ Aura) over the DATA as shipped. The gamble is one die with two
// outcomes: grantTokenOnRoll's failure arm (engine S4a, TargetBonusMarkerTests) lands the exposed
// token on a 1 while a 2+ lands the boon - never both, never two dice. These pin the authored
// shape (opt-in ability at activation start, round-end tokens, the two token-gated passive arms)
// and net each arm through the live evaluator: the boon is Actor-seat AP(+1) out, the exposed
// token is Subject-seat AP(+1) against the bearer - the Mobile Artillery self-token shape with
// the sign flipped, the corpus' first hostile Subject-seat save modifier.
[TestFixture]
public class RecklessPiercingShippedDataTests
{
    private static readonly TokenType Boon = new("RecklessPiercingBoon");
    private static readonly TokenType Exposed = new("RecklessPiercingExposed");

    private static IReadOnlyList<SpecialRuleDefinition> Supplement() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Books", "AofRuleSupplement.json")));

    private static SpecialRuleDefinition Definition(string name) =>
        Supplement().Single(r => r.Name == name);

    // ---- Structure ----------------------------------------------------------------------------------

    [Test]
    public void TheGamble_IsOneOptInDie_BoonOnTwoPlus_ExposedOnAOne()
    {
        ActivatedAbility ability = Definition("Reckless Piercing").Activated.Single();
        Assert.That(ability.TriggerHook, Is.EqualTo(EHookID.Activation_OnActivationStart),
            "a single ability at activation start is offered as an optional Yes/No.");
        Assert.That(ability.AvailableWhen, Is.InstanceOf<Condition.AllModelsHaveThisRule>());

        var roll = (Effect.GrantTokenOnRoll)ability.Effect;
        Assert.That(roll.MinRoll, Is.EqualTo(2));
        Assert.That(roll.TType, Is.EqualTo(Boon));
        Assert.That(roll.Clear, Is.InstanceOf<TokenClearTrigger.RoundEnd>(),
            "both arms last until the end of the round.");

        var backfire = (Effect.GrantToken)roll.OnFailure!;
        Assert.That(backfire.TType, Is.EqualTo(Exposed));
        Assert.That(backfire.Clear, Is.InstanceOf<TokenClearTrigger.RoundEnd>());
    }

    [Test]
    public void TheTwoArms_AreTokenGatedSaveModifiers_AtOppositeSeats()
    {
        IReadOnlyList<HookEntry> entries = Definition("Reckless Piercing").Passive;
        Assert.That(entries, Has.Count.EqualTo(2));
        Assert.That(entries.All(e => e.HookID == EHookID.Shooting_OnHitRollComplete), Is.True,
            "hook 73 serves shooting and melee alike - 'when attacking' covers both.");
        Assert.That(entries.All(e => ((Effect.RollModifier)e.Effect).Delta == -1), Is.True);

        Assert.That(entries.Count(e => e.Seat == ERuleSeat.Actor), Is.EqualTo(1), "the boon arm");
        Assert.That(entries.Count(e => e.Seat == ERuleSeat.Subject), Is.EqualTo(1), "the exposed arm");
    }

    [Test]
    public void TheAura_ConfersTheBase()
    {
        HookEntry entry = Definition("Reckless Piercing Aura").Passive.Single();
        Assert.That(((Effect.Aura)entry.Effect).RuleName, Is.EqualTo("Reckless Piercing"));
    }

    // ---- Net effect over shipped data ---------------------------------------------------------------

    [Test]
    public void BoonToken_NetsApPlusOne_OnTheBearersOwnAttacks()
    {
        var harness = new Harness();
        IUnit bearer = harness.BuildUnit("Reckless Piercing");
        IUnit enemy = harness.BuildUnit();
        bearer.Tokens.AddToken(new Token(Boon, 1, new TokenClearTrigger.RoundEnd()));

        Assert.That(harness.NetSave(attacker: bearer, defender: enemy), Is.EqualTo(-1),
            "the 2+ arm: the bearer's weapons get AP(+1) while the boon stands.");
        Assert.That(harness.NetSave(attacker: enemy, defender: bearer), Is.EqualTo(0),
            "the boon does nothing for enemies attacking the bearer.");
    }

    [Test]
    public void ExposedToken_NetsApPlusOne_ForWhoeverAttacksTheBearer()
    {
        var harness = new Harness();
        IUnit bearer = harness.BuildUnit("Reckless Piercing");
        IUnit enemy = harness.BuildUnit();
        bearer.Tokens.AddToken(new Token(Exposed, 1, new TokenClearTrigger.RoundEnd()));

        Assert.That(harness.NetSave(attacker: enemy, defender: bearer), Is.EqualTo(-1),
            "the 1 arm: enemy weapons get AP(+1) against the bearer while it stands.");
        Assert.That(harness.NetSave(attacker: bearer, defender: enemy), Is.EqualTo(0),
            "the backfire does not sharpen the bearer's own attacks.");
    }

    [Test]
    public void NoToken_NoEffect()
    {
        var harness = new Harness();
        IUnit bearer = harness.BuildUnit("Reckless Piercing");
        IUnit enemy = harness.BuildUnit();

        Assert.That(harness.NetSave(attacker: bearer, defender: enemy), Is.EqualTo(0));
        Assert.That(harness.NetSave(attacker: enemy, defender: bearer), Is.EqualTo(0));
    }

    /// <summary>Live resolver + evaluator; both participants evaluated at hook 73 the way
    /// RollToHitStage does, netting the shared save-modifier scalar.</summary>
    private sealed class Harness
    {
        private readonly GameDataStore _store = GameDataStore.GameDataStoreBuilder.GetDefault();
        private readonly RuleResolver _resolver = new();
        private readonly RuleEvaluator _evaluator;

        public Harness()
        {
            var byName = Supplement().ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);
            _resolver.Register(byName["Reckless Piercing"]);
            _evaluator = new RuleEvaluator(new ProbabilisticDiceRoller(), ruleResolver: _resolver);
        }

        public IUnit BuildUnit(params string[] ruleNames)
        {
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                initialPosition: new Position(), gameDataStore: _store);
            DataBinding<ModelData> binding = _store.GetDataBinding<ModelData>(_store.Create(model));
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Unit", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { binding });
            foreach (string name in ruleNames) unit.AttachRuleDefinition(_resolver.Resolve(name));
            return unit;
        }

        public int NetSave(IUnit attacker, IUnit defender)
        {
            var sink = new RollModifierSink();
            sink.ApplyFrom(_evaluator.EvaluateAll(
                new HitRollCompleteContext(attacker, defender, Faces(4), 6f),
                RuleParticipant.Actor(attacker),
                RuleParticipant.Subject(defender)));
            return sink.Net(ERollKind.Save);
        }

        private static DiceResults Faces(params int[] faces)
        {
            var perSide = new float[6];
            foreach (int face in faces) perSide[face - 1] += 1f;
            return new DiceResults(perSide);
        }
    }
}
