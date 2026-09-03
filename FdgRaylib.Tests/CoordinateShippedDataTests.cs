using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #197 P19 Coordinate - OPR FPlO2MymiMc0: "At the end of this unit's activation, another friendly unit
// within 12\" that hasn't activated yet may be activated immediately. May not be used if this unit was
// activated via Coordinate." Dead no-definition (3 refs, the HDF "General" item) until 2026-07-30.
//
// The mechanism is a rule-agnostic turn-order primitive pinned engine-side by
// ActivateUnitNextRuleIntegrationTests; nothing in the engine names Coordinate. These pin the authored
// JSON, the corpus census, the embedded book copy, and a real-book compile.
[TestFixture]
public class CoordinateShippedDataTests
{
    private const string RuleName = "Coordinate";
    private const string Book = "HumanDefenseForce";

    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static BookFile LoadBook(string name) => JsonSerializer.Deserialize<BookFile>(
        File.ReadAllText(Path.Combine(BooksDirectory, name + BookFile.EXTENSION_WITH_PERIOD)),
        RuleJson.Options)!;

    private static SpecialRuleDefinition Supplement() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
                Path.Combine(BooksDirectory, "GdfRuleSupplement.json")))
            .Single(r => r.Name == RuleName);

    // ---- The authored definition ----------------------------------------------------------------------

    [Test]
    public void Coordinate_IsAnEndOfActivationGrantOfTheNextActivation()
    {
        SpecialRuleDefinition rule = Supplement();

        Assert.That(rule.Passive, Is.Empty);
        ActivatedAbility ability = rule.Activated.Single();

        Assert.That(ability.TriggerHook, Is.EqualTo(EHookID.Activation_OnEndOfActivation),
            "'at the end of this unit's activation' - the only hook ReconcileEndOfActivationStage offers at");
        Assert.That(ability.Effect, Is.InstanceOf<Effect.ActivateUnitNext>(),
            "the turn-order primitive, not Effect.Reactivate - a different mechanic with a similar name");

        // No usage cost: the rule may be used every activation. Its only limit is the anti-chain clause.
        Assert.That(ability.Cost, Is.InstanceOf<Cost.Free>(),
            "the source states no per-game or per-round limit, so inventing one would nerf the rule");

        Assert.That(rule.Scope, Is.EqualTo(ERuleScope.Unit));
        Assert.That(rule.EngineArgumentCount, Is.EqualTo(0));
    }

    [Test]
    public void TheSelector_IsOneFriendlyUnitWithinTwelveInches()
    {
        TargetSelector selector = Supplement().Activated.Single().TargetSelector;

        Assert.That(selector.RangeInches, Is.EqualTo(12f));
        Assert.That(selector.TargetAffinity, Is.EqualTo(ETargetAffinity.Friend),
            "'another FRIENDLY unit' - which includes a teammate's, whose owner then controls it");
        Assert.That(selector.MinCount, Is.EqualTo(1));
        Assert.That(selector.MaxCount, Is.EqualTo(1), "'ANOTHER friendly unit', singular");
        Assert.That(selector.RequireLineOfSight, Is.False, "the source asks for range only");
    }

    // "May not be used if this unit was activated via Coordinate." Authored as a condition over the marker
    // the engine stamps when it grants an out-of-order activation, so the anti-chain is data, not code -
    // and so any future rule using the same primitive gets to decide the question for itself.
    [Test]
    public void TheAntiChainClause_IsAConditionOverTheOutOfOrderMarker()
    {
        Condition condition = Supplement().Activated.Single().AvailableWhen;

        Assert.That(condition, Is.InstanceOf<Condition.Not>());
        Condition inner = ((Condition.Not)condition).Inner;

        Assert.That(inner, Is.InstanceOf<Condition.TokenPresent>());
        Assert.That(((Condition.TokenPresent)inner).TType, Is.EqualTo(TokenType.ActivatedOutOfOrder),
            "without this a line of Generals within 12in of each other activates the whole army in one go");
    }

    // ---- The corpus census -----------------------------------------------------------------------------

    private record Site(string Book, string Unit, int MinModels, int MaxModels);

    private static IEnumerable<Site> Sites()
    {
        foreach (string path in ShippedBooks.GdfPaths()
                     .OrderBy(p => p))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            string bookName = Path.GetFileNameWithoutExtension(path);

            foreach (RosterUnit unit in book.Units)
            {
                bool hit = unit.Rules.Any(Names)
                    || unit.Sections.Any(section => section.Options.Any(option =>
                        option.RulesGained.Any(Names)
                        || option.ItemsGained.Any(item => item.Rules.Any(Names))));

                if (hit)
                {
                    yield return new Site(bookName, unit.Name, unit.MinModels, unit.MaxModels);
                }
            }
        }
    }

    private static bool Names(SpecialRuleEntry entry) =>
        entry is SpecialRuleEntry_Core core && string.Equals(core.Name, RuleName, StringComparison.Ordinal);

    [Test]
    public void EveryCarrier_IsASingleModelHeroInOneBook()
    {
        List<Site> sites = Sites().ToList();

        Assert.That(sites.Count, Is.EqualTo(3), "the audit's 3 references - all the HDF 'General' item");
        Assert.That(sites.Select(s => s.Book).Distinct(), Is.EquivalentTo(new[] { Book }));

        foreach (Site site in sites)
        {
            // Unit scope is exact only while every carrier is one model. A multi-model carrier would make
            // "this unit's activation" and "this model's" diverge and the scoping would need re-deciding.
            Assert.That(site.MinModels, Is.EqualTo(1), $"{site.Book}/{site.Unit}");
            Assert.That(site.MaxModels, Is.EqualTo(1), $"{site.Book}/{site.Unit}");
        }
    }

    [Test]
    public void TheEmbeddedBookCopy_CarriesTheEffectAndTheAntiChainCondition()
    {
        SpecialRuleDefinition embedded = LoadBook(Book).RuleDefinitions.Single(d => d.Name == RuleName);
        ActivatedAbility ability = embedded.Activated.Single();

        Assert.That(ability.Effect, Is.InstanceOf<Effect.ActivateUnitNext>(),
            "army load reads the BOOK's copy - re-run --apply-rules if this fails");
        Assert.That(ability.TriggerHook, Is.EqualTo(EHookID.Activation_OnEndOfActivation));
        Assert.That(ability.AvailableWhen, Is.InstanceOf<Condition.Not>(),
            "the anti-chain clause has to travel with the embedded copy too");
    }

    // ---- End to end: a real book unit through the real compiler ----------------------------------------

    [Test]
    public void CompanyLeader_BuyingAGeneral_ResolvesCoordinateOnTheUnit()
    {
        BookFile book = LoadBook(Book);
        RosterUnit leader = book.Units.Single(u => u.Name == "Company Leader");
        UpgradeSection section = leader.Sections
            .Single(s => s.Options.Any(o => o.Label.Contains("General")));
        UpgradeOption general = section.Options.Single(o => o.Label.Contains("General"));

        BuiltArmyFile army = ListCompiler.Compile(book, new BuilderList
        {
            Name = "Test", BookName = book.Name, PointsLimit = 500,
            Units =
            {
                new BuilderUnit
                {
                    RosterUnitId = leader.Id!,
                    Choices = { new UpgradeChoice { SectionId = section.Id!, OptionId = general.Id!, Count = 1 } },
                },
            },
        });

        Assert.That(army.Units.Single().SpecialRules.Select(r => r.PrintableName), Does.Contain(RuleName),
            "the item's rule flattens onto the unit, which is exact for a 1-model hero");
    }
}
