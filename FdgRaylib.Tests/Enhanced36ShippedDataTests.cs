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

    private static HookEntry[] ApMinusTwo(string x) => new[]
    {
        new HookEntry(EHookID.Shooting_OnHitRollComplete, And(Has(x), MostModels), new Effect.ReduceArmorPenetration(1),
            ELifetime.ThisAttack, ERuleSeat.Subject),
    };

    private static HookEntry[] IgnoreFivePlus(string x) => new[]
    {
        new HookEntry(EHookID.Shooting_OnSaveRollComplete, And(Has(x), MostModels), new Effect.IgnoreWoundOnRoll(5),
            ELifetime.ThisAttack, ERuleSeat.Subject),
    };

    private static HookEntry[] RecoverThreePlus(string x) => new[]
    {
        new HookEntry(EHookID.Round_OnRoundStart, And(Has(x), And(new Condition.TokenPresent(TokenType.Shaken), MostModels)),
            new Effect.ClearTokenOnRoll(TokenType.Shaken, 3), ELifetime.ThisRound),
    };

    private static HookEntry[] RerollFivePlus(string x) => new[]
    {
        new HookEntry(EHookID.Shooting_OnSaveRollComplete, Has(x),
            new Effect.Reroll(ERollKind.Save, new RerollCondition.OnUnmodifiedValue(5)), ELifetime.ThisAttack),
    };

    private static HookEntry[] PostCombatSix(string x) => new[]
    {
        new HookEntry(EHookID.Shooting_OnPostShoot, And(Has(x), MostModels), new Effect.TriggeredMove(6f, true),
            ELifetime.ThisAttack),
        new HookEntry(EHookID.Melee_OnPostMelee, And(Has(x), MostModels), new Effect.TriggeredMove(6f, true),
            ELifetime.ThisAttack),
    };

    private static readonly Condition NotSpell = new Condition.IsNotSpell();

    private static HookEntry[] MinusHit(string x) => new[]
    {
        new HookEntry(EHookID.Shooting_OnHitRollModifier, And(Has(x), And(Not(Attacked), MostModels)),
            new Effect.RollModifier(ERollKind.Hit, -1), ELifetime.ThisAttack, ERuleSeat.Subject),
    };

    private static HookEntry[] PlusDefense(string x) => new[]
    {
        new HookEntry(EHookID.Shooting_OnHitRollComplete, And(Has(x), And(Not(Attacked), MostModels)),
            new Effect.RollModifier(ERollKind.Save, 1), ELifetime.ThisAttack, ERuleSeat.Subject),
    };

    private static HookEntry[] Ward(string x) => new[]
    {
        new HookEntry(EHookID.Shooting_OnSaveRollComplete, And(Has(x), And(And(ShotOrCharged, Not(Attacked)), MostModels)),
            new Effect.IgnoreWoundOnRoll(6), ELifetime.ThisAttack, ERuleSeat.Subject),
    };

    private static HookEntry[] Morale(string x) => new[]
    {
        new HookEntry(EHookID.Morale_OnPreMoraleTest, And(Has(x), And(Not(Attacked), MostModels)),
            new Effect.RollModifier(ERollKind.Morale, 1), ELifetime.ThisActivation),
    };

    private static HookEntry[] ExtraHit(string x) => new[]
    {
        new HookEntry(EHookID.Shooting_OnHitRollComplete,
            And(Has(x), And(And(Not(Attacking), NotSpell), new Condition.UnmodifiedRollEquals(6))),
            new Effect.AddExtraHit(6, 1), ELifetime.ThisAttack),
    };

    private static HookEntry[] ApPlusOne(string x) => new[]
    {
        new HookEntry(EHookID.Shooting_OnHitRollComplete, And(Has(x), And(Not(Attacking), NotSpell)),
            new Effect.RollModifier(ERollKind.Save, -1), ELifetime.ThisAttack),
    };

    private static HookEntry[] ExtraWound(string x) => new[]
    {
        new HookEntry(EHookID.Shooting_OnSaveRollComplete, And(Has(x), And(Not(Attacking), NotSpell)),
            new Effect.AddExtraWound(1, 1), ELifetime.ThisAttack),
    };

    private static HookEntry[] Pick(string x) => new[]
    {
        new HookEntry(EHookID.Shooting_OnHitRollComplete, And(Has($"{x} (Piercing)"), And(Not(Attacking), NotSpell)),
            new Effect.RollModifier(ERollKind.Save, -1), ELifetime.ThisAttack),
        new HookEntry(EHookID.Shooting_OnHitRollModifier, And(Has($"{x} (Precision)"), Not(Attacking)),
            new Effect.RollModifier(ERollKind.Hit, 1), ELifetime.ThisAttack),
    };

    private static HookEntry[] PlusHit(string x) => new[]
    {
        new HookEntry(EHookID.Shooting_OnHitRollModifier,
            And(Has(x), Not(And(Not(new Condition.IsMelee()), new Condition.DistanceGreaterThan(9f)))),
            new Effect.RollModifier(ERollKind.Hit, 1), ELifetime.ThisAttack),
    };

    private static HookEntry[] HitsOnFive(string x) => new[]
    {
        new HookEntry(EHookID.Shooting_OnHitRollComplete, And(Has(x), new Condition.UnmodifiedRollHits(5)),
            new Effect.AddExtraHit(5, 1), ELifetime.ThisAttack),
    };

    private static HookEntry[] TwoD3(string x) => new[]
    {
        new HookEntry(EHookID.Activation_OnActivationStart, And(Has(x), MostModels),
            new Effect.RepositionAtActivation(new DiceExpression.D3(), 0, Move: true), ELifetime.ThisActivation),
    };

    // --- the 3.6.0 texts: OPR templates them per family, so one template per shape ---------------------------------------

    private const string MoveText =
        "If this model has {X}, it moves +2\" on Advance and +4\" on Rush/Charge from {X} (instead of only +1\" and +2\").";
    private const string ShrinkText =
        "If most models in this unit have {X}, enemy units shooting/charging it get -8\"/-4\" range to a min. of 6\" from " +
        "{X} (instead of only -4\"/-2\").";
    private const string ReachText =
        "If this model has {X}, it gets +8\"/+4\" range when shooting/charging from {X} (instead of only +4\"/+2\").";
    private const string ApMinusTwoText =
        "If most models in this unit have {X}, hits they take count as having AP(-2) from {X} (instead of only AP(-1)).";
    private const string IgnoreFivePlusText =
        "If most models in this unit have {X}, they ignore wounds on rolls of 5-6 from {X} (instead of only on 6+).";
    private const string RecoverThreePlusText =
        "If most models in this unit have {X}, they stops being Shaken on rolls of 3+ from {X} (instead of only on 4+).";
    private const string RerollFivePlusText =
        "If this model has {X}, enemies taking hits from it must re-roll successful unmodified Defense results of 5-6 " +
        "from {X} (instead of only results of 6).";
    private const string PostCombatSixText =
        "If most models in this unit have {X}, they may move by up to 6\" from {X} (instead of only 3\").";
    private const string TwoD3Text = "If this model has {X}, it may move by up to 2D3\" from {X} (instead of only D3\").";
    private const string MinusHitText =
        "If most models in this unit have {X}, enemies attacking them always get -1 to hit rolls from {X} (instead of " +
        "only when shooting/charging from over 9\"/6\" away).";
    private const string PlusDefenseText =
        "If most models in this unit have {X}, they always get +1 to Defense rolls from {X} (instead of only when " +
        "shot/charged from over 9\"/6\" away).";
    private const string WardText =
        "If most models in this unit have {X}, they always ignore shooting/charging wounds on rolls of 6+ from {X} " +
        "(instead of only when shot/charged from over 9\"/6\" away).";
    private const string MoraleText =
        "If most models in this unit have {X}, they always get +1 to Morale Test rolls from {X} (instead of only when " +
        "shot/charged from over 9\"/6\" away).";
    private const string ExtraHitText =
        "If this model has {X}, the target always takes hits on unmodified hit results of 6 from {X} (instead of only " +
        "when shooting/charging enemies over 9\"/6\" away).";
    private const string ApPlusOneText =
        "If this model has {X}, it always gets AP(+1) from {X} (instead of only when shooting/charging enemies over " +
        "9\"/6\" away).";
    private const string ExtraWoundText =
        "If this model has {X}, enemies always take wounds on unmodified results of 1 to block from {X} (instead of " +
        "only when shooting/charging enemies over 9\"/6\" away).";
    // OPR's own text names the mechanic ("Versatile Attack") for all three pick rules; kept verbatim (#435 note).
    private const string PickText =
        "If this model has Versatile Attack, it always gets either AP(+1) or +1 to hit rolls from Versatile Attack " +
        "(instead of only when shooting/charging enemies over 9\"/6\" away).";
    private const string PlusHitText =
        "If this model has {X}, it always gets +1 to hit rolls from {X} (instead of only when shooting over 9\" away).";
    private const string HitsOnFiveText =
        "If this model has {X}, it gets extra attacks on successful unmodified hit results of 5-6 from {X} (instead of " +
        "only on 6).";

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
        // B2: keep-best folds.
        foreach (string x in new[] { "Guardian", "Fortified" })
            yield return Case(Gdf, x, Text(ApMinusTwoText, x), ApMinusTwo(x));
        foreach (string x in new[] { "Ossified", "Warden" })
            yield return Case(Aof, x, Text(ApMinusTwoText, x), ApMinusTwo(x));
        foreach (string x in new[] { "Plaguebound", "Self-Repair", "Protected" })
            yield return Case(Gdf, x, Text(IgnoreFivePlusText, x), IgnoreFivePlus(x));
        yield return Case(Aof, "Cursed Undead", Text(IgnoreFivePlusText, "Cursed Undead"), IgnoreFivePlus("Cursed Undead"));
        yield return Case(Gdf, "Steadfast", Text(RecoverThreePlusText, "Steadfast"), RecoverThreePlus("Steadfast"));
        yield return Case(Aof, "Vale Oath", Text(RecoverThreePlusText, "Vale Oath"), RecoverThreePlus("Vale Oath"));
        foreach (string x in new[] { "Mischievous", "Scrapper", "Vicious" })
            yield return Case(Gdf, x, Text(RerollFivePlusText, x), RerollFivePlus(x));
        yield return Case(Aof, "Bestial", Text(RerollFivePlusText, "Bestial"), RerollFivePlus("Bestial"));
        foreach (string x in new[] { "Harassing", "Guerrilla", "Hit & Run" })
            yield return Case(Gdf, x, Text(PostCombatSixText, x), PostCombatSix(x));
        // B3: the activation move.
        foreach (string x in new[] { "Wolfborn", "Nimble", "Rapid Blink" })
            yield return Case(Gdf, x, Text(TwoD3Text, x), TwoD3(x));
        yield return Case(Aof, "Wave-Step", Text(TwoD3Text, "Wave-Step"), TwoD3("Wave-Step"));
        // C: the "always" complements.
        foreach (string x in new[] { "Changebound", "Machine-Fog", "Screened" })
            yield return Case(Gdf, x, Text(MinusHitText, x), MinusHit(x));
        yield return Case(Aof, "Empyrean Spirit", Text(MinusHitText, "Empyrean Spirit"), MinusHit("Empyrean Spirit"));
        foreach (string x in new[] { "Guarded", "Sturdy" })
            yield return Case(Gdf, x, Text(PlusDefenseText, x), PlusDefense(x));
        foreach (string x in new[] { "Knightborn", "Warding" })
            yield return Case(Gdf, x, Text(WardText, x), Ward(x));
        yield return Case(Aof, "Angelic Blessing", Text(WardText, "Angelic Blessing"), Ward("Angelic Blessing"));
        foreach (string x in new[] { "Hive Bond", "Hold the Line", "Valiant" })
            yield return Case(Gdf, x, Text(MoraleText, x), Morale(x));
        foreach (string x in new[] { "Devout", "Ferocious", "Brutal" })
            yield return Case(Gdf, x, Text(ExtraHitText, x), ExtraHit(x));
        yield return Case(Aof, "Lucky", Text(ExtraHitText, "Lucky"), ExtraHit("Lucky"));
        foreach (string x in new[] { "Havocbound", "Piercing Warrior" })
            yield return Case(Gdf, x, Text(ApPlusOneText, x), ApPlusOne(x));
        foreach (string x in new[] { "Infected", "Warbound", "Violent" })
            yield return Case(Gdf, x, Text(ExtraWoundText, x), ExtraWound(x));
        yield return Case(Aof, "Destroyer", Text(ExtraWoundText, "Destroyer"), ExtraWound("Destroyer"));
        foreach (string x in new[] { "Versatile Warrior", "Watchborn" })
            yield return new TestCaseData(Gdf, $"Enhanced {x}", $"{x} (", PickText, Pick(x)).SetArgDisplayNames($"Enhanced {x}");
        yield return new TestCaseData(Aof, "Enhanced Vinci Tech", "Vinci Tech (", PickText, Pick("Vinci Tech"))
            .SetArgDisplayNames("Enhanced Vinci Tech");
        foreach (string x in new[] { "Targeting Visor", "Precision Marksman" })
            yield return Case(Gdf, x, Text(PlusHitText, x), PlusHit(x));
        yield return Case(Aof, "Buccaneer", Text(PlusHitText, "Buccaneer"), PlusHit("Buccaneer"));
        // D: extra attacks on successful 5-6.
        foreach (string x in new[] { "Bloodborn", "Clan Warrior", "Primal", "Predator" })
            yield return Case(Gdf, x, Text(HitsOnFiveText, x), HitsOnFive(x));
        yield return Case(Aof, "Royal Warrior", Text(HitsOnFiveText, "Royal Warrior"), HitsOnFive("Royal Warrior"));
    }

    private static TestCaseData Case(string file, string x, string text, HookEntry[] entries) =>
        new TestCaseData(file, $"Enhanced {x}", x, text, entries).SetArgDisplayNames($"Enhanced {x}");

    // --- E: the wrappers ---------------------------------------------------------------------------------------------

    private static readonly string[] GdfAuras =
    {
        "Bloodborn", "Brutal", "Changebound", "Clan Warrior", "Darkborn", "Devout", "Ferocious", "Guardian", "Guerrilla",
        "Harassing", "Havocbound", "Highborn", "Hive Bond", "Hold the Line", "Infected", "Knightborn", "Lustbound",
        "Machine-Fog", "Mischievous", "Plaguebound", "Rapid Blink", "Scrapper", "Scurry", "Shadowborn", "Sturdy",
        "Targeting Visor", "Warbound", "Watchborn", "Wolfborn",
    };

    private static readonly string[] AofAuras =
    {
        "Bestial", "Buccaneer", "Destroyer", "Empyrean Spirit", "Lucky", "Ossified", "Royal Legion", "Royal Warrior",
        "Vale Oath", "Vinci Tech", "Warden", "Wave-Step", "Wild Veil",
    };

    private static readonly string[] GdfBuffs = { "Hold the Line", "Primal", "Self-Repair", "Shadowborn" };
    private static readonly string[] AofBuffs = { "Angelic Blessing", "Cursed Undead" };

    private static IEnumerable<TestCaseData> Auras()
    {
        foreach (string x in GdfAuras) yield return new TestCaseData(Gdf, x).SetArgDisplayNames($"Enhanced {x} Aura");
        foreach (string x in AofAuras) yield return new TestCaseData(Aof, x).SetArgDisplayNames($"Enhanced {x} Aura");
    }

    private static IEnumerable<TestCaseData> Buffs()
    {
        foreach (string x in GdfBuffs) yield return new TestCaseData(Gdf, x).SetArgDisplayNames($"Enhanced {x} Buff");
        foreach (string x in AofBuffs) yield return new TestCaseData(Aof, x).SetArgDisplayNames($"Enhanced {x} Buff");
    }

    [TestCaseSource(nameof(Auras))]
    public void TheShippedAura_GrantsTheEnhancedRule_WithThe36Text(string file, string x)
    {
        SpecialRuleDefinition aura = Shipped(file, $"Enhanced {x} Aura");

        Assert.That(aura.Passive, Is.EqualTo(new[]
        {
            new HookEntry(EHookID.Lifecycle_OnUnitCreated, new Condition.Always(), new Effect.Aura($"Enhanced {x}"),
                ELifetime.UntilEndOfGame),
        }));
        Assert.That(aura.Activated, Is.Empty);
        Assert.That(aura.Scope, Is.EqualTo(ERuleScope.Unit));
        // OPR's 3.6.0 books print "This model and its unit Enhanced Brutal." / "... Enhanced Ferocious." (a missing
        // "get"); kept verbatim - #436 normalised OPR's whitespace slips only and kept every wording/grammar slip as printed.
        string expected = x is "Brutal" or "Ferocious"
            ? $"This model and its unit Enhanced {x}."
            : $"This model and its unit get Enhanced {x}.";
        Assert.That(aura.Description, Is.EqualTo(expected));
    }

    [TestCaseSource(nameof(Buffs))]
    public void TheShippedBuff_GrantsTheEnhancedRuleOnce_ToAFriendWithin12_WithThe36Text(string file, string x)
    {
        SpecialRuleDefinition buff = Shipped(file, $"Enhanced {x} Buff");

        Assert.That(buff.Passive, Is.Empty);
        ActivatedAbility ability = buff.Activated.Single();
        Assert.That(ability.TriggerHook, Is.EqualTo(EHookID.Activation_OnBeforeAttackAction));
        Assert.That(ability.Cost, Is.EqualTo(new Cost.OncePerActivation()));
        Assert.That(ability.TargetSelector.TargetAffinity, Is.EqualTo(ETargetAffinity.Friend));
        Assert.That(ability.TargetSelector.RangeInches, Is.EqualTo(12f));
        Assert.That((ability.TargetSelector.MinCount, ability.TargetSelector.MaxCount), Is.EqualTo((1, 1)));
        Assert.That(ability.Effect, Is.EqualTo(new Effect.AddRule($"Enhanced {x}", ELifetime.NextTrigger)));
        Assert.That(ability.AvailableWhen, Is.EqualTo(new Condition.Always()));
        Assert.That(buff.Description, Is.EqualTo("Once per activation, before attacking, pick one friendly unit within " +
            $"12\", which gets Enhanced {x} once (next time the effect would apply)."));
    }

    [Test]
    public void EveryWrapper_GrantsAnEnhancedRule_DefinedInTheSameUniverse_ThatReadsNoArgument()
    {
        var gdfUniverse = Load(Gdf).ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);
        var aofUniverse = Load(Gdf).Concat(Load(Aof)).GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        foreach ((string file, Dictionary<string, SpecialRuleDefinition> universe) in new[] { (Gdf, gdfUniverse), (Aof, aofUniverse) })
        {
            foreach (SpecialRuleDefinition rule in Load(file).Where(r => r.Name.StartsWith("Enhanced ")
                         && (r.Name.EndsWith(" Aura") || r.Name.EndsWith(" Buff"))))
            {
                string granted = rule.Passive.Select(e => e.Effect).OfType<Effect.Aura>().Select(a => a.RuleName)
                    .Concat(rule.Activated.Select(a => a.Effect).OfType<Effect.AddRule>().Select(a => a.RuleName))
                    .Single();
                Assert.That(universe.ContainsKey(granted), Is.True, $"{file}: {rule.Name} grants '{granted}'");
                // RuleEvaluator.CollectGrantedRules skips a granted rule that reads Arg(n) - grants carry no arguments.
                Assert.That(RuleArgumentArity.MaxReferencedArgIndex(universe[granted]), Is.EqualTo(-1),
                    $"{granted} must read no argument to be grantable");
            }
        }
    }

    // --- the whole family --------------------------------------------------------------------------------------------

    [Test]
    public void TheFamily_IsExactlyThe36Inventory()
    {
        // Every "Enhanced X" / Aura / Buff the 3.6.0 book snapshots define (91 names referenced by units, the rest
        // defined for spells and future books), 112 definitions, and nothing else under the prefix.
        var expected = new HashSet<string>(PassiveRules().Select(c => (string)c.Arguments[1]!)
            .Concat(GdfAuras.Concat(AofAuras).Select(x => $"Enhanced {x} Aura"))
            .Concat(GdfBuffs.Concat(AofBuffs).Select(x => $"Enhanced {x} Buff")));
        List<string> shipped = Load(Gdf).Concat(Load(Aof)).Select(r => r.Name).Where(n => n.StartsWith("Enhanced ")).ToList();

        Assert.That(shipped, Is.EquivalentTo(expected));
        Assert.That(shipped.Count, Is.EqualTo(112));
    }

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

    // A UnitHasRule(x) in a conjunctive position (the And chain), the shape of every Enhanced entry. The pick rules gate
    // each entry on one of the pick's helpers ("Versatile Warrior (Piercing)" / "(Precision)"), so their x is the prefix.
    private static bool Gates(Condition condition, string x) => condition switch
    {
        Condition.UnitHasRule has => has.RuleName.StartsWith(x, StringComparison.OrdinalIgnoreCase),
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
    public void ShippedPlagueboundAndEnhancedPlaguebound_FoldToOneFivePlusIgnore()
    {
        var world = new World();
        UnitData attacker = world.Unit();
        UnitData daemons = world.Unit(Shipped(Gdf, "Plaguebound"), Shipped(Gdf, "Enhanced Plaguebound"));
        UnitData pretender = world.Unit(Shipped(Gdf, "Enhanced Plaguebound"));

        WoundIgnoreSink Fold(UnitData defender)
        {
            var sink = new WoundIgnoreSink();
            sink.ApplyFrom(world.Evaluator.EvaluateAll(new SaveRollCompleteContext(attacker, defender, TestFaces(1)),
                RuleParticipant.Actor(attacker), RuleParticipant.Subject(defender, models: HeroStatRules.LivingModels(defender))));
            return sink;
        }

        Assert.That(Fold(daemons).Threshold, Is.EqualTo(5), "6+ and 5+ fold to one roll at 5+");
        Assert.That(Fold(pretender).HasIgnore, Is.False, "Enhanced Plaguebound without Plaguebound is inert");
    }

    [Test]
    public void ShippedSteadfastAndEnhancedSteadfast_FoldToOneThreePlusRoll()
    {
        var world = new World();
        UnitData unit = world.Unit(Shipped(Gdf, "Steadfast"), Shipped(Gdf, "Enhanced Steadfast"));
        unit.Tokens.AddToken(new FDG.Rules.Tokens.Token(TokenType.Shaken, 1, new TokenClearTrigger.ManualOnly()));

        var sink = new TokenClearRollSink();
        sink.ApplyFrom(world.Evaluator.EvaluateAll(new RoundStartContext(unit), RuleParticipant.Actor(unit)));

        Assert.That(sink.Entries, Is.EqualTo(new[] { (TokenType.Shaken, 3) }));
    }

    [Test]
    public void ShippedChangeboundAndEnhancedChangebound_MinusOneToHit_AtEveryDistance_Once()
    {
        var world = new World();
        UnitData attacker = world.Unit();
        UnitData daemons = world.Unit(Shipped(Gdf, "Changebound"), Shipped(Gdf, "Enhanced Changebound"));
        UnitData pretender = world.Unit(Shipped(Gdf, "Enhanced Changebound"));

        int NetHit(UnitData defender, HitRollModifierContext context)
        {
            var sink = new RollModifierSink();
            sink.ApplyFrom(world.Evaluator.EvaluateAll(context, RuleParticipant.Actor(attacker),
                RuleParticipant.Subject(defender, models: HeroStatRules.LivingModels(defender))));
            return sink.Net(ERollKind.Hit);
        }

        Assert.That(NetHit(daemons, new HitRollModifierContext(attacker, daemons, 10f)), Is.EqualTo(-1), "the base arm");
        Assert.That(NetHit(daemons, new HitRollModifierContext(attacker, daemons, 8f)), Is.EqualTo(-1), "the Enhanced arm");
        Assert.That(NetHit(daemons, new HitRollModifierContext(attacker, daemons, 0.5f, IsMelee: true, IsCharging: true,
            ChargeOriginDistanceInches: 7f)), Is.EqualTo(-1));
        Assert.That(NetHit(daemons, new HitRollModifierContext(attacker, daemons, 0.5f, IsMelee: true, IsCharging: true,
            ChargeOriginDistanceInches: 5f)), Is.EqualTo(-1));
        Assert.That(NetHit(pretender, new HitRollModifierContext(attacker, pretender, 8f)), Is.EqualTo(0), "inert alone");
    }

    [Test]
    public void ShippedBloodbornAndEnhancedBloodborn_OneExtraHitPerSuccessfulFiveOrSix()
    {
        var world = new World();
        UnitData target = world.Unit();
        UnitData berserkers = world.Unit(Shipped(Gdf, "Bloodborn"), Shipped(Gdf, "Enhanced Bloodborn"));
        UnitData pretender = world.Unit(Shipped(Gdf, "Enhanced Bloodborn"));

        float Extra(UnitData attacker, int needed, params int[] faces)
        {
            var sink = new HitInjectionSink();
            sink.ApplyFrom(world.Evaluator.EvaluateAll(
                new HitRollCompleteContext(attacker, target, TestFaces(faces), 10f, HitRollNeeded: needed),
                RuleParticipant.Actor(attacker), RuleParticipant.Subject(target)));
            return sink.TotalExtraHits;
        }

        Assert.That(Extra(berserkers, 4, 5, 6), Is.EqualTo(2f), "one from the 5, one from the 6");
        Assert.That(Extra(berserkers, 4, 6, 6), Is.EqualTo(2f), "the 6s pay once each - the delta is only the 5 face");
        Assert.That(Extra(berserkers, 6, 5, 6), Is.EqualTo(1f), "needing 6s: the 5 missed and pays nothing");
        Assert.That(Extra(pretender, 4, 5), Is.EqualTo(0f), "Enhanced Bloodborn without Bloodborn is inert");
    }

    private static DiceResults TestFaces(params int[] faces)
    {
        var perSide = new float[6];
        foreach (int face in faces) perSide[face - 1] += 1f;
        return new DiceResults(perSide);
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
