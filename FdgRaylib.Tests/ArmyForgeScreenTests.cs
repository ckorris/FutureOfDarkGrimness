using System.Linq;
using FdgRaylib.ArmyBuilding;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #153 (P1) — the pure formatting seams behind the ArmyForge three-pane viewer. The ImGui layout itself is
// hand-verified in the running window; these pin the text.
[TestFixture]
public class ArmyForgeScreenTests
{
    [Test]
    public void PointsHeader_ShowsTotalOverLimit()
    {
        Assert.That(ArmyForgeScreen.PointsHeader(271, 500), Is.EqualTo("271 / 500 pts"));
    }

    [Test]
    public void RosterStatLine_ShowsSizeQualityDefenseCost()
    {
        RosterUnit warriors = DemoBook.Build().Units.Single(u => u.Id == "warriors");
        Assert.That(ArmyForgeScreen.RosterStatLine(warriors),
            Is.EqualTo("Vanguard Warriors [5] - Qua 4+ Def 4+  (65 pts)"));
    }

    [Test]
    public void OptionSummary_AppendsCost_WhenNonZero()
    {
        var paid = new UpgradeOption { Label = "Plasma Rifle", Cost = 5 };
        var free = new UpgradeOption { Label = "Combat Blade", Cost = 0 };
        Assert.That(ArmyForgeScreen.OptionSummary(paid), Is.EqualTo("Plasma Rifle  (+5 pts)"));
        Assert.That(ArmyForgeScreen.OptionSummary(free), Is.EqualTo("Combat Blade"));
    }

    [Test]
    public void DemoBook_HasExpectedRosterAndUpgrades()
    {
        BookFile book = DemoBook.Build();
        Assert.That(book.Units.Select(u => u.Id), Is.EquivalentTo(new[] { "warriors", "gunners" }));
        RosterUnit warriors = book.Units.Single(u => u.Id == "warriors");
        Assert.That(warriors.Sections.Select(s => s.Variant),
            Does.Contain(UpgradeVariant.Replace).And.Contain(UpgradeVariant.AddModels).And.Contain(UpgradeVariant.Upgrade));
    }
}
