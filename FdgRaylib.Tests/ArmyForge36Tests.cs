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
}
