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
using FDG.Rules.Tokens;
using FDG.Utilities;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #197 P23 Spell Accumulator (7 refs) - the app-side half, over the DATA as shipped rather than a
// hand-built stand-in. The engine's SpellAccumulatorRuleIntegrationTests prove the mechanism; these prove
// the supplement authors it the way that mechanism expects, which neither --validate-rules (structure) nor
// RuleFireLint (an entry CAN fire) can check: the token TYPE, the cap, the range, and the Shaken gate are
// all plain values in the JSON, and every one of them is silently wrong-able.
//
// The rule's two halves are authored independently - a round-start grant that fills a pool and a capability
// entry that opens it - so the failure this guards against is the two naming different token types, which
// would leave a rule that funds a pool nobody can reach.
[TestFixture]
public class SpellAccumulatorShippedDataTests
{
    private const string RuleName = "Spell Accumulator";

    private static IReadOnlyList<SpecialRuleDefinition> Supplement() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Books", "GdfRuleSupplement.json")));

    private static SpecialRuleDefinition Definition() =>
        Supplement().Single(r => r.Name == RuleName);

    private static Effect.GrantToken Grant() => (Effect.GrantToken)Definition().Passive
        .Single(e => e.HookID == EHookID.Round_OnRoundStart).Effect;

    private static HookEntry CapabilityEntry() => Definition().Passive
        .Single(e => e.HookID == EHookID.Lifecycle_OnCapabilityQuery);

    [Test]
    public void TheGrant_FillsItsOwnPool_SizedByTheRulesArgument()
    {
        Effect.GrantToken grant = Grant();

        Assert.That(grant.TType, Is.EqualTo(TokenType.AccumulatorTokens),
            "not SpellTokens: the corpus puts this upgrade on caster units, and 'casters from OTHER " +
            "friendly units' means the holder must not be able to spend its own pool.");
        Assert.That(grant.Count, Is.InstanceOf<ValueSource.Arg>(),
            "'gets X accumulator tokens' - X is the rule's argument, as in Spell Accumulator(1).");
        Assert.That(Definition().EngineArgumentCount, Is.EqualTo(1),
            "so the book's coreNumeric reference resolves its numericValue into Arg(0).");
    }

    [Test]
    public void ThePool_CarriesOver_AndIsCappedAtSix()
    {
        Effect.GrantToken grant = Grant();

        Assert.That(grant.Clear, Is.InstanceOf<TokenClearTrigger.ManualOnly>(),
            "the pool accumulates - a RoundEnd clear would empty it every round and make the cap dead.");
        Assert.That(grant.MaxTotal, Is.EqualTo(6),
            "'can't hold more than 6 tokens at once'. The cap is the RULE's, stated on its own grant, " +
            "not the engine's MAX_SPELL_TOKENS clamp - the two are free to differ.");
    }

    [Test]
    public void TheLendingEntry_OpensThatSamePool_TwelveInchesOut()
    {
        var lending = (Effect.EnableSpellLending)CapabilityEntry().Effect;

        Assert.That(lending.Pool, Is.EqualTo(Grant().TType),
            "the two halves are authored separately, so this is the join: fund one pool and lend another " +
            "and the rule does nothing at all.");
        Assert.That(lending.RangeInches, Is.EqualTo(12f), "'within 12 inches'.");
    }

    [Test]
    public void LendingIsShutOff_WhileTheUnitIsShaken()
    {
        Condition condition = CapabilityEntry().Condition;

        Assert.That(condition, Is.InstanceOf<Condition.Not>(),
            "'friendly casters may only use this rule if this unit isn't Shaken'.");
        Assert.That(((Condition.Not)condition).Inner, Is.InstanceOf<Condition.TokenPresent>());
        Assert.That(((Condition.TokenPresent)((Condition.Not)condition).Inner).TType,
            Is.EqualTo(TokenType.Shaken));
    }

    [Test]
    public void TheShippedRule_LendsToANearbyFriendlyCaster_AndStopsWhenShaken()
    {
        // End to end over shipped data: fund the pool through the real round-start entry, then ask the
        // purse - which is what CastSpellStage and ChooseActionStage ask - what the caster may spend.
        var harness = new Harness(RuleName);
        IUnit caster = harness.BuildCaster("Psy-Seer", new Position(10f, 10f), spellTokens: 1);
        IUnit accumulator = harness.BuildUnit("Change Boon", new Position(16f, 10f),
            (RuleName, new RuleArgument.Int(3)));

        harness.FireRoundStart(accumulator);
        Assert.That(accumulator.Tokens.GetTokenCount(TokenType.AccumulatorTokens), Is.EqualTo(3));

        Assert.That(harness.Available(caster), Is.EqualTo(4),
            "1 of its own plus the 3 on offer 5 inches away.");

        accumulator.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Shaken));
        Assert.That(harness.Available(caster), Is.EqualTo(1),
            "and the Shaken clause shuts the pool without touching the tokens in it.");
    }

    [Test]
    public void EveryBookThatReferencesTheRule_EmbedsIt()
    {
        // --apply-rules is a manual step, so a book can reference the rule by name and ship without a
        // definition for it - which resolves to nothing in play and warns only in the load log.
        string booksDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Books");
        List<string> missing = new List<string>();

        foreach (string path in Directory.GetFiles(booksDir, "*.fdgbook"))
        {
            string json = File.ReadAllText(path);
            if (!json.Contains($"\"name\": \"{RuleName}\"", StringComparison.Ordinal)) continue;
            if (!json.Contains("\"enableSpellLending\"", StringComparison.Ordinal))
            {
                missing.Add(Path.GetFileName(path));
            }
        }

        Assert.That(missing, Is.Empty,
            "books referencing Spell Accumulator without an embedded definition: " +
            string.Join(", ", missing));
    }

    /// <summary>Live resolver + evaluator carrying the real shipped definitions, over a real store so the
    /// purse can see both units on the table.</summary>
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

        public IUnit BuildUnit(string name, Position position,
            params (string rule, RuleArgument arg)[] rules)
        {
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                initialPosition: position, gameDataStore: _store);
            var modelBindings = new List<DataBinding<ModelData>>
            {
                _store.GetDataBinding<ModelData>(_store.Create(model)),
            };

            var unit = new UnitData(_player, name, quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            foreach ((string rule, RuleArgument arg) in rules)
            {
                binding.GetValue().AttachRuleDefinition(new ResolvedRule(rule, _resolver.Resolve(rule).Definition,
                    new[] { arg }));
            }

            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { binding }));
            return binding.GetValue();
        }

        public IUnit BuildCaster(string name, Position position, int spellTokens)
        {
            IUnit unit = BuildUnit(name, position);
            ((UnitData)unit).AttachRuleDefinition(new ResolvedRule("Caster", CoreRuleCatalog.Caster,
                new RuleArgument[] { new RuleArgument.Int(2) }));
            if (spellTokens > 0)
            {
                unit.Tokens.AddToken(
                    new Token(TokenType.SpellTokens, spellTokens, new TokenClearTrigger.ManualOnly()));
            }

            return unit;
        }

        public void FireRoundStart(IUnit unit) => OperationApplier.ApplyTokenOperations(
            _evaluator.Evaluate(unit, ERuleSeat.Actor,
                new FDG.Rules.Dispatch.Contexts.RoundStartContext(unit), weapon: null, models: unit.Models));

        public int Available(IUnit caster) => FDG.Stages.SpellPurse.Available(_tableState, _evaluator, caster);
    }
}
