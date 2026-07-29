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

// #197 P7 - the No Retreat family (9 refs: Aura 5, base 3, Buff 1). The mechanism is pinned engine-side
// by NoRetreatRuleIntegrationTests; these pin the authored JSON and its agreement with the corpus.
//
// The numbers are the point. "for each result of 1-3" is a band, not a threshold, and it is the only place
// the rule's cost lives - authoring a 1 instead of a 3 turns a brutal last stand into a free rescue, and
// every structural check still passes. Same for the condition: AllModelsHaveThisRule is one word away from
// MostModelsHaveThisRule and would quietly stop the rule firing for any unit with a joined hero.
[TestFixture]
public class NoRetreatShippedDataTests
{
    private const string RuleName = "No Retreat";
    private const string AuraName = "No Retreat Aura";
    private const string BuffName = "No Retreat Buff";

    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static IReadOnlyList<SpecialRuleDefinition> Supplement() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
            Path.Combine(BooksDirectory, "GdfRuleSupplement.json")));

    private static SpecialRuleDefinition Definition(string name) =>
        Supplement().Single(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

    [Test]
    public void NoRetreat_ConvertsAFailedMoraleTest_AtTheCorpusPrice()
    {
        HookEntry entry = Definition(RuleName).Passive.Single();

        Assert.That(entry.HookID, Is.EqualTo(EHookID.Morale_OnMoraleTestComplete),
            "the hook MoraleUtilities fires once a test has failed");
        Assert.That(entry.Seat, Is.EqualTo(ERuleSeat.Actor), "the unit taking the test is the actor");
        Assert.That(entry.Condition, Is.InstanceOf<Condition.MostModelsHaveThisRule>(),
            "'a unit where MOST models have this rule' - not all, and not any");
        Assert.That(entry.Effect, Is.EqualTo(new Effect.PassFailedMoraleTest(SelfWoundOnRollAtMost: 3)),
            "'for each result of 1-3 the unit takes one wound'");
    }

    [Test]
    public void TheAura_ConfersTheBaseRule()
    {
        Assert.That(Definition(AuraName).Passive.Single().Effect,
            Is.EqualTo(new Effect.Aura(RuleName)),
            "5 of the 9 references are the Aura - a broken link makes the rule unreachable for them");
    }

    [Test]
    public void TheBuff_GrantsTheBaseRuleOnce_ToAFriendWithin12()
    {
        ActivatedAbility ability = Definition(BuffName).Activated.Single();

        Assert.That(ability.TriggerHook, Is.EqualTo(EHookID.Activation_OnBeforeAttackAction));
        Assert.That(ability.Cost, Is.InstanceOf<Cost.OncePerActivation>(), "'once per activation'");
        Assert.That(ability.TargetSelector!.TargetAffinity, Is.EqualTo(ETargetAffinity.Friend));
        Assert.That(ability.TargetSelector.RangeInches, Is.EqualTo(12f));
        Assert.That(ability.TargetSelector.RequireLineOfSight, Is.False,
            "the friendly-buff family does not say 'in line of sight'");
        Assert.That(ability.Effect, Is.EqualTo(new Effect.AddRule(RuleName, ELifetime.NextTrigger)),
            "'gets No Retreat once (next time the effect would apply)'");
    }

    // ---- The corpus, book by book -------------------------------------------------------------------

    private record Site(string Book, string Unit, string Rule);

    private static IEnumerable<Site> Sites()
    {
        string[] tracked = { RuleName, AuraName, BuffName };

        foreach (string path in Directory.EnumerateFiles(BooksDirectory, "*" + BookFile.EXTENSION_WITH_PERIOD)
                     .OrderBy(p => p))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            string bookName = Path.GetFileNameWithoutExtension(path);

            foreach (RosterUnit unit in book.Units)
                foreach (string name in RuleNamesOn(unit))
                    if (tracked.Contains(name, StringComparer.OrdinalIgnoreCase))
                        yield return new Site(bookName, unit.Name, name);
        }
    }

    private static IEnumerable<string> RuleNamesOn(RosterUnit unit)
    {
        foreach (SpecialRuleEntry rule in unit.Rules) yield return NameOf(rule);
        foreach (ItemEntry item in unit.Items)
            foreach (SpecialRuleEntry rule in item.Rules) yield return NameOf(rule);

        foreach (UpgradeSection section in unit.Sections)
            foreach (UpgradeOption option in section.Options)
            {
                foreach (SpecialRuleEntry rule in option.RulesGained) yield return NameOf(rule);
                foreach (ItemEntry item in option.ItemsGained)
                    foreach (SpecialRuleEntry rule in item.Rules) yield return NameOf(rule);
            }
    }

    private static string NameOf(SpecialRuleEntry rule) =>
        ArmyListRuleResolution.DescribeRuleEntry(rule).lookupName;

    [Test]
    public void EveryReference_ResolvesAgainstItsOwnBook_AlongWithWhatItGrants()
    {
        List<Site> sites = Sites().ToList();

        Assert.That(sites.Count(s => s.Rule == AuraName), Is.EqualTo(5));
        Assert.That(sites.Count(s => s.Rule == RuleName), Is.EqualTo(3));
        Assert.That(sites.Count(s => s.Rule == BuffName), Is.EqualTo(1),
            "the audit's 9 No Retreat references - a change here means the corpus moved, not the engine");

        var problems = new List<string>();
        foreach (string bookName in sites.Select(s => s.Book).Distinct())
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(
                File.ReadAllText(Path.Combine(BooksDirectory, bookName + BookFile.EXTENSION_WITH_PERIOD)),
                RuleJson.Options)!;

            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            foreach (SpecialRuleDefinition definition in book.RuleDefinitions)
                resolver.RegisterOrReplace(definition);

            foreach (Site site in sites.Where(s => s.Book == bookName))
            {
                if (!resolver.TryResolve(site.Rule, out ResolvedRule resolved))
                {
                    problems.Add($"{bookName}: {site.Unit} - '{site.Rule}' has no definition");
                    continue;
                }

                // A wrapper is only worth anything if the rule it hands out is embedded too.
                if (site.Rule == RuleName) continue;
                if (!resolver.TryResolve(RuleName, out ResolvedRule target)
                    || target.Definition.Passive.Count == 0)
                {
                    problems.Add($"{bookName}: '{site.Rule}' grants '{RuleName}', which its book does not carry");
                }
            }
        }

        Assert.That(problems, Is.Empty, string.Join("; ", problems));
    }
}
