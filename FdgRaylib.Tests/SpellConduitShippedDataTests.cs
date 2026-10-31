using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FDG;
using FDG.ArmyBuilding;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.Utilities;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #197 P23 Spell Conduit (9 refs) - the app-side half, over the DATA as shipped. The engine's
// SpellConduitRuleIntegrationTests prove the relay mechanism; these prove the supplement authors the one
// capability entry the way that mechanism expects (range, bonus, the Shaken gate), which neither
// --validate-rules nor RuleFireLint checks - all three are plain values in the JSON.
[TestFixture]
public class SpellConduitShippedDataTests
{
    private const string RuleName = "Spell Conduit";

    private static IReadOnlyList<SpecialRuleDefinition> Supplement() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Books", "GdfRuleSupplement.json")));

    private static SpecialRuleDefinition Definition() =>
        Supplement().Single(r => r.Name == RuleName);

    private static HookEntry CapabilityEntry() => Definition().Passive
        .Single(e => e.HookID == EHookID.Lifecycle_OnCapabilityQuery);

    [Test]
    public void TheRule_IsOneCapabilityEntry_AndNothingElse()
    {
        SpecialRuleDefinition rule = Definition();

        Assert.That(rule.Passive.Select(e => e.HookID), Is.EqualTo(new[] { EHookID.Lifecycle_OnCapabilityQuery }),
            "a relay changes only where a cast is measured from - there is no per-round grant and no ability.");
        Assert.That(rule.Activated, Is.Empty);
    }

    [Test]
    public void TheRelay_ReachesTwelveInches_AndAddsOneToTheRoll()
    {
        var relay = (Effect.EnableSpellRelay)CapabilityEntry().Effect;

        Assert.That(relay.RangeInches, Is.EqualTo(12f), "'casters within 12 inches'.");
        Assert.That(relay.CastRollBonus, Is.EqualTo(1), "'+1 to casting rolls when doing so'.");
    }

    [Test]
    public void Relaying_IsShutOff_WhileTheConduitIsShaken()
    {
        Condition condition = CapabilityEntry().Condition;

        Assert.That(condition, Is.InstanceOf<Condition.Not>(),
            "'friendly casters may only use this rule if this unit isn't Shaken'.");
        Assert.That(((Condition.Not)condition).Inner, Is.InstanceOf<Condition.TokenPresent>());
        Assert.That(((Condition.TokenPresent)((Condition.Not)condition).Inner).TType,
            Is.EqualTo(TokenType.Shaken));
    }

    [Test]
    public void TheShippedRule_RelaysForANearbyFriendlyCaster()
    {
        // End to end over shipped data: the relay offers itself as a cast origin, carrying its +1.
        var harness = new Harness(RuleName);
        IUnit caster = harness.BuildCaster("Psy-Seer", new Position(10f, 10f));
        harness.BuildConduit("Synaptic Relay", new Position(10f, 18f));

        IReadOnlyList<SpellRelay.CastOrigin> origins = harness.OriginsFor(caster);

        Assert.That(origins.Any(o => !o.IsSelf), Is.True, "the conduit is within 12 inches.");
        Assert.That(origins.First(o => !o.IsSelf).RollBonus, Is.EqualTo(1));
    }

    [Test]
    public void EveryBookThatReferencesTheRule_EmbedsIt()
    {
        // --apply-rules is manual, so a book can name the rule and ship without a definition, which
        // resolves to nothing in play.
        string booksDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Books");
        List<string> missing = new List<string>();

        foreach (string path in Directory.GetFiles(booksDir, "*.fdgbook"))
        {
            string json = File.ReadAllText(path);
            if (!json.Contains($"\"name\": \"{RuleName}\"", StringComparison.Ordinal)) continue;
            if (!json.Contains("\"enableSpellRelay\"", StringComparison.Ordinal))
            {
                missing.Add(Path.GetFileName(path));
            }
        }

        Assert.That(missing, Is.Empty,
            "books referencing Spell Conduit without an embedded definition: " + string.Join(", ", missing));
    }

    /// <summary>Live resolver + evaluator carrying the real shipped definition, over a real store so the
    /// relay scan can see both units on the table.</summary>
    private sealed class Harness
    {
        private readonly GameDataStore _store = GameDataStore.GameDataStoreBuilder.GetDefault();
        private readonly RuleResolver _resolver = new();
        private readonly RuleEvaluator _evaluator;
        private readonly TableState _tableState;
        private readonly PlayerID _player = new PlayerID(Guid.NewGuid());

        public Harness(params string[] ruleNames)
        {
            var byName = Supplement().ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);
            foreach (string name in ruleNames) _resolver.Register(byName[name]);
            _evaluator = new RuleEvaluator(new ProbabilisticDiceRoller(), ruleResolver: _resolver);
            _tableState = new TableState(_store);
        }

        public IUnit BuildCaster(string name, Position position)
        {
            IUnit unit = Build(name, position);
            ((UnitData)unit).AttachRuleDefinition(new ResolvedRule("Caster", CoreRuleCatalog.Caster,
                new RuleArgument[] { new RuleArgument.Int(2) }));
            return unit;
        }

        public IUnit BuildConduit(string name, Position position)
        {
            IUnit unit = Build(name, position);
            ((UnitData)unit).AttachRuleDefinition(new ResolvedRule(RuleName, _resolver.Resolve(RuleName).Definition));
            return unit;
        }

        public IReadOnlyList<SpellRelay.CastOrigin> OriginsFor(IUnit caster) =>
            SpellRelay.OriginsFor(_tableState, _evaluator, caster);

        private IUnit Build(string name, Position position)
        {
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                initialPosition: position, gameDataStore: _store);
            var modelBindings = new List<DataBinding<ModelData>>
            {
                _store.GetDataBinding<ModelData>(_store.Create(model)),
            };

            var unit = new UnitData(_player, name, quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { binding }));
            return binding.GetValue();
        }
    }
}
