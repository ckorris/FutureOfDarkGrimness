using System.Text.Json;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using FdgRaylib.ArmyBuilding;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #153 (P0) — the catalog/selection/embedded-file types round-trip through the same STJ kind-schema as a
// hand-authored .fdgarmy, and — the load-bearing claim of the whole feature — the engine reads a builder-saved
// file as a plain ArmyListFile, silently ignoring the embedded book + selections (so embedding needs no
// engine change).
[TestFixture]
public class ArmyForgeSerializationTests
{
    private static BookFile MakeBook() => new()
    {
        Name = "Demo Legion", Faction = "Demo", Version = "1.0.0",
        Units =
        {
            new RosterUnit
            {
                Id = "warriors", Name = "Warriors",
                Quality = 4, Defense = 4, BaseModelCount = 5, MinModels = 5, MaxModels = 10, BasePointCost = 65,
                Weapons = { new WeaponFileEntry { Name = "Rifle", Quantity = 5, RangeInches = 24, Attacks = 1 } },
                Rules = { new SpecialRuleEntry_CoreNumeric("Tough", 3) },
                Sections =
                {
                    new UpgradeSection
                    {
                        Id = "warriors-heavy", Label = "Upgrade one Rifle to a Heavy Rifle",
                        Variant = UpgradeVariant.Replace, Affects = UpgradeAffects.One, Targets = { "Rifle" },
                        Options =
                        {
                            new UpgradeOption
                            {
                                Id = "heavy-rifle", Label = "Heavy Rifle (36\", A1, AP(1))", Cost = 5,
                                WeaponsGained =
                                {
                                    new WeaponFileEntry
                                    {
                                        Name = "Heavy Rifle", Quantity = 1, RangeInches = 36, Attacks = 1,
                                        SpecialRules = { new SpecialRuleEntry_CoreNumeric("AP", 1) },
                                    },
                                },
                            },
                        },
                    },
                },
            },
            new RosterUnit
            {
                Id = "gunners", Name = "Heavy Gunners",
                Quality = 4, Defense = 4, BaseModelCount = 3, MinModels = 3, MaxModels = 3, BasePointCost = 120,
                Weapons = { new WeaponFileEntry { Name = "Heavy Rifle", Quantity = 3, RangeInches = 36, Attacks = 1 } },
            },
        },
    };

    private static BuiltArmyFile MakeBuiltArmy()
    {
        BookFile book = MakeBook();
        return new BuiltArmyFile
        {
            Name = "My List", Faction = "Demo", PointsLimit = 500,
            // A representative compiled unit (the real compiler produces these in the P0 compiler slice).
            Units =
            {
                new UnitFileEntry
                {
                    Name = "Warriors", ModelCount = 5, Quality = 4, Defense = 4, PointCost = 70,
                    SpecialRules = { new SpecialRuleEntry_CoreNumeric("Tough", 3) },
                    Weapons =
                    {
                        new WeaponFileEntry { Name = "Rifle", Quantity = 4, RangeInches = 24, Attacks = 1 },
                        new WeaponFileEntry
                        {
                            Name = "Heavy Rifle", Quantity = 1, RangeInches = 36, Attacks = 1,
                            SpecialRules = { new SpecialRuleEntry_CoreNumeric("AP", 1) },
                        },
                    },
                },
            },
            Selections = new BuilderList
            {
                BookName = "Demo Legion", PointsLimit = 500,
                Units = { new BuilderUnit
                {
                    RosterUnitId = "warriors", ModelCount = 5,
                    Choices = { new UpgradeChoice { SectionId = "warriors-heavy", OptionId = "heavy-rifle", Count = 1 } },
                } },
            },
            Book = book,
        };
    }

    [Test]
    public void BuiltArmyFile_RoundTripsStructurally_AsDerivedType()
    {
        BuiltArmyFile built = MakeBuiltArmy();

        string first = JsonSerializer.Serialize(built, RuleJson.Options);
        BuiltArmyFile back = JsonSerializer.Deserialize<BuiltArmyFile>(first, RuleJson.Options)!;
        string second = JsonSerializer.Serialize(back, RuleJson.Options);

        Assert.That(second, Is.EqualTo(first), "built army did not round-trip structurally.");

        // Base (playable) view survived.
        Assert.That(back.Units, Has.Count.EqualTo(1));
        Assert.That(back.Units[0].Weapons, Has.Count.EqualTo(2));

        // Embedded editable state survived, including polymorphic rule entries and the full book snapshot.
        Assert.That(back.Selections, Is.Not.Null);
        Assert.That(back.Selections!.Units[0].RosterUnitId, Is.EqualTo("warriors"));
        Assert.That(back.Selections.Units[0].Choices[0].OptionId, Is.EqualTo("heavy-rifle"));
        Assert.That(back.Book, Is.Not.Null);
        Assert.That(back.Book!.Units, Has.Count.EqualTo(2));
        Assert.That(back.Book.Units[0].Rules[0], Is.EqualTo(new SpecialRuleEntry_CoreNumeric("Tough", 3)));
        Assert.That(back.Book.Units[0].Sections[0].Options[0].Cost, Is.EqualTo(5));
    }

    [Test]
    public void Engine_ReadsBuilderFile_AsPlainArmy_IgnoringEmbeddedBlock()
    {
        // The exact bytes the builder writes.
        string json = JsonSerializer.Serialize(MakeBuiltArmy(), RuleJson.Options);

        // The engine / lobby / headless loader all deserialize as ArmyListFile.
        ArmyListFile asArmy = JsonSerializer.Deserialize<ArmyListFile>(json, RuleJson.Options)!;

        Assert.That(asArmy, Is.Not.Null);
        Assert.That(asArmy.Units, Has.Count.EqualTo(1), "playable units must survive for the engine.");
        Assert.That(asArmy.Units[0].Name, Is.EqualTo("Warriors"));

        // Re-serializing the engine's view carries none of the embedded block — it was dropped on read.
        string engineView = JsonSerializer.Serialize(asArmy, RuleJson.Options);
        Assert.That(engineView, Does.Not.Contain("\"selections\""));
        Assert.That(engineView, Does.Not.Contain("\"book\""));
    }

    [Test]
    public void PlainArmyFile_DeserializesAsBuiltArmy_WithNullSelections()
    {
        // A hand-authored .fdgarmy (no embedded block) opened via the builder's derived type is graceful:
        // it still carries its playable units; it just isn't catalog-re-editable.
        var plain = new ArmyListFile { Name = "Hand", PointsLimit = 300,
            Units = { new UnitFileEntry { Name = "Grunts", ModelCount = 3, Quality = 5, Defense = 5, PointCost = 45 } } };
        string json = JsonSerializer.Serialize(plain, RuleJson.Options);

        BuiltArmyFile asBuilt = JsonSerializer.Deserialize<BuiltArmyFile>(json, RuleJson.Options)!;

        Assert.That(asBuilt.Units, Has.Count.EqualTo(1));
        Assert.That(asBuilt.Selections, Is.Null);
        Assert.That(asBuilt.Book, Is.Null);
    }
}
