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

// #376 S2 - Vale Oath Boost (+ Aura) over the DATA as shipped. The engine's ClearTokenOnRollTests
// prove the threshold fold (base 4+ plus Boost 3+ = ONE roll at 3+); these prove the supplement
// authors the pair the way that fold expects: the Boost carries the FULL boosted band (3, the
// min-threshold convention - an increment here would silently win every fold), it gates on the unit
// actually having Vale Oath (a Boost granted to a unit without the base must not conjure a roll),
// and the Aura confers the Boost.
[TestFixture]
public class ValeOathShippedDataTests
{
    private static IReadOnlyList<SpecialRuleDefinition> Supplement() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Books", "AofRuleSupplement.json")));

    private static SpecialRuleDefinition Definition(string name) =>
        Supplement().Single(r => r.Name == name);

    // ---- Structure ----------------------------------------------------------------------------------

    [Test]
    public void ValeOathBoost_RollsAtThree_GatedOnShakenAndTheBaseRule()
    {
        HookEntry entry = Definition("Vale Oath Boost").Passive.Single();
        Assert.That(entry.HookID, Is.EqualTo(EHookID.Round_OnRoundStart));

        var and = (Condition.And)entry.Condition;
        Assert.That(((Condition.TokenPresent)and.Left).TType, Is.EqualTo(TokenType.Shaken));
        Assert.That(((Condition.UnitHasRule)and.Right).RuleName, Is.EqualTo("Vale Oath"));

        var effect = (Effect.ClearTokenOnRoll)entry.Effect;
        Assert.That(effect.TType, Is.EqualTo(TokenType.Shaken));
        Assert.That(effect.MinRoll, Is.EqualTo(3),
            "min-threshold folds take the boosted value as authored, never an increment.");
    }

    [Test]
    public void ValeOathBoostAura_ConfersTheBoost()
    {
        HookEntry entry = Definition("Vale Oath Boost Aura").Passive.Single();
        Assert.That(entry.HookID, Is.EqualTo(EHookID.Lifecycle_OnUnitCreated));
        Assert.That(((Effect.Aura)entry.Effect).RuleName, Is.EqualTo("Vale Oath Boost"));
    }

    // ---- The fold over shipped data -----------------------------------------------------------------

    [Test]
    public void ValeOathWithBoost_FoldsToOneRollAtThree()
    {
        var harness = new Harness("Vale Oath", "Vale Oath Boost");
        IUnit unit = harness.BuildShakenUnit("Vale Oath", "Vale Oath Boost");

        Assert.That(harness.FoldedEntries(unit), Is.EqualTo(new[] { (TokenType.Shaken, 3) }),
            "base + Boost is a single roll at the boosted band, never two chances.");
    }

    [Test]
    public void ValeOathAlone_RollsAtFour()
    {
        var harness = new Harness("Vale Oath", "Vale Oath Boost");
        IUnit unit = harness.BuildShakenUnit("Vale Oath");

        Assert.That(harness.FoldedEntries(unit), Is.EqualTo(new[] { (TokenType.Shaken, 4) }));
    }

    [Test]
    public void BoostWithoutTheBaseRule_ConjuresNoRoll()
    {
        var harness = new Harness("Vale Oath", "Vale Oath Boost");
        IUnit unit = harness.BuildShakenUnit("Vale Oath Boost");

        Assert.That(harness.FoldedEntries(unit), Is.Empty,
            "the Boost modifies Vale Oath's roll; without the base rule there is no roll to modify.");
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
