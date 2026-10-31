using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #441 - the Army Forge, its hover glossary and its import summary against the SHIPPED 3.6 books. Everything here
// drives the ImGui-free seams (the screen's test-seam ctor, RuleTextFlow, RuleGlossary, ListValidator); the panes
// themselves are hand-verified (checklist in WorkItems/441-army-forge-ui-3-6.md).
[TestFixture]
public class ArmyForge36Tests
{
    private static BookFile Book(string fileName) =>
        JsonSerializer.Deserialize<BookFile>(
            File.ReadAllText(Path.Combine(ShippedBooks.Directory, fileName + BookFile.EXTENSION_WITH_PERIOD)),
            RuleJson.Options)!;

    private static List<BookFile> AllBooks() =>
        Directory.EnumerateFiles(ShippedBooks.Directory, "*" + BookFile.EXTENSION_WITH_PERIOD)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => JsonSerializer.Deserialize<BookFile>(File.ReadAllText(p), RuleJson.Options)!)
            .ToList();

    // ── Goal 2: the book roster ─────────────────────────────────────────────────────────────────────

    // The Forge's dropdown is the shipped library, filtered per system (#378). 3.6 renamed five books (OPR's
    // "Wormhole Daemons of X" / "Rift Daemons of X" -> "Daemons of X", "Saurian Starhost" -> "Saurian Starhosts"),
    // added Voidspace Stalkers, and lists 0-unit parent entries ("Wormhole Daemons", "Rift Daemons") the importer
    // never turns into books - so the dropdown must show 48 + 40 books, every one under its 3.6 name.
    [Test]
    public void TheRoster_Lists48GdfAnd40AofBooks_UnderTheir36Names()
    {
        var screen = new ArmyForgeScreen(AllBooks());

        Assert.That(screen.SystemIndex, Is.EqualTo(0), "Grimdark Future first");
        IReadOnlyList<string> gdf = screen.VisibleBookNames.ToList();
        screen.SwitchSystem(1);
        IReadOnlyList<string> aof = screen.VisibleBookNames.ToList();

        Assert.Multiple(() =>
        {
            Assert.That(gdf, Has.Count.EqualTo(48));
            Assert.That(aof, Has.Count.EqualTo(40));
            Assert.That(gdf, Is.Unique);
            Assert.That(aof, Is.Unique);
            Assert.That(gdf, Does.Contain("Voidspace Stalkers"), "new in 3.6");
            Assert.That(gdf, Does.Contain("Saurian Starhosts"));
            Assert.That(gdf, Is.SupersetOf(new[] { "Daemons of Change", "Daemons of Lust", "Daemons of Plague", "Daemons of War" }));
            Assert.That(aof, Is.SupersetOf(new[] { "Daemons of Change", "Daemons of Lust", "Daemons of Plague", "Daemons of War" }));
            foreach (string name in gdf.Concat(aof))
            {
                Assert.That(name, Does.Not.StartWith("Wormhole Daemons").And.Not.StartWith("Rift Daemons"),
                    "no 3.5 name and no 0-unit parent entry");
                Assert.That(name, Is.Not.EqualTo("Saurian Starhost"));
            }
        });
    }

    [Test]
    public void EveryShippedBook_IsStamped36_AndCarriesItsSystem()
    {
        foreach (BookFile book in AllBooks())
        {
            Assert.That(book.Version, Is.EqualTo("OPR v3.6.0"), book.Name);
            Assert.That(GameSystems.Normalize(book.GameSystem),
                Is.EqualTo(GameSystems.GrimdarkFuture).Or.EqualTo(GameSystems.AgeOfFantasy), book.Name);
            Assert.That(book.Units, Is.Not.Empty, $"{book.Name}: a parent entry is not a book");
        }
    }

    // A loaded or imported list lands on its book in the dropdown even where the name exists in both systems
    // (3.6 gave GDF and AoF a "Daemons of War" each).
    [Test]
    public void AdoptLoaded_ADaemonsList_SelectsTheBookOfItsOwnSystem()
    {
        var screen = new ArmyForgeScreen(AllBooks());
        BookFile aofWar = Book("AoF-DaemonsofWar");
        BuiltArmyFile saved = new()
        {
            Book = aofWar,
            Selections = new BuilderList { BookName = aofWar.Name, PointsLimit = 750 },
        };

        Assert.That(screen.AdoptLoaded(saved), Is.True);
        Assert.That(screen.SystemIndex, Is.EqualTo(1), "Age of Fantasy");
        Assert.That(screen.VisibleBookNames, Has.Count.EqualTo(40));
        Assert.That(GameSystems.Normalize(screen.CurrentBook.GameSystem), Is.EqualTo(GameSystems.AgeOfFantasy));
    }
    // ── Goal 1: an Assault weapon on the card ───────────────────────────────────────────────────────

    // The shipped 3.6 Assault Brothers carry a Heavy Pistol (9", A1, AP(1), Assault) and a Hand Weapon. The Forge
    // prints the pistol ONCE, as the ranged line it is, with Assault a hoverable rule whose tooltip is the 3.6
    // sentence - never as a second melee line (#424). Its melee use is the rule's, so the card adds nothing else.
    [Test]
    public void AssaultBrothers_HeavyPistol_PrintsOnce_WithItsRange_AndAHoverableAssault()
    {
        BookFile book = Book("BattleBrothers");
        RosterUnit roster = book.Units.Single(u => u.Name == "Assault Brothers");
        var screen = new ArmyForgeScreen(book);
        screen.AddToList(roster.Id);

        UnitFileEntry unit = screen.Compile().Units.Single();
        List<string> lines = unit.Weapons.Select(w => RuleTextFlow.Flatten(RuleTextFlow.WeaponLine(w))).ToList();

        Assert.That(lines, Is.EqualTo(new[] { "5x Heavy Pistol (9\", A1, AP(1), Assault)", "5x Hand Weapon (A1)" }),
            "one line per weapon - the pistol is not repeated as a melee weapon");
        RuleTextFlow.RuleSegment assault = RuleTextFlow.WeaponLine(unit.Weapons[0]).Single(s => s.IsRule);
        Assert.That(assault.Text, Is.EqualTo("Assault"));
        Assert.That(RuleGlossary.Build(book).Tooltip(assault.Rule!),
            Is.EqualTo("Assault\nCounts as both a ranged weapon and a melee weapon."),
            "the hover is the 3.6 sentence from the book's embedded definition, underlined as documented");
    }

    // ── Goal 8 / #438 D3: the stepper with the pass-through combo bought ────────────────────────────

    // 3.6 Assault Brothers: "Replace any Heavy Pistol" sits ABOVE "Replace one Heavy Pistol and Hand Weapon -> Heavy
    // Pistol, Energy Hammer". The combo hands a pistol back, so the any-swap still reaches all five pistols (the
    // compiler yields, #438 D3) - the Forge stepper has to offer all five too, and the compile has to land both.
    [Test]
    public void AssaultBrothers_WithTheHammerComboBought_TheAnySwapStepperOffersAllFive()
    {
        BookFile book = Book("BattleBrothers");
        RosterUnit roster = book.Units.Single(u => u.Name == "Assault Brothers");
        UpgradeSection any = roster.Sections.Single(s => s.Label == "Replace any Heavy Pistol");
        UpgradeOption plasma = any.Options.Single();
        UpgradeSection combo = roster.Sections.First(s => s.Label == "Replace one Heavy Pistol and Hand Weapon");
        UpgradeOption hammer = combo.Options.Single(o => o.Label.Contains("Energy Hammer"));

        var screen = new ArmyForgeScreen(book);
        screen.AddToList(roster.Id);
        BuilderUnit bu = screen.List.Units[0];
        ArmyForgeScreen.SetChoice(bu, combo, hammer.Id, 1);

        Assert.That(StepperMaxFor(book, bu, any, plasma), Is.EqualTo(5), "the combo returns its pistol");

        ArmyForgeScreen.SetChoice(bu, any, plasma.Id, 5);
        Assert.That(StepperMaxFor(book, bu, any, plasma), Is.EqualTo(5), "still five with all five taken");

        UnitFileEntry compiled = screen.Compile().Units.Single();
        Assert.That(compiled.Weapons.Single(w => w.Name == "Plasma Pistol").Quantity, Is.EqualTo(5));
        Assert.That(compiled.Weapons.Single(w => w.Name == "Energy Hammer").Quantity, Is.EqualTo(1));
        Assert.That(compiled.Weapons.Any(w => w.Name == "Heavy Pistol"), Is.False);
        Assert.That(compiled.PointCost, Is.EqualTo(roster.BasePointCost + hammer.Cost + 5 * plasma.Cost));
    }

    // The same purchase in the other order: five Plasma Pistols first, then the combo. The compiler lands both
    // whichever order the clicks came in; the Forge must not gray the combo out just because the any-swap holds
    // every pistol.
    [Test]
    public void AssaultBrothers_WithAllFivePistolsSwapped_TheHammerComboIsStillOffered()
    {
        BookFile book = Book("BattleBrothers");
        RosterUnit roster = book.Units.Single(u => u.Name == "Assault Brothers");
        UpgradeSection any = roster.Sections.Single(s => s.Label == "Replace any Heavy Pistol");
        UpgradeSection combo = roster.Sections.First(s => s.Label == "Replace one Heavy Pistol and Hand Weapon");
        UpgradeOption hammer = combo.Options.Single(o => o.Label.Contains("Energy Hammer"));

        var screen = new ArmyForgeScreen(book);
        screen.AddToList(roster.Id);
        BuilderUnit bu = screen.List.Units[0];
        ArmyForgeScreen.SetChoice(bu, any, any.Options.Single().Id, 5);

        UpgradeOption chainSword = combo.Options.Single(o => o.Label.StartsWith("Great Chain-Sword"));
        Assert.That(ArmyForgeScreen.AvailableExcludingSection(book, bu, combo), Is.Zero,
            "no pistol is left for an ordinary pick");
        Assert.That(ArmyForgeScreen.PassThroughPool(book, bu, roster, combo, hammer, excludeOwn: true), Is.GreaterThan(0),
            "but the hammer combo hands its pistol back, so its radio stays live (the swap above yields to it)");
        Assert.That(ArmyForgeScreen.PassThroughPool(book, bu, roster, combo, chainSword, excludeOwn: true), Is.Zero,
            "the chain-sword consumes the pistol for good - grayed out, as the compiler would drop it");

        ArmyForgeScreen.SetChoice(bu, combo, hammer.Id, 1);
        UnitFileEntry compiled = screen.Compile().Units.Single();
        Assert.That(compiled.Weapons.Single(w => w.Name == "Plasma Pistol").Quantity, Is.EqualTo(5));
        Assert.That(compiled.Weapons.Single(w => w.Name == "Energy Hammer").Quantity, Is.EqualTo(1));
    }

    private static int StepperMaxFor(BookFile book, BuilderUnit bu, UpgradeSection section, UpgradeOption option)
    {
        (UnitFileEntry unit, var items) = ListCompiler.CompileUnitDetailed(book, bu);
        RosterUnit roster = book.Units.Single(u => u.Id == bu.RosterUnitId);
        int own = ArmyForgeScreen.ChoiceCount(bu, section.Id, option.Id);
        int others = section.Options.Sum(o => ArmyForgeScreen.ChoiceCount(bu, section.Id, o.Id)) - own;
        int available = ArmyForgeScreen.YieldingAllSwapChosen(bu, roster, section)
            ? ArmyForgeScreen.ReplacePool(book, bu, roster, section, excludeOwn: false)
            : ListCompiler.AvailableApplications(unit.Weapons, items, section.Targets);
        return ArmyForgeScreen.StepperMax(section, roster, unit, available, own, others);
    }
}
