using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #428 - OPR 3.6 rewrote every "a unit where all models have this rule" gate as "where MOST models". The
// supplement sources (every bundled RuleSupplement file plus the AofBookOverrides) were swept off the
// pre-3.6 "allModelsHaveThisRule" kind, which still PARSES - old army files embed it - but now means the
// most-models gate. These pin the sweep: new source data never uses the ambiguous legacy kind (the
// authoring validator refuses it, but only --validate-rules runs that over the shipped files), and the
// strict gate stays on exactly the two rules whose 3.6 text keeps "if all models in this unit have this
// rule": Inquisitorial Agent (supplement) and Martial Prowess (core catalog).
[TestFixture]
public class MostModelsGateShippedDataTests
{
    [Test]
    public void NoSupplementDefinition_UsesTheLegacyAllModelsKind()
    {
        List<string> legacy = AllSupplementRules()
            .Where(x => Conditions(x.Rule).Any(c => c is Condition.LegacyAllModelsHaveThisRule))
            .Select(x => $"{x.Tag}:{x.Rule.Name}")
            .ToList();

        Assert.That(legacy, Is.Empty,
            "write 'mostModelsHaveThisRule' (or 'everyModelHasThisRule' where the 3.6 text still says 'all')");
    }

    [Test]
    public void OnlyInquisitorialAgent_KeepsTheStrictGate_InTheSupplements()
    {
        List<string> strict = AllSupplementRules()
            .Where(x => Conditions(x.Rule).Any(c => c is Condition.EveryModelHasThisRule))
            .Select(x => x.Rule.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.That(strict, Is.EquivalentTo(new[] { "Inquisitorial Agent" }));
    }

    [Test]
    public void OnlyMartialProwess_KeepsTheStrictGate_InTheCoreCatalog()
    {
        List<string> strict = CoreRuleCatalog.All
            .Where(r => Conditions(r).Any(c => c is Condition.EveryModelHasThisRule))
            .Select(r => r.Name)
            .ToList();

        Assert.That(strict, Is.EquivalentTo(new[] { "Martial Prowess" }));
    }

    [Test]
    public void TheSweptSupplements_StillValidate()
    {
        // The same check --validate-rules runs (the authoring validator included), over the bundled files as
        // one later-wins merged universe, the way that command treats several files.
        string books = Path.Combine(AppContext.BaseDirectory, "Assets", "Books");
        List<SpecialRuleDefinition> merged = FdgRaylib.Import.RuleSupplementSet.LoadMerged(
            FdgRaylib.Import.RuleSupplementSet.BundledFileNames.Select(f => Path.Combine(books, f)));

        Assert.That(BookRuleSupplement.ValidateAll(merged), Is.Empty);
    }

    // Every condition tree a definition carries (passive conditions and ability availability), flattened
    // through And/Or/Not.
    private static IEnumerable<Condition> Conditions(SpecialRuleDefinition rule) =>
        rule.Passive.Select(e => e.Condition)
            .Concat(rule.Activated.Select(a => a.AvailableWhen))
            .SelectMany(Flatten);

    private static IEnumerable<Condition> Flatten(Condition condition)
    {
        yield return condition;
        IEnumerable<Condition> children = condition switch
        {
            Condition.And and => new[] { and.Left, and.Right },
            Condition.Or or => new[] { or.Left, or.Right },
            Condition.Not not => new[] { not.Inner },
            _ => Array.Empty<Condition>(),
        };
        foreach (Condition child in children.SelectMany(Flatten))
        {
            yield return child;
        }
    }

    // Mirrors RuleSupplementLintTests: every bundled supplement file plus every per-book override file,
    // each definition individually (not the later-wins merge).
    private static IEnumerable<(string Tag, SpecialRuleDefinition Rule)> AllSupplementRules()
    {
        string books = Path.Combine(AppContext.BaseDirectory, "Assets", "Books");
        IEnumerable<(string, string)> files = FdgRaylib.Import.RuleSupplementSet.BundledFileNames
            .Select(f => (Path.Combine(books, f), Path.GetFileNameWithoutExtension(f)));
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
}
