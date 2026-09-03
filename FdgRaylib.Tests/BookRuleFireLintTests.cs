using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Serialization;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// The "every rule must fire" lint (RuleFireLint, #166a), applied to the definitions actually SHIPPED
// inside each bundled .fdgbook - the byte-for-byte copies ListCompiler bakes onto a compiled army
// (BookFile.RuleDefinitions doc: "Copied onto a compiled army so ... rule references resolve").
// RuleSupplementLintTests proves the SOURCE (RuleSupplement JSON + AofBookOverrides) fires; this
// fixture proves the SHIPPED bake did too, closing the staleness gap those source files warn about
// (a supplement edit that isn't re-applied to a book via --apply-rules ships a stale, possibly-dead
// definition that no other test would ever see - BookRuleCensusTests only proves the name resolves,
// not that the resolved definition does anything in play).
[TestFixture]
public class BookRuleFireLintTests
{
    private static readonly IReadOnlyDictionary<string, string> Allowlist = new Dictionary<string, string>
    {
        ["Unique"] = "list-building marker: no dispatch entries; enforced at army-build time by " +
            "ListValidator ('the army may only include one copy'), never during play.",
        ["Ethereal"] = "activated teleport enacted by TeleportStage (routed on Effect.Teleport), not by operations.",
    };

    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static IEnumerable<(string BookTag, SpecialRuleDefinition Rule)> AllBookRules()
    {
        foreach (string path in Directory.EnumerateFiles(BooksDirectory, "*" + BookFile.EXTENSION_WITH_PERIOD)
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            string tag = Path.GetFileNameWithoutExtension(path);
            foreach (SpecialRuleDefinition rule in book.RuleDefinitions)
            {
                yield return (tag, rule);
            }
        }
    }

    private static IEnumerable<TestCaseData> BookRules() =>
        AllBookRules().Select(x => new TestCaseData(x.Rule).SetArgDisplayNames($"{x.BookTag}:{x.Rule.Name}"));

    [TestCaseSource(nameof(BookRules))]
    public void EveryBookRuleFires(SpecialRuleDefinition rule)
    {
        IReadOnlyList<string> problems = RuleFireLint.Check(rule);

        if (Allowlist.TryGetValue(rule.Name, out string? reason))
        {
            Assert.That(problems, Is.Not.Empty,
                $"'{rule.Name}' is allowlisted ({reason}) but now passes the fire-lint - " +
                "remove its stale allowlist entry.");
            return;
        }

        Assert.That(problems, Is.Empty,
            $"'{rule.Name}' fails the fire-lint:{Environment.NewLine}  " +
            string.Join($"{Environment.NewLine}  ", problems));
    }

    [Test]
    public void AllowlistNamesExistInSomeBook()
    {
        var bookNames = AllBookRules().Select(x => x.Rule.Name).ToHashSet();
        var unknown = Allowlist.Keys.Where(name => !bookNames.Contains(name)).ToList();
        Assert.That(unknown, Is.Empty,
            "Allowlist entries with no matching book rule: " + string.Join(", ", unknown));
    }
}
