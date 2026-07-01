using System.Linq;
using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
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

    // ── P2: list building ───────────────────────────────────────────────────────────────────────────────

    [Test]
    public void AddToList_ThenCompile_SumsBasePoints()
    {
        var screen = new ArmyForgeScreen();
        screen.AddToList("warriors");
        screen.AddToList("gunners");

        BuiltArmyFile army = screen.Compile();
        Assert.That(army.Units.Select(u => u.Name), Is.EqualTo(new[] { "Vanguard Warriors", "Heavy Gunners" }));
        Assert.That(army.TotalPoints, Is.EqualTo(185)); // 65 + 120 base, no options chosen yet
    }

    [Test]
    public void RemoveFromList_DropsThatUnit()
    {
        var screen = new ArmyForgeScreen();
        screen.AddToList("warriors");
        screen.AddToList("gunners");
        screen.RemoveFromList(0);

        Assert.That(screen.Compile().Units.Single().Name, Is.EqualTo("Heavy Gunners"));
    }

    [Test]
    public void AddToList_UnknownRosterId_IsIgnored()
    {
        var screen = new ArmyForgeScreen();
        screen.AddToList("does-not-exist");
        Assert.That(screen.List.Units, Is.Empty);
    }

    [Test]
    public void SaveLoadRoundTrip_RestoresEditableList()
    {
        var a = new ArmyForgeScreen();
        a.AddToList("warriors");
        a.AddToList("gunners");

        // Exactly what Save writes (derived type → embed included).
        string json = JsonSerializer.Serialize(a.Compile(), RuleJson.Options);
        BuiltArmyFile loaded = JsonSerializer.Deserialize<BuiltArmyFile>(json, RuleJson.Options)!;

        var b = new ArmyForgeScreen();
        Assert.That(b.AdoptLoaded(loaded), Is.True);
        Assert.That(b.List.Units.Select(u => u.RosterUnitId), Is.EqualTo(new[] { "warriors", "gunners" }));
        Assert.That(b.Compile().TotalPoints, Is.EqualTo(185));
    }

    [Test]
    public void AdoptLoaded_PlainArmy_ReturnsFalse()
    {
        // A hand-authored .fdgarmy (no embedded book/selections) can't be catalog-edited.
        Assert.That(new ArmyForgeScreen().AdoptLoaded(new BuiltArmyFile()), Is.False);
    }

    // ── P3: interactive upgrade choices ─────────────────────────────────────────────────────────────────

    [Test]
    public void SetChoice_SingleSelectSection_IsMutuallyExclusive()
    {
        var unit = new BuilderUnit { RosterUnitId = "x" };
        var section = new UpgradeSection { Id = "s", MaxPicks = 1, Options = { new() { Id = "a" }, new() { Id = "b" } } };

        ArmyForgeScreen.SetChoice(unit, section, "a", 1);
        Assert.That(ArmyForgeScreen.IsChosen(unit, "s", "a"), Is.True);

        ArmyForgeScreen.SetChoice(unit, section, "b", 1);   // picking b clears a
        Assert.That(ArmyForgeScreen.IsChosen(unit, "s", "a"), Is.False);
        Assert.That(ArmyForgeScreen.IsChosen(unit, "s", "b"), Is.True);
        Assert.That(unit.Choices, Has.Count.EqualTo(1));

        ArmyForgeScreen.SetChoice(unit, section, "b", 0);   // unticking clears it
        Assert.That(unit.Choices, Is.Empty);
    }

    [Test]
    public void SetChoice_CountedSection_StoresCount()
    {
        var unit = new BuilderUnit();
        var section = new UpgradeSection { Id = "r", Variant = UpgradeVariant.AddModels, Options = { new() { Id = "add" } } };

        ArmyForgeScreen.SetChoice(unit, section, "add", 3);
        Assert.That(ArmyForgeScreen.ChoiceCount(unit, "r", "add"), Is.EqualTo(3));
    }

    [Test]
    public void UpgradeChoices_ThenCompile_MatchTheCompilersCost_Warriors()
    {
        var screen = new ArmyForgeScreen();
        screen.AddToList("warriors");
        BuilderUnit bu = screen.List.Units[0];

        RosterUnit warriors = DemoBook.Build().Units.Single(u => u.Id == "warriors");
        ArmyForgeScreen.SetChoice(bu, warriors.Sections.Single(s => s.Id == "warriors-special"), "plasma", 1);
        ArmyForgeScreen.SetChoice(bu, warriors.Sections.Single(s => s.Id == "warriors-reinforce"), "add-warrior", 2);
        ArmyForgeScreen.SetChoice(bu, warriors.Sections.Single(s => s.Id == "warriors-banner"), "war-banner", 1);

        UnitFileEntry compiled = screen.Compile().Units.Single();
        Assert.That(compiled.ModelCount, Is.EqualTo(7));
        Assert.That(compiled.PointCost, Is.EqualTo(106)); // 65 + 5 + 13×2 + 10
    }

    [Test]
    public void UpgradeChoices_ReplaceAll_ThenCompile_Gunners()
    {
        var screen = new ArmyForgeScreen();
        screen.AddToList("gunners");
        BuilderUnit bu = screen.List.Units[0];

        RosterUnit gunners = DemoBook.Build().Units.Single(u => u.Id == "gunners");
        ArmyForgeScreen.SetChoice(bu, gunners.Sections.Single(s => s.Id == "gunners-missiles"), "missile", 1);

        Assert.That(screen.Compile().Units.Single().PointCost, Is.EqualTo(165)); // 120 + 15×3 (all 3 swapped)
    }
}
