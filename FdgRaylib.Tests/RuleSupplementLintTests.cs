using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #166a — the "every rule must fire" lint over the shipped rule supplement, the app-side twin of the
// engine's RuleCatalogLintTests. The supplement is where the Breath Attack no-op (SpecialRulesAudit
// BUG-1) actually lived: hand-authored JSON passes --validate-rules while an ability's operations are
// silently dropped by the stage that offers it. RuleFireLint proves each definition is offered/fires
// and produces stage-executable operations. Allowlist entries carry the reason a rule legitimately
// fails; a stale entry (rule starts passing) fails too.
[TestFixture]
public class RuleSupplementLintTests
{
    private static readonly IReadOnlyDictionary<string, string> Allowlist = new Dictionary<string, string>
    {
        ["Unique"] = "list-building marker: no dispatch entries; enforced at army-build time by " +
            "ListValidator ('the army may only include one copy'), never during play.",
    };

    private static string SupplementPath =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "Books", "GdfRuleSupplement.json");

    private static IEnumerable<TestCaseData> SupplementRules() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(SupplementPath))
            .Select(rule => new TestCaseData(rule).SetArgDisplayNames(rule.Name));

    [TestCaseSource(nameof(SupplementRules))]
    public void EverySupplementRuleFires(SpecialRuleDefinition rule)
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
    public void AllowlistNamesExistInSupplement()
    {
        var supplementNames = BookRuleSupplement.LoadDefinitions(File.ReadAllText(SupplementPath))
            .Select(r => r.Name).ToHashSet();
        var unknown = Allowlist.Keys.Where(name => !supplementNames.Contains(name)).ToList();
        Assert.That(unknown, Is.Empty,
            "Allowlist entries with no matching supplement rule: " + string.Join(", ", unknown));
    }
}
