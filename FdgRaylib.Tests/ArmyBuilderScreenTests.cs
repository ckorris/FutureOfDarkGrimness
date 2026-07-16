using System.Linq;
using FDG.SaveLoad;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #106: the pure formatting + clone seams behind the army-builder stat block and Duplicate button.
[TestFixture]
public class ArmyBuilderScreenTests
{
    [Test]
    public void WeaponSummary_Ranged_ShowsCountRangeAttacksRules()
    {
        var w = new WeaponFileEntry
        {
            Name = "Beautiful Boomstick", Quantity = 2, RangeInches = 24,
            Attacks = 2, ArmorPenetration = 0,
            SpecialRules = { new SpecialRuleEntry_Core("Rending") },
        };
        // AP 0 omitted; range shown for ranged.
        Assert.That(ArmyBuilderScreen.WeaponSummary(w),
            Is.EqualTo("2x Beautiful Boomstick (24\", A2, Rending)"));
    }

    [Test]
    public void WeaponSummary_Melee_OmitsRange_ShowsAp()
    {
        var w = new WeaponFileEntry
        {
            Name = "Heavy Serrated Blade", Quantity = 1, RangeInches = 0,
            Attacks = 6, ArmorPenetration = 4,
            SpecialRules = { new SpecialRuleEntry_Core("Reliable") },
        };
        Assert.That(ArmyBuilderScreen.WeaponSummary(w),
            Is.EqualTo("1x Heavy Serrated Blade (A6, AP(4), Reliable)"));
    }

    [Test]
    public void WeaponSummary_NoRulesNoAp_JustCountAndAttacks()
    {
        var w = new WeaponFileEntry { Name = "Knife", Quantity = 3, RangeInches = 0, Attacks = 1, ArmorPenetration = 0 };
        Assert.That(ArmyBuilderScreen.WeaponSummary(w), Is.EqualTo("3x Knife (A1)"));
    }

    [Test]
    public void WeaponSummary_NumericRule_PrintsValue()
    {
        var w = new WeaponFileEntry
        {
            Name = "Greatsword", Quantity = 1, Attacks = 2, ArmorPenetration = 1,
            SpecialRules = { new SpecialRuleEntry_CoreNumeric("Blast", 3) },
        };
        Assert.That(ArmyBuilderScreen.WeaponSummary(w),
            Is.EqualTo("1x Greatsword (A2, AP(1), Blast(3))"));
    }

    [Test]
    public void UnitStatLine_FormatsModelsQualityDefense()
    {
        var u = new UnitFileEntry { Name = "My Unit", ModelCount = 10, Quality = 3, Defense = 4 };
        Assert.That(ArmyBuilderScreen.UnitStatLine(u), Is.EqualTo("My Unit [10] - Qua 3+ Def 4+"));
    }

    [Test]
    public void CloneUnit_ClearsId_ForFreshIdentity()
    {
        var u = new UnitFileEntry { Name = "Host", Id = "abc123", ModelCount = 5 };

        var copy = ArmyBuilderScreen.CloneUnit(u);

        Assert.That(copy, Is.Not.Null);
        Assert.That(copy!.Id, Is.Null, "the copy must not share the source's cross-file Id");
        Assert.That(copy.Name, Is.EqualTo("Host"));
        Assert.That(copy.ModelCount, Is.EqualTo(5));
        Assert.That(copy.StableID, Is.Not.EqualTo(u.StableID), "the copy is a distinct entry");
    }

    [Test]
    public void CloneUnit_DeepCopies_RulesWeaponsAndBase()
    {
        var u = new UnitFileEntry
        {
            Name = "Bugs", ModelCount = 3, Quality = 4, Defense = 5,
            SpecialRules =
            {
                new SpecialRuleEntry_CoreNumeric("Tough", 12),
                new SpecialRuleEntry_Core("Fearless"),
            },
            Weapons =
            {
                new WeaponFileEntry
                {
                    Name = "Claws", Attacks = 4,
                    SpecialRules = { new SpecialRuleEntry_Core("Rending") },
                },
            },
            Base = new BaseFileEntry { Shape = EBaseShapeKind.Rectangle, WidthInches = 1f, HeightInches = 2f },
        };

        var copy = ArmyBuilderScreen.CloneUnit(u);
        Assert.That(copy, Is.Not.Null);

        // Values preserved through the round-trip.
        Assert.That(copy!.SpecialRules.Select(r => r.PrintableName),
            Is.EqualTo(new[] { "Tough(12)", "Fearless" }));
        Assert.That(copy.Weapons[0].SpecialRules[0].PrintableName, Is.EqualTo("Rending"));
        Assert.That(copy.Base.Shape, Is.EqualTo(EBaseShapeKind.Rectangle));
        Assert.That(copy.Base.WidthInches, Is.EqualTo(1f).Within(1e-4f));

        // Independent: mutating the copy's collections leaves the source untouched.
        copy.SpecialRules.Clear();
        copy.Weapons.Clear();
        Assert.That(u.SpecialRules, Has.Count.EqualTo(2));
        Assert.That(u.Weapons, Has.Count.EqualTo(1));
    }

    // #236: the freeform builder must RECOGNIZE a Forge-built army, so Save can gate on the detach
    // confirm instead of silently stripping the embedded book/selections block.
    [Test]
    public void HasForgeBlock_TrueOnlyWhenBookOrSelectionsPresent()
    {
        Assert.That(ArmyBuilderScreen.HasForgeBlock(null), Is.False);
        Assert.That(ArmyBuilderScreen.HasForgeBlock(new ArmyListFile()), Is.False, "plain freeform army");
        Assert.That(ArmyBuilderScreen.HasForgeBlock(new FDG.ArmyBuilding.BuiltArmyFile()), Is.False,
            "BuiltArmyFile with no block (a plain file read through the superset type)");
        Assert.That(ArmyBuilderScreen.HasForgeBlock(
            new FDG.ArmyBuilding.BuiltArmyFile { Book = new FDG.ArmyBuilding.BookFile() }), Is.True);
        Assert.That(ArmyBuilderScreen.HasForgeBlock(
            new FDG.ArmyBuilding.BuiltArmyFile { Selections = new FDG.ArmyBuilding.BuilderList() }), Is.True);
    }

    [Test]
    public void HasForgeBlock_SurvivesTheLoadPathRoundTrip()
    {
        // Mirrors ArmyBuilderScreen.Load: every file deserializes as BuiltArmyFile via RuleJson.Options.
        // A Forge file's block must be seen; a plain army's absence of one must read as false.
        var forge = System.Text.Json.JsonSerializer.Deserialize<FDG.ArmyBuilding.BuiltArmyFile>(
            "{\"name\":\"A\",\"units\":[],\"book\":{\"name\":\"B\"}}", FDG.Rules.Serialization.RuleJson.Options);
        Assert.That(ArmyBuilderScreen.HasForgeBlock(forge), Is.True, "book block detected through the load path");

        var plain = System.Text.Json.JsonSerializer.Deserialize<FDG.ArmyBuilding.BuiltArmyFile>(
            "{\"name\":\"A\",\"units\":[]}", FDG.Rules.Serialization.RuleJson.Options);
        Assert.That(ArmyBuilderScreen.HasForgeBlock(plain), Is.False, "plain army stays freeform");
    }
}
