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

// #376 S2 - Vale Oath over the DATA as shipped: the base rule's Shaken-clear roll at 4+ through the real fold.
// #432 deleted Vale Oath Boost (+ Aura) with the rest of the 3.5 Boost family (OPR 3.6 dropped them; #433's
// Enhanced family replaces the mechanic), so only the base-rule case remains here. The engine's
// ClearTokenOnRollTests still pin the min-threshold fold itself.
[TestFixture]
public class ValeOathShippedDataTests
{
    private static IReadOnlyList<SpecialRuleDefinition> Supplement() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Books", "AofRuleSupplement.json")));

    [Test]
    public void ValeOathAlone_RollsAtFour()
    {
        var harness = new Harness("Vale Oath");
        IUnit unit = harness.BuildShakenUnit("Vale Oath");

        Assert.That(harness.FoldedEntries(unit), Is.EqualTo(new[] { (TokenType.Shaken, 4) }));
    }

    [Test]
    public void TheBoostIsGone()
    {
        Assert.That(Supplement().Select(r => r.Name), Has.None.StartsWith("Vale Oath Boost"),
            "#432: OPR 3.6 dropped Vale Oath Boost and its Aura.");
    }

    /// <summary>Live resolver + evaluator over the shipped AoF definitions.</summary>
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

        public IUnit BuildShakenUnit(params string[] ruleNames)
        {
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                initialPosition: new Position(), gameDataStore: _store);
            DataBinding<ModelData> binding = _store.GetDataBinding<ModelData>(_store.Create(model));
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Vale Unit", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { binding });
            foreach (string name in ruleNames) unit.AttachRuleDefinition(_resolver.Resolve(name));
            unit.Tokens.AddToken(new Token(TokenType.Shaken, 1, new TokenClearTrigger.ManualOnly()));
            return unit;
        }

        public IReadOnlyList<(TokenType, int)> FoldedEntries(IUnit unit)
        {
            var sink = new TokenClearRollSink();
            sink.ApplyFrom(_evaluator.EvaluateAll(new RoundStartContext(unit),
                RuleParticipant.Actor(unit, models: unit.Models)));
            return sink.Entries;
        }
    }
}
