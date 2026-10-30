using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FDG;
using FDG.ArmyBuilding;
using FDG.Data;
using FDG.GameModel;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #432 - the OPR 3.6 range-conditional army-rule family as SHIPPED in GdfRuleSupplement.json / AofRuleSupplement.json.
// The mechanics are pinned engine-side (RangeConditionalOffensive/Defensive/MoraleRuleIntegrationTests,
// RangeFamily36RuleIntegrationTests, RangeConditional36ConditionTests) against hand-built copies of these definitions
// (RangeConditional36Definitions); this fixture pins that the authored JSON is exactly those shapes - entry for entry,
// the 9"/6" pair in each rule's data - with the 3.6.0 book text verbatim, that the 3.5 Boost family is gone (and a list
// still naming one loads with the "not implemented" diagnostic), and drives a few shipped rules through the real
// evaluator and queries (the guard BoostRuleCompositionTests used to give the Boost family).
[TestFixture]
public class RangeConditional36ShippedDataTests
{
    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static List<SpecialRuleDefinition> Load(string fileName) =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(Path.Combine(BooksDirectory, fileName)));

    private static SpecialRuleDefinition Shipped(string file, string name) => Load(file).Single(r => r.Name == name);

    private const string Gdf = "GdfRuleSupplement.json";
    private const string Aof = "AofRuleSupplement.json";

    private static readonly Condition Attacking = new Condition.AttackingFromOver(9f, 6f);
    private static readonly Condition Attacked = new Condition.AttackedFromOver(9f, 6f);
    private static readonly Condition MostModels = new Condition.MostModelsHaveThisRule();

    private const string ExtraHitText =
        "When this model shoots/charges enemies over 9\"/6\" away, unmodified result of 6 to hit deal 1 extra hit " +
        "(only the original hit counts as a 6 for special rules).";
    private const string ApText = "When this model shoots/charges enemies over 9\"/6\" away, its weapons get AP(+1).";
    private const string ExtraWoundText =
        "When this model shoots/charges enemies over 9\"/6\" away, unmodified results of 1 to block hits from weapons " +
        "deal 1 extra wound.";
    private const string PickText =
        "When this unit is activated, pick one effect: until the end of the activation, when shooting/charging enemies " +
        "over 9\"/6\" away, all models with this rule in it either get AP(+1) or get +1 to hit rolls.";
    private const string MinusHitText =
        "If most models in this unit have this rule, enemy units shooting/charging them from over 9\"/6\" away get -1 " +
        "to hit rolls .";
    private const string PlusDefenseText =
        "If most models in this unit have this rule, they get +1 to Defense rolls when shot/charged from over 9\"/6\" " +
        "away.";
    private const string WardText =
        "When a unit where most models have this rule takes wounds, roll one die for each. If the wound was from a " +
        "spell, then it is ignored on a 2+. If the wound was not from a spell, and the unit was shot/charged from over " +
        "9\"/6\" away, then it is ignored on a 6+ instead.";
    private const string ShroudText =
        "When a unit where most models have this rule is targeted by an enemy unit for shooting/charging, that enemy " +
        "unit gets -4\"/-2\" range to a min. of 6\".";
    private const string MoraleText =
        "If most models in this unit have this rule, they get +1 to Morale Test rolls when shot/charged from over " +
        "9\"/6\" away.";
    private const string ReachText = "This model gets +4\"/+2\" range when shooting/charging.";
    private const string MoveText = "Moves +1\" when using Advance, and +2\" when using Rush/Charge.";

    private static HookEntry[] ExtraHit() => new[]
    {
        new HookEntry(EHookID.Shooting_OnHitRollComplete, new Condition.And(Attacking, new Condition.UnmodifiedRollEquals(6)),
            new Effect.AddExtraHit(6, 1), ELifetime.ThisAttack),
    };

    private static HookEntry[] Ap() => new[]
    {
        new HookEntry(EHookID.Shooting_OnHitRollComplete, Attacking, new Effect.RollModifier(ERollKind.Save, -1),
            ELifetime.ThisAttack),
    };

    private static HookEntry[] PlusHit() => new[]
    {
        new HookEntry(EHookID.Shooting_OnHitRollModifier, Attacking, new Effect.RollModifier(ERollKind.Hit, 1),
            ELifetime.ThisAttack),
    };

    private static HookEntry[] ExtraWound() => new[]
    {
        new HookEntry(EHookID.Shooting_OnSaveRollComplete, Attacking, new Effect.AddExtraWound(1, 1),
            ELifetime.ThisAttack),
    };

    private static HookEntry[] MinusHit() => new[]
    {
        new HookEntry(EHookID.Shooting_OnHitRollModifier, new Condition.And(Attacked, MostModels),
            new Effect.RollModifier(ERollKind.Hit, -1), ELifetime.ThisAttack, ERuleSeat.Subject),
    };

    private static HookEntry[] PlusDefense() => new[]
    {
        new HookEntry(EHookID.Shooting_OnHitRollComplete, new Condition.And(Attacked, MostModels),
            new Effect.RollModifier(ERollKind.Save, 1), ELifetime.ThisAttack, ERuleSeat.Subject),
    };

    private static HookEntry[] Ward() => new[]
    {
        new HookEntry(EHookID.Shooting_OnSaveRollComplete, new Condition.And(new Condition.IsSpell(), MostModels),
            new Effect.IgnoreWoundOnRoll(2), ELifetime.ThisAttack, ERuleSeat.Subject),
        new HookEntry(EHookID.Shooting_OnSaveRollComplete, new Condition.And(Attacked, MostModels),
            new Effect.IgnoreWoundOnRoll(6), ELifetime.ThisAttack, ERuleSeat.Subject),
    };

    private static HookEntry[] Shroud() => new[]
    {
        new HookEntry(EHookID.Shooting_OnRangeCheck, MostModels, new Effect.RangeModifier(-4, 6, -2),
            ELifetime.ThisActivation, ERuleSeat.Subject),
    };

    private static HookEntry[] Morale() => new[]
    {
        new HookEntry(EHookID.Morale_OnPreMoraleTest, new Condition.And(Attacked, MostModels),
            new Effect.RollModifier(ERollKind.Morale, 1), ELifetime.ThisActivation),
    };

    private static HookEntry[] Reach() => new[]
    {
        new HookEntry(EHookID.Shooting_OnRangeCheck, new Condition.Always(), new Effect.RangeModifier(4, 0, 2),
            ELifetime.ThisActivation),
    };

    private static HookEntry[] Move() => new[]
    {
        MoveBonus(EActionType.Advance, 1f), MoveBonus(EActionType.Rush, 2f), MoveBonus(EActionType.Charge, 2f),
    };

    private static HookEntry MoveBonus(EActionType action, float inches) =>
        new(EHookID.Movement_OnMoveActionDeclared, new Condition.ActionTypeIs(action),
            new Effect.MovementBonus(action, inches, 0f), ELifetime.ThisActivation);

    // file, name, the 3.6.0 book text, scope, the expected passive entries.
    private static IEnumerable<TestCaseData> PassiveRules()
    {
        foreach (string name in new[] { "Devout", "Ferocious", "Brutal" })
            yield return Case(Gdf, name, ExtraHitText, ERuleScope.Unit, ExtraHit());
        yield return Case(Aof, "Lucky", ExtraHitText, ERuleScope.Unit, ExtraHit());
        foreach (string name in new[] { "Havocbound", "Piercing Warrior" })
            yield return Case(Gdf, name, ApText, ERuleScope.Unit, Ap());
        foreach (string name in new[] { "Infected", "Warbound", "Violent" })
            yield return Case(Gdf, name, ExtraWoundText, ERuleScope.Weapon, ExtraWound());
        yield return Case(Aof, "Destroyer", ExtraWoundText, ERuleScope.Weapon, ExtraWound());
        foreach (string name in new[] { "Changebound", "Machine-Fog", "Screened" })
            yield return Case(Gdf, name, MinusHitText, ERuleScope.Unit, MinusHit());
        yield return Case(Aof, "Empyrean Spirit", MinusHitText, ERuleScope.Unit, MinusHit());
        foreach (string name in new[] { "Guarded", "Sturdy" })
            yield return Case(Gdf, name, PlusDefenseText, ERuleScope.Unit, PlusDefense());
        foreach (string name in new[] { "Knightborn", "Warding" })
            yield return Case(Gdf, name, WardText, ERuleScope.Unit, Ward());
        yield return Case(Aof, "Angelic Blessing", WardText, ERuleScope.Unit, Ward());
        foreach (string name in new[] { "Darkborn", "Shadowborn", "Shrouded" })
            yield return Case(Gdf, name, ShroudText, ERuleScope.Unit, Shroud());
        yield return Case(Aof, "Wild Veil", ShroudText, ERuleScope.Unit, Shroud());
        foreach (string name in new[] { "Hive Bond", "Hold the Line", "Valiant" })
            yield return Case(Gdf, name, MoraleText, ERuleScope.Unit, Morale());
        foreach (string name in new[] { "Highborn", "Extra Reach" })
            yield return Case(Gdf, name, ReachText, ERuleScope.Unit, Reach());
        yield return Case(Aof, "Royal Legion", ReachText, ERuleScope.Unit, Reach());
        foreach (string name in new[] { "Lustbound", "Scurry" })
            yield return Case(Gdf, name, MoveText, ERuleScope.Unit, Move());
    }

    private static TestCaseData Case(string file, string name, string text, ERuleScope scope, HookEntry[] entries) =>
        new TestCaseData(file, name, text, scope, entries).SetArgDisplayNames(name);

    [TestCaseSource(nameof(PassiveRules))]
    public void TheShippedDefinition_IsTheTestedShape_WithThe36Text(string file, string name, string text,
        ERuleScope scope, HookEntry[] entries)
    {
        SpecialRuleDefinition rule = Shipped(file, name);

        Assert.That(rule.Passive, Is.EqualTo(entries), "entry for entry - hook, condition, effect, lifetime, seat");
        Assert.That(rule.Activated, Is.Empty);
        Assert.That(rule.Scope, Is.EqualTo(scope));
        Assert.That(rule.EngineArgumentCount, Is.EqualTo(0), "the 9\"/6\" pair is the condition's data, not an argument");
        Assert.That(rule.Valence, Is.EqualTo(EValence.Positive));
        Assert.That(rule.Description, Is.EqualTo(text), "the 3.6.0 book text, verbatim");
    }

    [TestCase(Gdf, "Versatile Warrior")]
    [TestCase(Gdf, "Watchborn")]
    [TestCase(Aof, "Vinci Tech")]
    public void ThePickRules_OfferAPOrHit_GatedOnRange(string file, string name)
    {
        SpecialRuleDefinition pick = Shipped(file, name);

        Assert.That(pick.Description, Is.EqualTo(PickText));
        Assert.That(pick.Passive, Is.Empty, "the rule itself is only the activation pick");
        Assert.That(pick.Activated.Select(a => (a.TriggerHook, a.Cost, a.Label, a.Effect)), Is.EqualTo(new[]
        {
            (EHookID.Activation_OnActivationStart, (Cost)new Cost.OncePerActivation(), "AP(+1)",
                (Effect)new Effect.AddRule($"{name} (Piercing)", ELifetime.ThisActivation)),
            (EHookID.Activation_OnActivationStart, (Cost)new Cost.OncePerActivation(), "+1 to hit",
                (Effect)new Effect.AddRule($"{name} (Precision)", ELifetime.ThisActivation)),
        }));
        Assert.That(pick.Activated.Select(a => a.TargetSelector.TargetAffinity), Is.All.EqualTo(ETargetAffinity.Self));

        Assert.That(Shipped(file, $"{name} (Piercing)").Passive, Is.EqualTo(Ap()));
        Assert.That(Shipped(file, $"{name} (Precision)").Passive, Is.EqualTo(PlusHit()));
    }

    [Test]
    public void VersatileWarriorAuraAndBuff_GrantTheRule()
    {
        SpecialRuleDefinition aura = Shipped(Gdf, "Versatile Warrior Aura");
        Assert.That(aura.Passive.Single().Effect, Is.EqualTo(new Effect.Aura("Versatile Warrior")));
        Assert.That(aura.Description, Is.EqualTo("This model and its unit get Versatile Warrior."));

        SpecialRuleDefinition buff = Shipped(Gdf, "Versatile Warrior Buff");
        ActivatedAbility ability = buff.Activated.Single();
        Assert.That(ability.TriggerHook, Is.EqualTo(EHookID.Activation_OnBeforeAttackAction));
        Assert.That(ability.TargetSelector.TargetAffinity, Is.EqualTo(ETargetAffinity.Friend));
        Assert.That(ability.TargetSelector.RangeInches, Is.EqualTo(12f));
        Assert.That(ability.Effect, Is.EqualTo(new Effect.AddRule("Versatile Warrior", ELifetime.NextTrigger)));
        Assert.That(buff.Description, Is.EqualTo("Once per activation, before attacking, pick one friendly unit " +
            "within 12\", which gets Versatile Warrior once (next time the effect would apply)."));
    }

    // --- the Boost family is gone --------------------------------------------------------------------------------

    private static IEnumerable<string> EverySupplementSource()
    {
        yield return Path.Combine(BooksDirectory, Gdf);
        yield return Path.Combine(BooksDirectory, Aof);
        // #435: no per-book override ships since the 3.6 sweep (every entry was moot), so the folder may be absent.
        string overrides = Path.Combine(BooksDirectory, "AofBookOverrides");
        if (!Directory.Exists(overrides)) yield break;
        foreach (string file in Directory.EnumerateFiles(overrides, "*.json"))
            yield return file;
    }

    [Test]
    public void NoBoostDefinitionShips_ButTheFeatHelpers()
    {
        // The Feat helpers are not the 3.5 Boost mechanic: "Speed Feat" etc. grant them for one activation.
        string[] helpers = { "Speed Feat Boost", "Precision Feat Boost", "Piercing Feat Boost" };
        List<string> boosts = EverySupplementSource()
            .SelectMany(path => BookRuleSupplement.LoadDefinitions(File.ReadAllText(path)))
            .Select(r => r.Name)
            .Where(n => n.EndsWith(" Boost") || n.EndsWith(" Boost Aura") || n.EndsWith(" Boost Buff"))
            .ToList();

        Assert.That(boosts, Is.EquivalentTo(helpers), "OPR 3.6 dropped every X Boost / X Boost Aura / X Boost Buff");
    }

    [Test]
    public void TheOverrides_NoLongerShadowThe36Texts()
    {
        foreach (string path in EverySupplementSource().Where(p => p.Contains("AofBookOverrides")))
        {
            Assert.That(BookRuleSupplement.LoadDefinitions(File.ReadAllText(path)).Select(r => r.Name),
                Has.None.EqualTo("Melee Shrouding").And.None.EqualTo("Lustbound"),
                $"{Path.GetFileName(path)}: 3.6 AoF prints the common Melee Shrouding / Lustbound");
        }

        Assert.That(Load(Aof).Select(r => r.Name), Has.None.EqualTo("Shadowborn"),
            "Shadowborn lives in the GDF file - GDF books name it too and bake against that file alone");
    }

    [Test]
    public void AListStillNamingABoost_LoadsWithANotImplementedDiagnostic()
    {
        // A list compiled against today's data (catalog + both supplements, no embedded copies) that still names two
        // 3.5 Boost rules beside their base rule: each Boost degrades to the #168 "not implemented" drop, the base rule
        // attaches, and the army builds.
        var army = new ArmyListFile
        {
            Name = "2k - Battle Sisters (saved under 3.5)",
            Faction = "Blessed Sisters",
            Units =
            {
                new UnitFileEntry
                {
                    Name = "Sisters", ModelCount = 5, Quality = 4, Defense = 4,
                    SpecialRules =
                    {
                        new SpecialRuleEntry_Core("Devout"),
                        new SpecialRuleEntry_Core("Devout Boost"),
                        new SpecialRuleEntry_Core("Devout Boost Aura"),
                    },
                    Weapons = { new WeaponFileEntry { Name = "Rifle", Quantity = 5, Attacks = 1, RangeInches = 24 } },
                },
            },
        };

        var drops = new List<RuleDrop>();
        void Capture(RuleDrop drop) => drops.Add(drop);
        UnitData unit;
        RuleDiagnostics.OnRuleDropped += Capture;
        try
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            foreach (SpecialRuleDefinition definition in Load(Gdf).Concat(Load(Aof)))
                resolver.RegisterOrReplace(definition);
            ArmyListRuleResolution.RegisterEmbeddedDefinitions(resolver, army);
            GameBootstrap.CreateArmy(new PlayerID(Guid.NewGuid()), army, store, resolver);
            unit = store.GetAllValues<UnitData>().Single();
        }
        finally
        {
            RuleDiagnostics.OnRuleDropped -= Capture;
        }

        Assert.That(drops.Select(d => d.RuleName), Is.EquivalentTo(new[] { "Devout Boost", "Devout Boost Aura" }));
        Assert.That(drops.Select(d => d.Reason), Is.All.EqualTo(ERuleDropReason.Unimplemented));
        Assert.That(unit.RuleDefinitions.Select(r => r.Definition.Name), Has.Member("Devout"),
            "the 3.6 base rule still attaches");
    }

    // --- shipped rules through the real evaluator and queries --------------------------------------------------

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
    public void ShippedChangebound_ReachesTheHitRoll_OnlyFromOverRange()
    {
        // The hook matters: a Hit delta emitted after the dice are rolled would net zero at every distance.
        var world = new World();
        UnitData attacker = world.Unit();
        UnitData daemons = world.Unit(Shipped(Gdf, "Changebound"));

        int NetHit(HitRollModifierContext context)
        {
            var sink = new RollModifierSink();
            sink.ApplyFrom(world.Evaluator.EvaluateAll(context, RuleParticipant.Actor(attacker),
                RuleParticipant.Subject(daemons, models: HeroStatRules.LivingModels(daemons))));
            return sink.Net(ERollKind.Hit);
        }

        Assert.That(NetHit(new HitRollModifierContext(attacker, daemons, 10f)), Is.EqualTo(-1));
        Assert.That(NetHit(new HitRollModifierContext(attacker, daemons, 8f)), Is.EqualTo(0));
        Assert.That(NetHit(new HitRollModifierContext(attacker, daemons, 0.5f, IsMelee: true, IsCharging: true,
            ChargeOriginDistanceInches: 7f)), Is.EqualTo(-1));
        Assert.That(NetHit(new HitRollModifierContext(attacker, daemons, 0.5f, IsMelee: true, IsCharging: true,
            ChargeOriginDistanceInches: 5f)), Is.EqualTo(0));
    }

    [Test]
    public void ShippedHiveBond_AddsOneToTheMoraleTest_OnlyWhenShotFromOverRange()
    {
        var world = new World();
        UnitData swarm = world.Unit(Shipped(Gdf, "Hive Bond"));

        int NetMorale(PreMoraleTestContext context)
        {
            var sink = new RollModifierSink();
            sink.ApplyFrom(world.Evaluator.EvaluateAll(context, RuleParticipant.Actor(swarm)));
            return sink.Net(ERollKind.Morale);
        }

        Assert.That(NetMorale(new PreMoraleTestContext(swarm, ShotFromInches: 10f)), Is.EqualTo(1));
        Assert.That(NetMorale(new PreMoraleTestContext(swarm, ShotFromInches: 8f)), Is.EqualTo(0));
        Assert.That(NetMorale(new PreMoraleTestContext(swarm)), Is.EqualTo(0), "a test no attack triggered");
    }

    [Test]
    public void ShippedDarkbornAndHighborn_FoldIntoRangeAndChargeReach()
    {
        var world = new World();
        UnitData shooter = world.Unit();
        UnitData darkborn = world.Unit(Shipped(Gdf, "Darkborn"));
        UnitData elves = world.Unit(Shipped(Gdf, "Highborn"));
        var rifle = new Weapon("Rifle", 24f, 1, 0);

        Assert.That(RangeRuleQueries.EffectiveRange(shooter, rifle, darkborn, world.Evaluator), Is.EqualTo(20f));
        Assert.That(RangeRuleQueries.EffectiveRange(shooter, new Weapon("Pistol", 9f, 1, 0), darkborn,
            world.Evaluator), Is.EqualTo(6f));
        Assert.That(MovementRuleQueries.EffectiveChargeDistanceAgainst(shooter, darkborn, 12f, world.Evaluator),
            Is.EqualTo(10f));
        Assert.That(RangeRuleQueries.EffectiveRange(elves, rifle, shooter, world.Evaluator), Is.EqualTo(28f));
        Assert.That(RangeRuleQueries.OwnChargeRangeDelta(elves, world.Evaluator), Is.EqualTo(2f));
    }
}
