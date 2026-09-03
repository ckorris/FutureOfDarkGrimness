using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Rules.Serialization;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #383 - the shipped per-model section stamp (22 sections across 19 GDF books, classified against the
// live army-books API 2026-08-22 and transferred by --import-section-shapes). OPR's "Any model may
// replace/take ..." sections spend one pick per MODEL, repeatable across models; the identically-
// selected "Upgrade with any" subset sections do not. The bundled snapshots can't re-derive the
// distinction (the raw `select`/`model` fields aren't stored), so these pin the stamp itself: lose it
// in a book edit and the section silently reverts to one-per-option checkboxes.
[TestFixture]
public class PerModelSectionShippedDataTests
{
    private static IEnumerable<(string Book, RosterUnit Unit, UpgradeSection Section)> AllSections()
    {
        string booksDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Books");
        foreach (string path in Directory.GetFiles(booksDir, "*" + BookFile.EXTENSION_WITH_PERIOD))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            foreach (RosterUnit unit in book.Units)
                foreach (UpgradeSection section in unit.Sections)
                    yield return (Path.GetFileName(path), unit, section);
        }
    }

    [Test]
    public void EveryAnyModelSection_IsACountedPerModelStepper()
    {
        var anyModel = AllSections()
            .Where(s => s.Section.Label.StartsWith("Any model may ", StringComparison.Ordinal))
            .ToList();

        Assert.That(anyModel, Has.Count.EqualTo(22), "the 2026-08-22 census of the live API");
        foreach ((string bookName, RosterUnit unit, UpgradeSection section) in anyModel)
        {
            Assert.That(section.PerModelBudget, Is.True, $"{bookName} / {unit.Name} / {section.Label}");
            Assert.That(section.Affects, Is.EqualTo(UpgradeAffects.Any),
                $"{bookName} / {unit.Name} / {section.Label}: a counted stepper, charged per application");
        }
    }

    // The other direction: nothing else may carry the budget - "Upgrade with any" and its all-models /
    // one-model variants are option SUBSETS (each option once), and stamping one would let a tank take
    // every option N times.
    [Test]
    public void NoOtherSection_CarriesThePerModelBudget()
    {
        var strays = AllSections()
            .Where(s => s.Section.PerModelBudget
                && !s.Section.Label.StartsWith("Any model may ", StringComparison.Ordinal))
            .Select(s => $"{s.Book} / {s.Unit.Name} / {s.Section.Label}")
            .ToList();

        Assert.That(strays, Is.Empty);
    }

    // The two reported units, by name - the census total above can absorb a swap; these cannot.
    [TestCase("AlienHives", "Hive Warriors", "Any model may replace one Razor Claws")]
    [TestCase("RobotLegions", "Robot Snakes", "Any model may replace Spike Whips")]
    [TestCase("RobotLegions", "Robot Snakes", "Any model may replace one Metal Fangs")]
    public void TheReportedSections_AreStamped(string bookFile, string unitName, string label)
    {
        BookFile book = JsonSerializer.Deserialize<BookFile>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "Books",
                bookFile + BookFile.EXTENSION_WITH_PERIOD)), RuleJson.Options)!;
        UpgradeSection section = book.Units.Single(u => u.Name == unitName)
            .Sections.Single(s => s.Label == label);

        Assert.That(section.PerModelBudget, Is.True);
        Assert.That(section.IsCounted, Is.True);
    }
}
