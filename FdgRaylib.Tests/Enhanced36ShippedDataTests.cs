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
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #433 - the OPR 3.6 "Enhanced X" family as SHIPPED in GdfRuleSupplement.json / AofRuleSupplement.json. The mechanics are
// pinned engine-side (EnhancedRuleIntegrationTests, Enhanced36ConditionTests) against hand-built copies of these
// definitions (Enhanced36Definitions); this fixture pins that the authored JSON is exactly those shapes - entry for
// entry, every entry gated on the bearer having X (Option B: the additive delta, the base never edited) - with the
// 3.6.0 book text verbatim, that every gate names a rule that is actually defined, that the whole family is present,
// and drives a few shipped pairs through the real evaluator (the no-stack guard the Boost family used to have).
[TestFixture]
public class Enhanced36ShippedDataTests
{
    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");
    private const string Gdf = "GdfRuleSupplement.json";
    private const string Aof = "AofRuleSupplement.json";

    private static List<SpecialRuleDefinition> Load(string fileName) =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(Path.Combine(BooksDirectory, fileName)));

    private static SpecialRuleDefinition Shipped(string file, string name) => Load(file).Single(r => r.Name == name);

    // The universe an AoF book bakes against: GDF first, AoF later-wins; a GDF book bakes against the GDF file alone.
    private static List<SpecialRuleDefinition> Merged() => FdgRaylib.Import.RuleSupplementSet.LoadMerged(new[]
    {
        Path.Combine(BooksDirectory, Gdf), Path.Combine(BooksDirectory, Aof),
    });

    // --- the shapes (Enhanced36Definitions, in the data's own terms) --------------------------------------------------

    private static Condition Has(string x) => new Condition.UnitHasRule(x);
    private static readonly Condition MostModels = new Condition.MostModelsHaveThisRule();
    private static readonly Condition Attacked = new Condition.AttackedFromOver(9f, 6f);
    private static readonly Condition Attacking = new Condition.AttackingFromOver(9f, 6f);
    private static readonly Condition ShotOrCharged = new Condition.AttackedFromOver(0f, 0f);
    private static Condition Not(Condition c) => new Condition.Not(c);
    private static Condition And(Condition l, Condition r) => new Condition.And(l, r);

    private static HookEntry[] Move(string x) => new[]
    {
        MoveBonus(x, EActionType.Advance, 1f), MoveBonus(x, EActionType.Rush, 2f), MoveBonus(x, EActionType.Charge, 2f),
    };

    private static HookEntry MoveBonus(string x, EActionType action, float inches) =>
        new(EHookID.Movement_OnMoveActionDeclared, And(new Condition.ActionTypeIs(action), Has(x)),
            new Effect.MovementBonus(action, inches, 0f), ELifetime.ThisActivation);

    private static HookEntry[] Shrink(string x) => new[]
    {
        new HookEntry(EHookID.Shooting_OnRangeCheck, And(Has(x), MostModels), new Effect.RangeModifier(-4, 6, -2),
            ELifetime.ThisActivation, ERuleSeat.Subject),
    };

    private static HookEntry[] Reach(string x) => new[]
    {
        new HookEntry(EHookID.Shooting_OnRangeCheck, Has(x), new Effect.RangeModifier(4, 0, 2), ELifetime.ThisActivation),
    };

    // --- the 3.6.0 texts: OPR templates them per family, so one template per shape ---------------------------------------

    private const string MoveText =
        "If this model has {X}, it moves +2\" on Advance and +4\" on Rush/Charge from {X} (instead of only +1\" and +2\").";
    private const string ShrinkText =
        "If most models in this unit have {X}, enemy units shooting/charging it get -8\"/-4\" range to a min. of 6\" from " +
        "{X} (instead of only -4\"/-2\").";
    private const string ReachText =
        "If this model has {X}, it gets +8\"/+4\" range when shooting/charging from {X} (instead of only +4\"/+2\").";

    private static string Text(string template, string x) => template.Replace("{X}", x);

    // file, Enhanced name, base X, the 3.6.0 text, the expected passive entries.
    private static IEnumerable<TestCaseData> PassiveRules()
    {
        // B1: sums.
        foreach (string x in new[] { "Lustbound", "Scurry", "Agile" })
            yield return Case(Gdf, x, Text(MoveText, x), Move(x));
        foreach (string x in new[] { "Darkborn", "Shadowborn", "Shrouded" })
            yield return Case(Gdf, x, Text(ShrinkText, x), Shrink(x));
        yield return Case(Aof, "Wild Veil", Text(ShrinkText, "Wild Veil"), Shrink("Wild Veil"));
        foreach (string x in new[] { "Highborn", "Extra Reach" })
            yield return Case(Gdf, x, Text(ReachText, x), Reach(x));
        yield return Case(Aof, "Royal Legion", Text(ReachText, "Royal Legion"), Reach("Royal Legion"));
    }

    private static TestCaseData Case(string file, string x, string text, HookEntry[] entries) =>
        new TestCaseData(file, $"Enhanced {x}", x, text, entries).SetArgDisplayNames($"Enhanced {x}");

    [TestCaseSource(nameof(PassiveRules))]
    public void TheShippedDefinition_IsTheTestedShape_WithThe36Text(string file, string name, string x, string text,
        HookEntry[] entries)
    {
        SpecialRuleDefinition rule = Shipped(file, name);

        Assert.That(rule.Passive, Is.EqualTo(entries), "entry for entry - hook, condition, effect, lifetime, seat");
        Assert.That(rule.Activated, Is.Empty);
        Assert.That(rule.Scope, Is.EqualTo(ERuleScope.Unit));
        Assert.That(rule.EngineArgumentCount, Is.EqualTo(0));
        Assert.That(rule.Valence, Is.EqualTo(EValence.Positive));
        Assert.That(rule.Description, Is.EqualTo(text), "the 3.6.0 book text, verbatim");
        Assert.That(rule.Passive.Select(e => e.Condition).All(c => Gates(c, x)), Is.True,
            $"every entry is inert without {x}: the gate is a conjunct of each condition");
    }

    // A UnitHasRule(x) in a conjunctive position (the And chain), the shape of every Enhanced entry.
    private static bool Gates(Condition condition, string x) => condition switch
    {
        Condition.UnitHasRule has => string.Equals(has.RuleName, x, StringComparison.OrdinalIgnoreCase),
        Condition.And and => Gates(and.Left, x) || Gates(and.Right, x),
        _ => false,
    };

    [Test]
    public void EveryGate_NamesARuleTheBookUniverseDefines()
    {
        // The base X of every Enhanced X must exist for the Enhanced to ever fire: in the catalog or the supplement the
        // same book reads (a GDF file's gate must resolve from the GDF file + catalog alone).
        var gdfUniverse = new HashSet<string>(CoreRuleCatalog.All.Select(r => r.Name).Concat(Load(Gdf).Select(r => r.Name)),
            StringComparer.OrdinalIgnoreCase);
        var aofUniverse = new HashSet<string>(gdfUniverse.Concat(Load(Aof).Select(r => r.Name)),
            StringComparer.OrdinalIgnoreCase);

        foreach ((string file, HashSet<string> universe) in new[] { (Gdf, gdfUniverse), (Aof, aofUniverse) })
        {
            foreach (SpecialRuleDefinition rule in Load(file).Where(r => r.Name.StartsWith("Enhanced ")))
            {
                foreach (string gate in rule.Passive.Select(e => e.Condition).SelectMany(GateNames))
                {
                    Assert.That(universe, Does.Contain(gate), $"{file}: {rule.Name} gates on '{gate}'");
                }
            }
        }
    }

    private static IEnumerable<string> GateNames(Condition condition) => condition switch
    {
        Condition.UnitHasRule has => new[] { has.RuleName },
        Condition.And and => GateNames(and.Left).Concat(GateNames(and.Right)),
        Condition.Or or => GateNames(or.Left).Concat(GateNames(or.Right)),
        Condition.Not not => GateNames(not.Inner),
        _ => Array.Empty<string>(),
    };

    [Test]
    public void EveryEnhancedDefinition_IsTheOnlyCopyOfItsName_AndTheBaseIsUntouched()
    {
        // Option B never edits X: the base rules the #432 / #434 / #435 fixtures pin are still those shapes (their own
        // fixtures assert them); here only that no Enhanced name is defined twice across the two files.
        List<string> names = Load(Gdf).Concat(Load(Aof)).Select(r => r.Name).Where(n => n.StartsWith("Enhanced ")).ToList();
        Assert.That(names, Is.Unique, "a shared Enhanced name lives in the GDF file once; the AoF file reads it too");
    }

    // --- shipped pairs through the real evaluator and queries: the no-stack guard --------------------------------------

    private sealed class World
    {
        public readonly GameDataStore Store = GameDataStore.GameDataStoreBuilder.GetDefault();
        public readonly RuleEvaluator Evaluator = new(new ProbabilisticDiceRoller());

        public UnitData Unit(params SpecialRuleDefinition[] rules)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), new Position(0, 0), Store);
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Unit", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { Store.GetDataBinding<ModelData>(Store.Create(model)) });
            foreach (SpecialRuleDefinition rule in rules) unit.AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
            return unit;
        }
    }

    [Test]
    public void ShippedDarkbornAndEnhancedDarkborn_ShrinkByEight_Once()
    {
        var world = new World();
        UnitData shooter = world.Unit();
        UnitData darkborn = world.Unit(Shipped(Gdf, "Darkborn"), Shipped(Gdf, "Enhanced Darkborn"));
        UnitData pretender = world.Unit(Shipped(Gdf, "Enhanced Darkborn"));

        Assert.That(RangeRuleQueries.EffectiveRange(shooter, new Weapon("Rifle", 24f, 1, 0), darkborn, world.Evaluator),
            Is.EqualTo(16f), "24 - 4 - 4: the delta once, never the Enhanced total added to the base");
        Assert.That(RangeRuleQueries.EffectiveRange(shooter, new Weapon("Carbine", 12f, 1, 0), darkborn, world.Evaluator),
            Is.EqualTo(6f), "the 6\" floor");
        Assert.That(MovementRuleQueries.EffectiveChargeDistanceAgainst(shooter, darkborn, 12f, world.Evaluator),
            Is.EqualTo(8f));
        Assert.That(RangeRuleQueries.EffectiveRange(shooter, new Weapon("Rifle", 24f, 1, 0), pretender, world.Evaluator),
            Is.EqualTo(24f), "Enhanced Darkborn without Darkborn is inert");
    }

    [Test]
    public void ShippedHighbornAndEnhancedHighborn_ReachEightAndFour_Once()
    {
        var world = new World();
        UnitData target = world.Unit();
        UnitData elves = world.Unit(Shipped(Gdf, "Highborn"), Shipped(Gdf, "Enhanced Highborn"));

        Assert.That(RangeRuleQueries.EffectiveRange(elves, new Weapon("Bow", 24f, 1, 0), target, world.Evaluator),
            Is.EqualTo(32f));
        Assert.That(RangeRuleQueries.OwnChargeRangeDelta(elves, world.Evaluator), Is.EqualTo(4f));
    }
}
