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

        // The #196 F1 Tough-majority entries (Shatter, Tear, Melee Slayer, Ranged Slayer) left with
        // #377: RuleFireLint now synthesizes Tough(9)-majority defender variants at
        // Shooting_OnHitRollComplete, so targetMajorityHasTough-gated rules prove fireable for real.

        // #375 C4. Effect.Teleport deliberately queues no operations: ChooseActionStage routes any offer
        // whose ability effect is Effect.Teleport to the dedicated TeleportStage (routing is by effect
        // TYPE, not rule name - ChooseActionStage.cs "#197 Teleport" branch), which enacts the 6"
        // placement. Same reason core Teleport lives outside the operation pipeline; mechanism proven by
        // TeleportRuleIntegrationTests. Ethereal's movement-penalty entries DO pass the lint - only the
        // teleport ability trips it.
        ["Ethereal"] = "activated teleport enacted by TeleportStage (routed on Effect.Teleport), not by operations.",
    };

    // Every bundled supplement file plus every per-book override file (#375 C9: AofBookOverrides/,
    // baked as a book's LAST supplement), each definition linted individually - deliberately NOT the
    // later-wins merge: an AoF or per-book redefinition of a shared name must not shadow the other
    // versions out of the lint.
    private static IEnumerable<(string Tag, SpecialRuleDefinition Rule)> AllSupplementRules()
    {
        string books = Path.Combine(AppContext.BaseDirectory, "Assets", "Books");
        IEnumerable<(string, string)> files = FdgRaylib.Import.RuleSupplementSet.BundledFileNames
            .Select(f => (Path.Combine(books, f), Path.GetFileNameWithoutExtension(f).Replace("RuleSupplement", "")));
        string overridesDir = Path.Combine(books, "AofBookOverrides");
        if (Directory.Exists(overridesDir))
        {
            files = files.Concat(Directory.EnumerateFiles(overridesDir, "*.json")
                .OrderBy(p => p, StringComparer.Ordinal)
                .Select(p => (p, "Ovr " + Path.GetFileNameWithoutExtension(p))));
        }
        return files.SelectMany(x => BookRuleSupplement.LoadDefinitions(File.ReadAllText(x.Item1))
            .Select(rule => (x.Item2, rule)));
    }

    private static IEnumerable<TestCaseData> SupplementRules() =>
        AllSupplementRules()
            .Select(x => new TestCaseData(x.Rule).SetArgDisplayNames($"{x.Tag}:{x.Rule.Name}"));

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
        var supplementNames = AllSupplementRules().Select(x => x.Rule.Name).ToHashSet();
        var unknown = Allowlist.Keys.Where(name => !supplementNames.Contains(name)).ToList();
        Assert.That(unknown, Is.Empty,
            "Allowlist entries with no matching supplement rule: " + string.Join(", ", unknown));
    }
}
