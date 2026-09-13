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

// #197 P22 - the shipped Ambushing Piercing Shot data (4 refs: Jackals, Robot Legions, Rebel
// Guerrillas). Pure data on two shipped seams: DeferDeployment (the "counts as having Ambush" half) and
// a Save delta at the hit-complete hook gated on the ArrivedFromReserve token - which the arrival pass
// stamps and the round-end sweep clears, so "on the round in which it deploys via this rule" rides the
// token's existing lifecycle. (The token is also stamped by an Aircraft off-table return; no corpus APS
// unit is an Aircraft, checked 2026-07-28, so nothing observable rides on the shared marker.)
[TestFixture]
public class AmbushingPiercingShotShippedDataTests
{
    private const string RuleName = "Ambushing Piercing Shot";

    private static IReadOnlyList<SpecialRuleDefinition> Supplement() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Books", "GdfRuleSupplement.json")));

    private static SpecialRuleDefinition Definition() => Supplement().Single(r => r.Name == RuleName);

    [Test]
    public void ItCountsAsHavingAmbush_TheCoreRoundTwoKind()
    {
        HookEntry defer = Definition().Passive
            .Single(e => e.HookID == EHookID.Deployment_OnPreDeploymentSelect);

        var effect = (Effect.DeferDeployment)defer.Effect;
        Assert.That(effect.Timing, Is.EqualTo(EDeferTiming.LaterRound));
        Assert.That(effect.PlacementRangeInches, Is.EqualTo(9f).Within(0.001f));
        Assert.That(effect.MinArrivalRound, Is.EqualTo(2),
            "plain Ambush timing - only Rapid Ambush names round 1");
    }

    [Test]
    public void ArrivedThisRound_ItsShooting_GetsApPlusOne()
    {
        var harness = new Harness();
        IUnit shooter = harness.BuildUnit("P1", RuleName);
        IUnit target = harness.BuildUnit("P2");
        shooter.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.ArrivedFromReserve));

        Assert.That(harness.NetSave(shooter, target, isMelee: false), Is.EqualTo(-1),
            "'its weapons get AP(+1) when shooting on the round in which it deploys'");
        Assert.That(harness.NetSave(shooter, target, isMelee: true), Is.EqualTo(0),
            "'when shooting' - melee swings gain nothing");
    }

    [Test]
    public void AnyOtherRound_NoToken_NoBonus()
    {
        var harness = new Harness();
        IUnit shooter = harness.BuildUnit("P1", RuleName);
        IUnit target = harness.BuildUnit("P2");

        Assert.That(harness.NetSave(shooter, target, isMelee: false), Is.EqualTo(0),
            "once the round-end sweep takes the ArrivedFromReserve marker, the AP is gone");
    }

    [Test]
    public void EveryBookReferencingIt_EmbedsTheDefinition()
    {
        string booksDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Books");
        List<string> missing = new List<string>();

        foreach (string path in Directory.GetFiles(booksDir, "*.fdgbook"))
        {
            string json = File.ReadAllText(path);
            if (!json.Contains($"\"name\": \"{RuleName}\"", StringComparison.Ordinal)) continue;
            // The defer half embeds under this name only if the whole definition did.
            if (!json.Contains($"\"description\": \"Counts as having Ambush, and its weapons",
                    StringComparison.Ordinal))
            {
                missing.Add(Path.GetFileName(path));
            }
        }

        Assert.That(missing, Is.Empty,
            "books referencing Ambushing Piercing Shot without its embedded definition: "
            + string.Join(", ", missing));
    }

    /// <summary>Live resolver + evaluator carrying the real shipped definition (Screened's harness shape).</summary>
    private sealed class Harness
    {
        private readonly GameDataStore _store = GameDataStore.GameDataStoreBuilder.GetDefault();
        private readonly RuleResolver _resolver = new();
        private readonly RuleEvaluator _evaluator;

        public Harness()
        {
            _resolver.Register(Definition());
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

        public int NetSave(IUnit attacker, IUnit target, bool isMelee)
        {
            var sink = new RollModifierSink();
            sink.ApplyFrom(_evaluator.EvaluateAll(
                new HitRollCompleteContext(attacker, target, Faces(4), DistanceInches: 12f,
                    IsMelee: isMelee),
                RuleParticipant.Actor(attacker)));
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
