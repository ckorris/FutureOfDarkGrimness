using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #377 — the regression guard for spell rule references, the blind spot the #196 census had until this
// item: --rule-coverage and BookRuleScopeTests walked unit/weapon/upgrade references only, so 21 spell
// references across 13 shipped GDF books resolved NOWHERE and their spells cast as silent no-ops.
// Mirrors army load exactly through the shared ResolveOrDescribeDrop ladder: a damage spell's WithRules
// names parse their arguments and resolve at Weapon scope (ArmyListSpellResolution); granted names
// (addRule / markTarget / aura, and the moraleTestThen on-fail arm) resolve RAW and argument-less,
// because RuleEvaluator.CollectGrantedRules does exactly that and screens out argument-reading
// definitions - grants carry no arguments.
//
// Without this, the next spell re-stamp or catalog edit silently reintroduces the bug: a spell that
// grants an unresolvable name looks exactly like a spell that works.
[TestFixture]
public class BookSpellCoverageTests
{
    // Known-dead spell references, each with a reason. Empty means every spell reference in every
    // bundled book resolves; a stale entry (the name starts resolving) fails the fixture too.
    // Still empty after #378 bundled the AoF books: their spells' references all resolve (the known
    // #381 Retreating Strike gap is unit-attached in Dark Elves, pinned by BookRuleCensusTests).
    private static readonly IReadOnlyDictionary<string, string> Allowlist = new Dictionary<string, string>();

    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static IEnumerable<TestCaseData> Books() =>
        Directory.EnumerateFiles(BooksDirectory, "*" + BookFile.EXTENSION_WITH_PERIOD)
            .OrderBy(path => path)
            .Select(path => new TestCaseData(path).SetArgDisplayNames(Path.GetFileNameWithoutExtension(path)));

    [TestCaseSource(nameof(Books))]
    public void EverySpellRuleReferenceResolves(string bookPath)
    {
        BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(bookPath), RuleJson.Options)!;
        RuleResolver resolver = CoreRuleCatalog.CreateResolver();
        foreach (SpecialRuleDefinition definition in book.RuleDefinitions)
        {
            resolver.RegisterOrReplace(definition);
        }

        List<string> dead = new();

        foreach (SpellDefinition spell in book.Spells)
        {
            foreach (string ruleName in SpellRuleReferences.WeaponRuleNames(spell.Effect))
            {
                SpecialRuleEntry entry = SpecialRuleEntryParser.Parse(ruleName);
                ArmyListRuleResolution.ResolveOrDescribeDrop(resolver, entry, ERuleScope.Weapon,
                    $"spell '{spell.Name}'", out RuleDrop? drop);
                Report(dead, drop);
            }

            foreach (string ruleName in SpellRuleReferences.GrantedRuleNames(spell.Effect))
            {
                ArmyListRuleResolution.ResolveOrDescribeDrop(resolver,
                    new SpecialRuleEntry_Core(ruleName), attachmentScope: null,
                    $"spell '{spell.Name}'", out RuleDrop? drop);
                Report(dead, drop);
            }
        }

        Assert.That(dead, Is.Empty,
            $"{Path.GetFileName(bookPath)} has spell references that will do nothing in play:" +
            $"{Environment.NewLine}  " + string.Join(Environment.NewLine + "  ", dead));
    }

    private static void Report(List<string> dead, RuleDrop? drop)
    {
        if (drop is { } d && !Allowlist.ContainsKey(d.RuleName))
        {
            dead.Add($"{d.Owner}: '{d.RuleName}' ({d.Reason}).");
        }
    }

    [Test]
    public void AllowlistedNamesAreStillDeadSomewhere()
    {
        if (Allowlist.Count == 0)
        {
            Assert.Pass("empty allowlist - nothing to check stale.");
        }

        var stillDead = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (TestCaseData bookCase in Books())
        {
            string bookPath = (string)bookCase.Arguments[0]!;
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(bookPath), RuleJson.Options)!;
            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            foreach (SpecialRuleDefinition definition in book.RuleDefinitions)
            {
                resolver.RegisterOrReplace(definition);
            }

            foreach (SpellDefinition spell in book.Spells)
            {
                foreach (string name in SpellRuleReferences.GrantedRuleNames(spell.Effect)
                             .Concat(SpellRuleReferences.WeaponRuleNames(spell.Effect)))
                {
                    SpecialRuleEntry entry = SpecialRuleEntryParser.Parse(name);
                    ArmyListRuleResolution.ResolveOrDescribeDrop(resolver, entry, attachmentScope: null,
                        "allowlist check", out RuleDrop? drop);
                    if (drop is { } d)
                    {
                        stillDead.Add(d.RuleName);
                    }
                }
            }
        }

        foreach ((string name, string reason) in Allowlist)
        {
            Assert.That(stillDead, Does.Contain(name),
                $"allowlisted '{name}' ({reason}) now resolves everywhere - remove its stale entry.");
        }
    }
}
