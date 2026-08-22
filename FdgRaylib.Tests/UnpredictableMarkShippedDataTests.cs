using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #197 Unpredictable Marks (P15 residual) - "pick one enemy unit within 18in; the first friendly unit to
// attack it counts as having Unpredictable Fighter/Shooter." The two definitions were dead no-definition
// names (5 refs) until 2026-07-29: the mark family template covers the placement, but the branch die is
// rolled at ACTION level (UnpredictableBranchResolver) while the mark is only claimed onto the attacker at
// the hit stage - so the resolver had to learn to scan the DEFENDER's marks, or the claimed rule's arms
// would gate themselves out on a branch that never rolled. The mechanism (defender scan + claim + arms
// composing) is pinned engine-side by UnpredictableRuleIntegrationTests; these pin the authored JSON, the
// embedded book copies, and the corpus shape.
[TestFixture]
public class UnpredictableMarkShippedDataTests
{
    private const string FighterMark = "Unpredictable Fighter Mark";
    private const string ShooterMark = "Unpredictable Shooter Mark";

    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static IReadOnlyList<SpecialRuleDefinition> Supplement() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
            Path.Combine(BooksDirectory, "GdfRuleSupplement.json")));

    // ---- The authored definitions ---------------------------------------------------------------------

    [TestCase(FighterMark, "Unpredictable Fighter")]
    [TestCase(ShooterMark, "Unpredictable Shooter")]
    public void Mark_IsAMarkFamilyAbility_GrantingTheCoreRule(string markName, string grantedRule)
    {
        SpecialRuleDefinition rule = Supplement().Single(r => r.Name == markName);
        Assert.That(rule.Passive, Is.Empty, "a mark is an activated ability, not a passive hook");
        ActivatedAbility ability = rule.Activated.Single();

        Assert.That(ability.TriggerHook, Is.EqualTo(EHookID.Activation_OnBeforeAttackAction),
            "'before attacking' - the family's shared trigger");
        Assert.That(ability.Cost, Is.InstanceOf<Cost.OncePerActivation>(), "'once per activation'");
        Assert.That(ability.Effect, Is.InstanceOf<Effect.MarkTarget>());
        Assert.That(((Effect.MarkTarget)ability.Effect).RuleName, Is.EqualTo(grantedRule),
            "the mark grants the CORE rule by exact name - a typo here places a token nothing can claim");
        Assert.That(ability.TargetSelector!.RangeInches, Is.EqualTo(18), "the family's uniform 18in");
        Assert.That(ability.TargetSelector!.RequireLineOfSight, Is.True);

        // The granted name must resolve against the core catalog, where the branch-gated arms live.
        RuleResolver resolver = CoreRuleCatalog.CreateResolver();
        Assert.That(resolver.TryResolve(grantedRule, out ResolvedRule resolved), Is.True,
            $"'{grantedRule}' is a core rule; the mark's grant resolves against the catalog");
        Assert.That(resolved.Definition.Passive, Is.Not.Empty,
            "the granted rule carries the branch-gated arms the claim will fire");
    }

    // ---- The corpus -----------------------------------------------------------------------------------

    private record Site(string Book, string Unit, string Mark);

    private static IEnumerable<Site> Sites()
    {
        foreach (string path in Directory.EnumerateFiles(BooksDirectory, "*" + BookFile.EXTENSION_WITH_PERIOD)
                     .OrderBy(p => p))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            string bookName = Path.GetFileNameWithoutExtension(path);

            foreach (RosterUnit unit in book.Units)
                foreach (string name in RuleNamesOn(unit))
                    if (name is FighterMark or ShooterMark)
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
    public void EveryReference_MatchesTheAuditCensus()
    {
        List<Site> sites = Sites().ToList();

        Assert.That(sites.Count, Is.EqualTo(5),
            "the audit's 5 references - a change here means the corpus moved, not the engine");
        Assert.That(sites.Where(s => s.Mark == FighterMark).Select(s => s.Book).Distinct(),
            Is.EquivalentTo(new[] { "AlienHives" }), "Fighter Mark is Battle Pheromones");
        Assert.That(sites.Where(s => s.Mark == ShooterMark).Select(s => s.Book).Distinct(),
            Is.EquivalentTo(new[] { "GoblinReclaimers" }), "Shooter Mark is Targeted Frenzy");
    }

    [Test]
    public void TheEmbeddedBookCopies_CarryTheAbility()
    {
        // --apply-rules embeds the supplement's definition into each referencing book. A slice that edits
        // the supplement and forgets to re-embed ships a book whose mark is still an unresolvable name.
        foreach (string bookName in Sites().Select(s => s.Book).Distinct())
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(
                File.ReadAllText(Path.Combine(BooksDirectory, bookName + BookFile.EXTENSION_WITH_PERIOD)),
                RuleJson.Options)!;

            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            foreach (SpecialRuleDefinition definition in book.RuleDefinitions)
            {
                resolver.RegisterOrReplace(definition);
            }

            string markName = bookName == "AlienHives" ? FighterMark : ShooterMark;
            Assert.That(resolver.TryResolve(markName, out ResolvedRule resolved), Is.True, bookName);
            Assert.That(resolved.Definition.Activated.Single().Effect, Is.InstanceOf<Effect.MarkTarget>(),
                $"{bookName}: the embedded copy carries the markTarget ability - re-run --apply-rules");
        }
    }
}
