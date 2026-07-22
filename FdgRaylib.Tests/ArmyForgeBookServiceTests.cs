using System.Linq;
using FDG.ArmyBuilding;
using FdgRaylib.Import;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #219 — the pure, network-free core of --import-book: re-import OPR's book (now cost-aware) and copy the
// resolved per-unit Cost + costUnpriced onto a bundled book, matched by (unit Id, option Id). OPR omits the
// flat `cost` on options it prices per unit in a `costs[]` array; the bundled snapshots lost that (all showed
// 0 / costUnpriced), and this restores the real numbers - keyed per unit, since the SAME option costs
// differently on different units.
[TestFixture]
public class ArmyForgeBookServiceTests
{
    // Two units share package P1. "pistol" omits `cost` but is priced per unit in `costs[]` (10 on the noble,
    // 5 on the protector). "free" is an explicit 0. "mystery" has neither - genuinely unpriced.
    private const string OprJson = """
    {
      "name": "Cost Legion", "versionString": "3.5.3",
      "units": [
        { "id": "noble",    "name": "Noble",    "size": 1, "cost": 45, "quality": 3, "defense": 4, "upgrades": ["P1"] },
        { "id": "protector","name": "Protector","size": 1, "cost": 35, "quality": 4, "defense": 5, "upgrades": ["P1"] }
      ],
      "upgradePackages": [
        { "uid": "P1", "sections": [
          { "id":"s1", "label":"Wargear",
            "options":[
              { "id":"pistol", "label":"Master Laser Pistol",
                "costs":[ {"cost":10,"unitId":"noble"}, {"cost":5,"unitId":"protector"} ] },
              { "id":"free", "label":"Trinket", "cost":0 },
              { "id":"mystery", "label":"Unknowable" }
            ] }
        ] }
      ]
    }
    """;

    // Simulate the shipped catalog state before this fix (post Slice-2): the cost-blind importer read no
    // per-unit price, so the shared "pistol" option sat at 0 / flagged unpriced on every unit.
    private static BookFile StaleBundled()
    {
        BookFile book = OprBookImporter.Import(OprJson, "OnePageRules", "CC-BY-SA 4.0");
        foreach (UpgradeOption o in book.Units.SelectMany(u => u.Sections).SelectMany(s => s.Options))
        {
            if (o.Id == "pistol") { o.Cost = 0; o.CostUnpriced = true; }
        }
        return book;
    }

    private static UpgradeOption Opt(BookFile book, string unit, string id) =>
        book.Units.Single(u => u.Name == unit).Sections.SelectMany(s => s.Options).First(o => o.Id == id);

    [Test]
    public void RefreshCosts_RecoversPerUnitPrices()
    {
        BookFile book = StaleBundled();

        var report = ArmyForgeBookService.RefreshCosts(book, OprJson);

        Assert.That(Opt(book, "Noble", "pistol").Cost, Is.EqualTo(10), "the noble's price, not the protector's");
        Assert.That(Opt(book, "Protector", "pistol").Cost, Is.EqualTo(5), "same option Id, different unit, different price");
        Assert.That(Opt(book, "Noble", "pistol").CostUnpriced, Is.False);
        Assert.That(report.Priced, Is.EqualTo(2), "one recovered cost per unit");
        Assert.That(report.Unmatched, Is.EqualTo(0));
    }

    [Test]
    public void RefreshCosts_LeavesGenuinelyUnpricedAndFreeAlone()
    {
        BookFile book = StaleBundled();

        ArmyForgeBookService.RefreshCosts(book, OprJson);

        Assert.That(Opt(book, "Noble", "mystery").CostUnpriced, Is.True, "no cost and no costs[] stays flagged");
        Assert.That(Opt(book, "Noble", "free").Cost, Is.EqualTo(0));
        Assert.That(Opt(book, "Noble", "free").CostUnpriced, Is.False, "an explicit 0 stays a real free option");
    }

    [Test]
    public void RefreshCosts_CountsUnmatchedOptionsWithoutTouchingThem()
    {
        BookFile book = StaleBundled();
        book.Units[0].Sections[0].Options.Add(new UpgradeOption { Id = "gone", Label = "Legacy", Cost = 5 });

        var report = ArmyForgeBookService.RefreshCosts(book, OprJson);

        Assert.That(Opt(book, "Noble", "gone").Cost, Is.EqualTo(5), "an option the live book lacks is untouched");
        Assert.That(report.Unmatched, Is.EqualTo(1));
    }
}
