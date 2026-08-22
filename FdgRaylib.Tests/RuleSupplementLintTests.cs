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

        // #196 F1. RuleFireLint's synthesized-context search covers "the capability conditions: near/far,
        // moved, melee, charging, all die faces" (its own doc comment) but not a target unit's Tough stat -
        // there is no Tough-majority variant among its synthesized IHasTarget contexts. The condition
        // itself is real and engine-tested (ConditionEvaluationTests.TargetMajorityHasTough_..., .
        // StatGreaterOrEqualTo_Tough_UsesMajoritySemantics), so this is a lint-coverage gap, not a rule
        // defect - StatGreaterOrEqualTo's Defense/Quality arms pass today only because the lint's default
        // synthesized target happens to clear a low threshold, not because Tough is actually covered.
        ["Shatter"] = "AP(+2) vs Tough 3+ (targetMajorityHasTough); lint has no Tough-majority target context.",
        ["Tear"] = "AP(+4) vs Tough 9+ (targetMajorityHasTough); lint has no Tough-majority target context.",
        ["Melee Slayer"] = "AP(+2) in melee vs Tough 3+ (targetMajorityHasTough); lint has no Tough-majority target context.",
        ["Ranged Slayer"] = "AP(+2) at range vs Tough 3+ (targetMajorityHasTough); lint has no Tough-majority target context.",

        // #375 C4. Effect.Teleport deliberately queues no operations: ChooseActionStage routes any offer
        // whose ability effect is Effect.Teleport to the dedicated TeleportStage (routing is by effect
        // TYPE, not rule name - ChooseActionStage.cs "#197 Teleport" branch), which enacts the 6"
        // placement. Same reason core Teleport lives outside the operation pipeline; mechanism proven by
        // TeleportRuleIntegrationTests. Ethereal's movement-penalty entries DO pass the lint - only the
        // teleport ability trips it.
        ["Ethereal"] = "activated teleport enacted by TeleportStage (routed on Effect.Teleport), not by operations.",
    };

    // Every bundled supplement file, each definition linted individually - deliberately NOT the
    // later-wins merge (#375): an AoF redefinition of a shared name must not shadow the GDF entry
    // out of the lint.
    private static IEnumerable<(string Tag, SpecialRuleDefinition Rule)> AllSupplementRules() =>
        FdgRaylib.Import.RuleSupplementSet.BundledFileNames
            .Select(f => Path.Combine(AppContext.BaseDirectory, "Assets", "Books", f))
            .SelectMany(path => BookRuleSupplement.LoadDefinitions(File.ReadAllText(path))
                .Select(rule => (Path.GetFileNameWithoutExtension(path).Replace("RuleSupplement", ""), rule)));

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
