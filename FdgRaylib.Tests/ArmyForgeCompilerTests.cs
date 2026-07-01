using System.IO;
using System.Linq;
using System.Text.Json;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using FdgRaylib.ArmyBuilding;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #153 (P0) — ListCompiler turns a book + selections into the exact playable ArmyListFile the engine consumes.
// Assertions are deterministic against the hand-authored DemoBook: replace-one, add-models (count-scaled),
// replace-all (cost scales with target count), and a rule-granting upgrade.
[TestFixture]
public class ArmyForgeCompilerTests
{
    // Warriors: +1 Plasma (replace one Rifle), +2 Warriors (add-models ×2), +War Banner (Fear).
    // Gunners: replace all 3 Heavy Rifles with Missile Launchers.
    private static BuilderList DemoList() => new()
    {
        Name = "Test List", BookName = "Dark Vanguard", PointsLimit = 500,
        Units =
        {
            new BuilderUnit
            {
                RosterUnitId = "warriors",
                Choices =
                {
                    new UpgradeChoice { SectionId = "warriors-special", OptionId = "plasma", Count = 1 },
                    new UpgradeChoice { SectionId = "warriors-reinforce", OptionId = "add-warrior", Count = 2 },
                    new UpgradeChoice { SectionId = "warriors-banner", OptionId = "war-banner", Count = 1 },
                },
            },
            new BuilderUnit
            {
                RosterUnitId = "gunners",
                Choices = { new UpgradeChoice { SectionId = "gunners-missiles", OptionId = "missile", Count = 1 } },
            },
        },
    };

    private static WeaponFileEntry Wpn(UnitFileEntry u, string name) => u.Weapons.Single(w => w.Name == name);

    [Test]
    public void Compile_Warriors_ReplaceOne_AddModels_And_Upgrade()
    {
        BuiltArmyFile army = ListCompiler.Compile(DemoBook.Build(), DemoList());
        UnitFileEntry warriors = army.Units.Single(u => u.Name == "Vanguard Warriors");

        // add-warrior ×2 → 5 + 2 models; default weapon count tracks it (6 Rifles + 1 Plasma = 7 = models).
        Assert.That(warriors.ModelCount, Is.EqualTo(7));
        Assert.That(Wpn(warriors, "Rifle").Quantity, Is.EqualTo(6));
        Assert.That(Wpn(warriors, "Plasma Rifle").Quantity, Is.EqualTo(1));
        Assert.That(Wpn(warriors, "Plasma Rifle").SpecialRules, Has.One.EqualTo(new SpecialRuleEntry_CoreNumeric("AP", 2)));

        // War Banner granted Fear.
        Assert.That(warriors.SpecialRules, Has.One.EqualTo(new SpecialRuleEntry_Core("Fear")));

        // 65 base + 5 plasma + 13×2 models + 10 banner.
        Assert.That(warriors.PointCost, Is.EqualTo(106));
    }

    [Test]
    public void Compile_Gunners_ReplaceAll_ScalesCostByTargetCount()
    {
        BuiltArmyFile army = ListCompiler.Compile(DemoBook.Build(), DemoList());
        UnitFileEntry gunners = army.Units.Single(u => u.Name == "Heavy Gunners");

        // All 3 Heavy Rifles swapped for 3 Missile Launchers; cost 120 + 15×3.
        Assert.That(gunners.Weapons.Any(w => w.Name == "Heavy Rifle"), Is.False);
        Assert.That(Wpn(gunners, "Missile Launcher").Quantity, Is.EqualTo(3));
        Assert.That(gunners.ModelCount, Is.EqualTo(3));
        Assert.That(gunners.PointCost, Is.EqualTo(165));
    }

    [Test]
    public void Compile_EmbedsSelectionsAndBook_AndCarriesDefs()
    {
        BookFile book = DemoBook.Build();
        BuilderList list = DemoList();
        BuiltArmyFile army = ListCompiler.Compile(book, list);

        Assert.That(army.Name, Is.EqualTo("Test List"));
        Assert.That(army.Units, Has.Count.EqualTo(2));
        Assert.That(army.Selections, Is.SameAs(list));
        Assert.That(army.Book, Is.SameAs(book));
        // Demo references only core rules by name, so no embedded defs are needed (the RuleValidator gate is
        // trivially satisfied). A book with custom rules would carry them here.
        Assert.That(army.RuleDefinitions, Is.Empty);
    }

    [Test]
    public void Compile_ProducesFileThatDeserializesAsPlainArmy_ForTheEngine()
    {
        BuiltArmyFile army = ListCompiler.Compile(DemoBook.Build(), DemoList());

        // Exactly what the builder would write (derived type → embed included).
        string json = JsonSerializer.Serialize(army, RuleJson.Options);

        // Exactly what the lobby "Load Army" / headless ArmyLoader read (base type → embed skipped).
        ArmyListFile asArmy = JsonSerializer.Deserialize<ArmyListFile>(json, RuleJson.Options)!;
        Assert.That(asArmy.Units, Has.Count.EqualTo(2));
        Assert.That(asArmy.Units.Sum(u => u.PointCost), Is.EqualTo(271));

        // Emit the compiled army for the headless play smoke (see the WorkItem notes / verify step).
        string dir = "/tmp/claude-1000/-home-chris-Projects-fdg-raylib/ed75fc9b-7dc5-4e35-b1dd-22374f34b3ec/scratchpad";
        if (Directory.Exists(dir))
            File.WriteAllText(Path.Combine(dir, "demo-compiled.fdgarmy"), json);
    }
}
