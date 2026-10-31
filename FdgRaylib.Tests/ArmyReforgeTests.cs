using FDG.ArmyBuilding;
using FDG.SaveLoad;
using FdgRaylib.Cli;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #440 - the `--reforge` dev tool that rebuilt the Random Army catalog and the FdgLab pool against the 3.6 books.
// Pins the parts that make its output trustworthy: the bracket read from a catalog file name, the closeness that
// ranks a nearest fit, the edition change end to end (recovered picks, recompiled stats, reopenable in the Forge),
// and the bracket fit's cut/add order.
[TestFixture]
public class ArmyReforgeTests
{
    [TestCase("1k - Ogres.fdgarmy", 1000)]
    [TestCase("2k - Orks - Horde Mixed.fdgarmy", 2000)]
    [TestCase("Wormhole Demons of Change 1k.fdgarmy", 1000)]
    [TestCase("High Elf Fleets 2k  - Caster-Heavy.fdgarmy", 2000)]
    [TestCase("4k - Alien Hives.fdgarmy", 4000)]
    [TestCase("Dummies.fdgarmy", null)]
    public void TheBracketComesFromTheFileName(string file, int? bracket) =>
        Assert.That(ArmyReforge.BracketFromFileName(file), Is.EqualTo(bracket));

    private static RosterUnit Warriors(string rifle, int rifleRange, int cost) => new()
    {
        Id = "warriors", Name = "Warriors", Quality = 4, Defense = 4, BaseModelCount = 5, MinModels = 5, MaxModels = 5,
        BasePointCost = cost,
        Weapons = { new WeaponFileEntry { Name = rifle, Quantity = 5, RangeInches = rifleRange, Attacks = 1 } },
        Sections =
        {
            new UpgradeSection
            {
                Id = "w-heavy", Label = "Replace one " + rifle, Variant = UpgradeVariant.Replace, Affects = UpgradeAffects.One,
                Targets = { rifle },
                Options =
                {
                    new UpgradeOption
                    {
                        Id = "w-heavy-opt", Label = "Heavy Rifle (36\", A1, AP(1))", Cost = 20,
                        WeaponsGained = { new WeaponFileEntry { Name = "Heavy Rifle", Quantity = 1, RangeInches = 36, Attacks = 1, ArmorPenetration = 1 } },
                    },
                },
            },
        },
    };

    private static BookFile Book(string version, string rifle, int cost) => new()
    {
        Name = "Demo Legion", Faction = "Demo Legion", Version = version,
        Units = { Warriors(rifle, 24, cost) },
    };

    [Test]
    public void AnEditionChange_RecoversThePicks_RecompilesTheStats_AndReopensInTheForge()
    {
        // 3.5: "Rifle" + one Heavy Rifle swap. 3.6 renamed the base weapon "Battle Rifle" and repriced the unit, so
        // no pick-set reproduces the saved loadout exactly - the label translation carries the swap across.
        BookFile old = Book("OPR v3.5.3", "Rifle", 100);
        BookFile now = Book("OPR v3.6.0", "Battle Rifle", 90);
        var picks = new BuilderList
        {
            Name = "Demo 1k", BookName = old.Name, PointsLimit = 1000,
            Units = { new BuilderUnit { RosterUnitId = "warriors", ModelCount = 5, Choices = { new UpgradeChoice { SectionId = "w-heavy", OptionId = "w-heavy-opt" } } } },
        };
        BuiltArmyFile saved = ListCompiler.Compile(old, picks);

        ArmyReforge.Report report = ArmyReforge.Reforge("Demo.fdgarmy", saved, new[] { now }, fitBracket: false);

        Assert.That(report.Error, Is.Null);
        Assert.That(report.Unsolved, Is.Zero);
        Assert.That(report.Units.Single().Method, Is.EqualTo("translated"));
        BuiltArmyFile rebuilt = report.Rebuilt!;
        Assert.That(rebuilt.Book!.Version, Is.EqualTo("OPR v3.6.0"), "the embedded book is the new edition");
        Assert.That(rebuilt.Units.Single().Weapons.Select(w => w.Name), Is.EquivalentTo(new[] { "Battle Rifle", "Heavy Rifle" }));
        Assert.That(rebuilt.TotalPoints, Is.EqualTo(110), "3.6 price: 90 + the 20-pt swap");
        Assert.That(EditableSession.Measure(rebuilt)!.Differs, Is.False, "the Forge reopens exactly this army");

        ArmyReforge.Report again = ArmyReforge.Reforge("Demo.fdgarmy", rebuilt, new[] { now }, fitBracket: false);
        Assert.That(again.Recompiled, Is.True, "a file already on the bundled edition recompiles from its own picks");
        Assert.That(again.Rebuilt!.TotalPoints, Is.EqualTo(110));
    }

    [Test]
    public void Closeness_PrefersTheRightSize_ThenTheSameWeapons()
    {
        var saved = new UnitFileEntry
        {
            Name = "Warriors", ModelCount = 5,
            Weapons = { new WeaponFileEntry { Name = "Rifle", Quantity = 5, RangeInches = 24, Attacks = 1 } },
        };
        UnitFileEntry Unit(int models, string weapon, int range) => new()
        {
            Name = "Warriors", ModelCount = models,
            Weapons = { new WeaponFileEntry { Name = weapon, Quantity = models, RangeInches = range, Attacks = 1 } },
        };

        double same = ArmyReforge.Closeness(Unit(5, "Rifle", 24), saved);
        double renamed = ArmyReforge.Closeness(Unit(5, "Battle Rifle", 24), saved);
        double melee = ArmyReforge.Closeness(Unit(5, "Sword", 0), saved);
        double smaller = ArmyReforge.Closeness(Unit(3, "Rifle", 24), saved);

        Assert.That(same, Is.GreaterThan(renamed));
        Assert.That(renamed, Is.GreaterThan(melee), "a renamed rifle is closer than a melee weapon");
        Assert.That(renamed, Is.GreaterThan(smaller), "size outranks a weapon name");
    }

    [Test]
    public void TheBracketFit_CutsUpgradesBeforeUnits_AndAddsUpgradesBeforeCopies()
    {
        BookFile book = Book("OPR v3.6.0", "Rifle", 480);
        BuilderUnit Upgraded() => new()
        {
            RosterUnitId = "warriors", ModelCount = 5,
            Choices = { new UpgradeChoice { SectionId = "w-heavy", OptionId = "w-heavy-opt" } },
        };

        // 2 x (480 + 20) = 1000 in a 990 bracket: dropping one 20-pt swap lands at 980, inside the band.
        var over = new BuilderList { BookName = book.Name, PointsLimit = 990, Units = { Upgraded(), Upgraded() } };
        var cut = new List<string>();
        ArmyReforge.FitBracket(book, over, 990, cut);
        Assert.That(cut, Has.Count.EqualTo(1));
        Assert.That(cut[0], Does.StartWith("cut upgrade"));
        Assert.That(ListCompiler.Compile(book, over).TotalPoints, Is.EqualTo(980));

        // 480 in a 520 bracket is under its 494 floor: the 20-pt swap lands at 500, inside; a copy (960) does not fit.
        var under = new BuilderList
        {
            BookName = book.Name, PointsLimit = 520,
            Units = { new BuilderUnit { RosterUnitId = "warriors", ModelCount = 5 } },
        };
        var added = new List<string>();
        ArmyReforge.FitBracket(book, under, 520, added);
        Assert.That(added, Has.Count.EqualTo(1));
        Assert.That(added[0], Does.StartWith("add upgrade"));
        Assert.That(ListCompiler.Compile(book, under).TotalPoints, Is.EqualTo(500));
    }
}
