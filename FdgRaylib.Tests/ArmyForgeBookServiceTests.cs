using System.Linq;
using FDG.ArmyBuilding;
using FdgRaylib.Import;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #219 Slice 2 — the pure, network-free core of --import-book: transfer OPR's "cost absent" distinction onto
// a bundled book's costUnpriced flags by option Id, without disturbing anything else. OPR omits the `cost`
// key on options it prices in its own algorithm (o2 below) and writes a number on the rest (o1); the bundled
// snapshots lost that distinction (all costUnpriced=false), and this restores it.
[TestFixture]
public class ArmyForgeBookServiceTests
{
    // Minimal OPR army-book JSON: o1 is priced (cost:10), o2 omits `cost` entirely (unpriced), o3 is an
    // explicit free option (cost:0 - genuinely free, must NOT be flagged).
    private const string OprJson = """
    {
      "name": "Test Legion", "versionString": "3.5.3",
      "units": [
        { "id": "u1", "name": "Grunts", "size": 5, "cost": 100, "quality": 4, "defense": 4,
          "weapons": [ { "name": "Blade", "count": 5, "range": null, "attacks": 2, "specialRules": [] } ],
          "rules": [], "upgrades": ["P1"] }
      ],
      "upgradePackages": [
        { "uid": "P1", "sections": [
          { "id":"s1", "label":"Wargear", "affects":{"type":"any"},
            "options":[
              { "id":"o1", "label":"Paid Blade", "cost":10 },
              { "id":"o2", "label":"Hexer" },
              { "id":"o3", "label":"Free Trinket", "cost":0 }
            ] }
        ] }
      ]
    }
    """;

    private static BookFile BundledFromOpr()
    {
        // Simulate the shipped catalog state: imported once, then a later re-serialize stamped every flag false.
        BookFile book = OprBookImporter.Import(OprJson, "OnePageRules", "CC-BY-SA 4.0");
        foreach (UpgradeOption o in book.Units.SelectMany(u => u.Sections).SelectMany(s => s.Options))
            o.CostUnpriced = false;
        return book;
    }

    private static UpgradeOption Opt(BookFile book, string id) =>
        book.Units.SelectMany(u => u.Sections).SelectMany(s => s.Options).First(o => o.Id == id);

    [Test]
    public void RefreshCostFlags_FlagsOnlyTheCostAbsentOption()
    {
        BookFile book = BundledFromOpr();

        var report = ArmyForgeBookService.RefreshCostFlags(book, OprJson);

        Assert.That(Opt(book, "o2").CostUnpriced, Is.True, "the option OPR left without a cost is now flagged");
        Assert.That(Opt(book, "o1").CostUnpriced, Is.False, "a priced option stays priced");
        Assert.That(Opt(book, "o3").CostUnpriced, Is.False, "an explicit 0 (genuinely free) stays unflagged");
        Assert.That(report.Flagged, Is.EqualTo(1));
        Assert.That(report.Cleared, Is.EqualTo(0));
        Assert.That(report.Unmatched, Is.EqualTo(0));
    }

    [Test]
    public void RefreshCostFlags_LeavesUnknownOptionsUntouchedAndCountsThem()
    {
        BookFile book = BundledFromOpr();
        // An option the live endpoint no longer has (OPR renamed/removed it since the snapshot).
        book.Units[0].Sections[0].Options.Add(new UpgradeOption { Id = "gone", Label = "Legacy", Cost = 5 });

        var report = ArmyForgeBookService.RefreshCostFlags(book, OprJson);

        Assert.That(Opt(book, "gone").CostUnpriced, Is.False, "an unmatched option is left exactly as-is");
        Assert.That(report.Unmatched, Is.EqualTo(1));
        Assert.That(report.Flagged, Is.EqualTo(1));
    }

    [Test]
    public void RefreshCostFlags_ClearsAStaleTrueFlag()
    {
        BookFile book = BundledFromOpr();
        Opt(book, "o1").CostUnpriced = true; // wrongly marked unpriced; the live book prices it

        var report = ArmyForgeBookService.RefreshCostFlags(book, OprJson);

        Assert.That(Opt(book, "o1").CostUnpriced, Is.False);
        Assert.That(report.Cleared, Is.EqualTo(1));
    }
}
