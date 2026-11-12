using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FDG.Rules.Definitions;
using FdgRaylib.Import;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #375 — later-wins merge semantics for multi-file supplement loads (AoF books bake against GDF + AoF,
// and an AoF redefinition of a shared name must be the one that embeds).
[TestFixture]
public class RuleSupplementSetTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "fdg-supplement-set-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_dir, recursive: true);

    private string WriteFile(string name, string json)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, json);
        return path;
    }

    private static string Def(string name, string description) =>
        $"{{ \"name\": \"{name}\", \"scope\": \"Unit\", \"description\": \"{description}\" }}";

    [Test]
    public void LaterFileWinsOnNameCollision_CaseInsensitive_NewNamesAppend()
    {
        string first = WriteFile("first.json", $"[ {Def("Alpha", "base alpha")}, {Def("Beta", "base beta")} ]");
        string second = WriteFile("second.json", $"[ {Def("ALPHA", "override alpha")}, {Def("Gamma", "new gamma")} ]");

        List<SpecialRuleDefinition> merged = RuleSupplementSet.LoadMerged(new[] { first, second });

        Assert.That(merged.Select(d => d.Name), Is.EqualTo(new[] { "ALPHA", "Beta", "Gamma" }),
            "the override replaces in place; new names append in file order");
        Assert.That(merged[0].Description, Is.EqualTo("override alpha"));
        Assert.That(merged[1].Description, Is.EqualTo("base beta"));
    }

    [Test]
    public void SingleFileLoadsUnchanged()
    {
        string only = WriteFile("only.json", $"[ {Def("Alpha", "a")}, {Def("Beta", "b")} ]");

        List<SpecialRuleDefinition> merged = RuleSupplementSet.LoadMerged(new[] { only });

        Assert.That(merged.Select(d => d.Name), Is.EqualTo(new[] { "Alpha", "Beta" }));
    }

    [Test]
    public void BundledFilesExistAndParse()
    {
        foreach (string fileName in RuleSupplementSet.BundledFileNames)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Assets", "Books", fileName);
            Assert.That(File.Exists(path), $"bundled supplement '{fileName}' is missing from Assets/Books");
            Assert.DoesNotThrow(() => RuleSupplementSet.LoadMerged(new[] { path }),
                $"bundled supplement '{fileName}' does not strict-parse");
        }
    }
}
