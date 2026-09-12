using System.IO;
using System.Linq;
using System.Text.Json;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using FdgRaylib;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #372: the lobby's lightweight index of the armies folder. The point of the streaming reader is that it
// agrees with ArmyListFile.TotalPoints WITHOUT deserializing the army, so the tests below check exactly
// that equivalence - on hand-built files for the edge cases, and on the shipped armies for the real thing.
[TestFixture]
public class ArmyCatalogTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp() => _root = Directory.CreateTempSubdirectory(nameof(ArmyCatalogTests)).FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_root, recursive: true);

    private string WriteArmy(string fileName, ArmyListFile army)
    {
        string path = Path.Combine(_root, fileName + ArmyListFile.EXTENSION_WITH_PERIOD);
        File.WriteAllText(path, JsonSerializer.Serialize(army, RuleJson.Options));
        return path;
    }

    private static ArmyListFile Army(string name, string faction, params int[] unitCosts)
    {
        var army = new ArmyListFile { Name = name, Faction = faction };
        foreach (int cost in unitCosts)
            army.Units.Add(new UnitFileEntry { Name = "Unit", PointCost = cost });
        return army;
    }

    [Test]
    public void ReadsNameFactionAndSummedUnitCosts()
    {
        string path = WriteArmy("a", Army("Grumpy Bugs", "Alien Hives", 300, 250, 45));

        ArmyCatalogEntry entry = ArmyCatalog.ReadEntry(path)!.Value;
        Assert.Multiple(() =>
        {
            Assert.That(entry.Name, Is.EqualTo("Grumpy Bugs"));
            Assert.That(entry.Faction, Is.EqualTo("Alien Hives"));
            Assert.That(entry.Points, Is.EqualTo(595));
            Assert.That(entry.Path, Is.EqualTo(path));
        });
    }

    // #241/#219: an imported Army Forge list carries upgrade points that belong to no single unit. They
    // count toward TotalPoints, so the index has to add them too or every imported army reads light.
    [Test]
    public void UnattributedPointsCountTowardTheTotal()
    {
        ArmyListFile army = Army("Imported", "Orc Marauders", 500);
        army.UnattributedPoints = 120;
        string path = WriteArmy("b", army);

        Assert.That(ArmyCatalog.ReadEntry(path)!.Value.Points, Is.EqualTo(army.TotalPoints));
        Assert.That(ArmyCatalog.ReadEntry(path)!.Value.Points, Is.EqualTo(620));
    }

    // A unit-less army is well-formed JSON but nothing a bot could play, so it stays out of the index
    // rather than showing up as a 0-point "closest to a 0-point limit" pick.
    [Test]
    public void AnArmyWithNoUnitsIsNotIndexed()
    {
        string path = WriteArmy("empty", new ArmyListFile { Name = "Nothing", Faction = "None" });
        Assert.That(ArmyCatalog.ReadEntry(path), Is.Null);
    }

    // A half-written or hand-edited file in the folder must not take the lobby down.
    [Test]
    public void UnparseableFilesAreSkippedRatherThanThrowing()
    {
        string path = Path.Combine(_root, "broken" + ArmyListFile.EXTENSION_WITH_PERIOD);
        File.WriteAllText(path, "{ \"name\": \"truncated\", \"units\": [ {");
        Assert.That(ArmyCatalog.ReadEntry(path), Is.Null);
    }

    [Test]
    public void MissingFileIsSkipped() =>
        Assert.That(ArmyCatalog.ReadEntry(Path.Combine(_root, "nope.fdgarmy")), Is.Null);

    [Test]
    public void ScanIndexesEveryArmyInTheFolderAndIgnoresOtherFiles()
    {
        WriteArmy("one", Army("One", "F", 100));
        WriteArmy("two", Army("Two", "F", 200));
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "not an army");

        var catalog = new ArmyCatalog(_root);
        Assert.That(catalog.Entries.Select(e => e.Name), Is.EquivalentTo(new[] { "One", "Two" }));
    }

    [Test]
    public void AMissingFolderYieldsAnEmptyCatalogRatherThanThrowing()
    {
        Assert.That(new ArmyCatalog(Path.Combine(_root, "absent")).Entries, Is.Empty);
        Assert.That(new ArmyCatalog(null).Entries, Is.Empty);
    }

    private static string? FindRepoArmiesFolder()
    {
        for (DirectoryInfo? dir = new(TestContext.CurrentContext.TestDirectory);
             dir is not null;
             dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, ArmyPaths.DirectoryName);
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    // The equivalence that matters, against the real shipped lists rather than a synthetic file: the
    // streaming reader must agree with a full deserialize + ArmyListFile.TotalPoints on every one of
    // them. These carry an embedded book and Forge selections, which is exactly what the reader skips.
    // It also pins the game-system slug: every shipped list must index the system it actually records,
    // since that is what the lobby's Army Source filter (#400) judges it by.
    [Test]
    public void IndexedPointsMatchAFullDeserializeOfEveryShippedArmy()
    {
        // Walk up from the test binary rather than using ArmyPaths.FolderPath: the test host's base
        // directory is FdgRaylib.Tests/bin/..., several levels below the repo's armies folder, so the
        // app's two-candidate search finds nothing and this test would silently never run.
        string? folder = FindRepoArmiesFolder();
        if (folder is null) Assert.Ignore("Could not locate the repo's armies folder from the test binary.");

        // Recursive since #400: the shipped lists live in armies/GDF and armies/AoF, so a flat
        // GetFiles here finds nothing at all and this test stops covering anything.
        string[] files = Directory.GetFiles(
            folder!, "*" + ArmyListFile.EXTENSION_WITH_PERIOD, SearchOption.AllDirectories);
        Assert.That(files, Is.Not.Empty, "the armies folder should not be empty");

        foreach (string file in files)
        {
            var full = JsonSerializer.Deserialize<ArmyListFile>(File.ReadAllText(file), RuleJson.Options);
            ArmyCatalogEntry? indexed = ArmyCatalog.ReadEntry(file);

            Assert.That(indexed, Is.Not.Null, $"{Path.GetFileName(file)} failed to index");
            Assert.Multiple(() =>
            {
                Assert.That(indexed!.Value.Points, Is.EqualTo(full!.TotalPoints), Path.GetFileName(file));
                Assert.That(indexed.Value.Name, Is.EqualTo(full.Name), Path.GetFileName(file));
                Assert.That(indexed.Value.Faction, Is.EqualTo(full.Faction), Path.GetFileName(file));
                Assert.That(indexed.Value.GameSystem, Is.EqualTo(full.GameSystem), Path.GetFileName(file));
            });
        }
    }

    // ── #400: subfolders and the game-system slug ────────────────────────────────────────────

    [Test]
    public void ScanRecursesIntoSubfolders()
    {
        // The shipped layout splits armies/ into GDF/ and AoF/; a flat scan would index neither.
        WriteArmy("top", Army("Top", "F", 100));
        Directory.CreateDirectory(Path.Combine(_root, "GDF"));
        Directory.CreateDirectory(Path.Combine(_root, "AoF", "deeper"));
        WriteArmy(Path.Combine("GDF", "gdf"), Army("In GDF", "F", 200));
        WriteArmy(Path.Combine("AoF", "deeper", "aof"), Army("Nested", "F", 300));

        string[] names = new ArmyCatalog(_root).Entries.Select(e => e.Name).OrderBy(n => n).ToArray();

        Assert.That(names, Is.EqualTo(new[] { "In GDF", "Nested", "Top" }));
    }

    [Test]
    public void ReadsTheGameSystemSlug()
    {
        ArmyListFile aof = Army("Elves", "High Elves", 500);
        aof.GameSystem = FDG.ArmyBuilding.GameSystems.AgeOfFantasy;
        string path = WriteArmy("aof", aof);

        Assert.That(ArmyCatalog.ReadEntry(path)!.Value.GameSystem,
            Is.EqualTo(FDG.ArmyBuilding.GameSystems.AgeOfFantasy));
    }

    // Absent means Grimdark Future (#378), and the field is omitted on save, so every pre-#378 file
    // reaches the picker as null rather than as an empty string or a guess.
    [Test]
    public void AnArmyWithNoGameSystemFieldReadsAsNull()
    {
        string path = WriteArmy("plain", Army("Bugs", "Alien Hives", 500));

        Assert.That(ArmyCatalog.ReadEntry(path)!.Value.GameSystem, Is.Null);
    }

    // The folder an army sits in is decoration: the slug comes from the file, so misfiling a list
    // cannot smuggle it past the lobby's Army Source filter.
    [Test]
    public void FolderNameDoesNotDecideTheSystem()
    {
        Directory.CreateDirectory(Path.Combine(_root, "GDF"));
        ArmyListFile aof = Army("Misfiled", "High Elves", 500);
        aof.GameSystem = FDG.ArmyBuilding.GameSystems.AgeOfFantasy;
        WriteArmy(Path.Combine("GDF", "misfiled"), aof);

        Assert.That(new ArmyCatalog(_root).Entries.Single().GameSystem,
            Is.EqualTo(FDG.ArmyBuilding.GameSystems.AgeOfFantasy));
    }
}
