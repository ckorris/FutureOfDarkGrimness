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
        // Three distinct 110-pt squads, each with a 20-pt swap, so no step trips a copies or 35% force-org line.
        RosterUnit Squad(string id) { RosterUnit u = Warriors("Rifle", 24, 110); u.Id = id; u.Name = id; return u; }
        var book = new BookFile { Name = "Demo Legion", Faction = "Demo Legion", Version = "OPR v3.6.0", Units = { Squad("A"), Squad("B"), Squad("C") } };
        BuilderUnit Unit(string id, bool swapped) => new()
        {
            RosterUnitId = id, ModelCount = 5,
            Choices = swapped ? new() { new UpgradeChoice { SectionId = "w-heavy", OptionId = "w-heavy-opt" } } : new(),
        };

        // 3 x 130 = 390 in a 370 bracket (floor 352): one swap cut lands on 370; a unit cut would overshoot to 260.
        var over = new BuilderList { BookName = book.Name, PointsLimit = 370, Units = { Unit("A", true), Unit("B", true), Unit("C", true) } };
        var cut = new List<string>();
        ArmyReforge.FitBracket(book, over, 370, cut);
        Assert.That(cut, Has.Count.EqualTo(1), string.Join("; ", cut));
        Assert.That(cut[0], Does.StartWith("cut upgrade"));
        Assert.That(ListCompiler.Compile(book, over).TotalPoints, Is.EqualTo(370));

        // 3 x 110 = 330 in a 400 bracket (floor 380): swaps (+20 each) come before a copy, and stop inside the band.
        var under = new BuilderList { BookName = book.Name, PointsLimit = 400, Units = { Unit("A", false), Unit("B", false), Unit("C", false) } };
        var added = new List<string>();
        ArmyReforge.FitBracket(book, under, 400, added);
        Assert.That(added, Is.Not.Empty);
        Assert.That(added, Has.All.StartWith("add upgrade"), string.Join("; ", added));
        Assert.That(ListCompiler.Compile(book, under).TotalPoints, Is.InRange(380, 400));
    }
}
