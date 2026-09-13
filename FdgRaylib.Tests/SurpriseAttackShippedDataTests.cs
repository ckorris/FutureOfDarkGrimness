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

// #197 Surprise Attack - OPR, verbatim: "Counts as having Infiltrate. The first time this unit is
// activated, pick one enemy unit within 6" in line of sight, and roll X dice. For each 2+ it takes one hit
// with AP(1)." Dead no-definition (2 refs) until 2026-07-30.
//
// The burst mechanism is pinned engine-side by SurpriseAttackRuleIntegrationTests (a rule-agnostic
// Effect.DealPooledHits at the activation-start hook; nothing in the engine names Surprise Attack). These
// pin the authored JSON - including that the deployment arm really is Infiltrate's and not a lookalike -
// the corpus census, the embedded book copies, and both carriers through the real compiler.
[TestFixture]
public class SurpriseAttackShippedDataTests
{
    private const string RuleName = "Surprise Attack";

    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static BookFile LoadBook(string name) => JsonSerializer.Deserialize<BookFile>(
        File.ReadAllText(Path.Combine(BooksDirectory, name + BookFile.EXTENSION_WITH_PERIOD)),
        RuleJson.Options)!;

    private static SpecialRuleDefinition Supplement(string name = RuleName) =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
                Path.Combine(BooksDirectory, "GdfRuleSupplement.json")))
            .Single(r => r.Name == name);

    // ---- The authored definition ----------------------------------------------------------------------

    [Test]
    public void SurpriseAttack_IsADeploymentArmPlusAFirstActivationBurst()
    {
        SpecialRuleDefinition rule = Supplement();

        Assert.That(rule.Passive.Count, Is.EqualTo(1), "the 'counts as having Infiltrate' arm");
        Assert.That(rule.Activated.Count, Is.EqualTo(1), "and the first-activation burst");
        Assert.That(rule.Scope, Is.EqualTo(ERuleScope.Unit));
        Assert.That(rule.EngineArgumentCount, Is.EqualTo(1),
            "Surprise Attack(X) - the pool size is the rule's argument (3 in HI, 5 in AH)");
    }

    // "Counts as having Infiltrate" is authored as a COPY of Infiltrate's own passive entry rather than a
    // grant: the deployment arm has to be live before deployment, and nothing grants a rule that early.
    // Comparing the whole entry is what keeps the copy honest if Infiltrate is ever retuned.
    [Test]
    public void TheDeploymentArm_IsInfiltratesOwnEntry()
    {
        Assert.That(Supplement().Passive.Single(), Is.EqualTo(Supplement("Infiltrate").Passive.Single()),
            "if Infiltrate changes, this copy has to change with it - that is what 'counts as having' means");
    }

    [Test]
    public void TheBurst_IsAOncePerGamePoolAtActivationStart()
    {
        ActivatedAbility ability = Supplement().Activated.Single();

        Assert.That(ability.TriggerHook, Is.EqualTo(EHookID.Activation_OnActivationStart),
            "'the first time this unit is activated' - fired by SurpriseAttackStage before the action menu");
        Assert.That(ability.Cost, Is.InstanceOf<Cost.OncePerGame>(),
            "the marker is what makes it the FIRST activation and no other");
        Assert.That(ability.AvailableWhen, Is.InstanceOf<Condition.Always>(),
            "the burst is mandatory - nothing gates it but the marker and the target search");

        Assert.That(ability.Effect, Is.InstanceOf<Effect.DealPooledHits>());
        Effect.DealPooledHits pool = (Effect.DealPooledHits)ability.Effect;
        Assert.That(pool.DiceCount, Is.EqualTo(new ValueSource.Arg(0)), "'roll X dice'");
        Assert.That(pool.SuccessThreshold, Is.EqualTo(2), "'for each 2+'");
        Assert.That(pool.ArmorPenetration, Is.EqualTo(1), "'one hit with AP(1)'");
    }

    [Test]
    public void TheSelector_IsOneEnemyWithinSixInchesInLineOfSight()
    {
        TargetSelector selector = Supplement().Activated.Single().TargetSelector;

        Assert.That(selector.RangeInches, Is.EqualTo(6f));
        Assert.That(selector.TargetAffinity, Is.EqualTo(ETargetAffinity.Foe));
        Assert.That(selector.MinCount, Is.EqualTo(1));
        Assert.That(selector.MaxCount, Is.EqualTo(1), "'pick ONE enemy unit' - a single target, unlike Storm");
        Assert.That(selector.RequireLineOfSight, Is.True, "'in line of sight'");
    }

    // ---- The corpus census -----------------------------------------------------------------------------

    private record Site(string Book, string Unit, int MinModels, int MaxModels, int Rating);

    private static IEnumerable<Site> Sites()
    {
        foreach (string path in ShippedBooks.GdfPaths()
                     .OrderBy(p => p))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            string bookName = Path.GetFileNameWithoutExtension(path);

            foreach (RosterUnit unit in book.Units)
            {
                List<SpecialRuleEntry> hits = unit.Rules.Where(Names)
                    .Concat(unit.Items.SelectMany(item => item.Rules.Where(Names)))
                    .Concat(unit.Sections.SelectMany(section => section.Options.SelectMany(option =>
                        option.RulesGained.Where(Names)
                            .Concat(option.ItemsGained.SelectMany(item => item.Rules.Where(Names))))))
                    .ToList();

                foreach (SpecialRuleEntry entry in hits)
                {
                    yield return new Site(bookName, unit.Name, unit.MinModels, unit.MaxModels,
                        ((SpecialRuleEntry_CoreNumeric)entry).NumericValue);
                }
            }
        }
    }

    private static bool Names(SpecialRuleEntry entry) =>
        entry is SpecialRuleEntry_CoreNumeric numeric
        && string.Equals(numeric.Name, RuleName, StringComparison.Ordinal);

    [Test]
    public void BothCarriers_AreSingleModelUnits()
    {
        List<Site> sites = Sites().ToList();

        Assert.That(sites.Count, Is.EqualTo(2), "the audit's 2 references");
        Assert.That(sites.Select(s => (s.Book, s.Unit, s.Rating)), Is.EquivalentTo(new[]
        {
            ("AlienHives", "Hive Burrower", 5),
            ("HumanInquisition", "Espionage Ministry Assassin", 3),
        }));

        foreach (Site site in sites)
        {
            // Unit scope is exact only while every carrier is one model: the source says "this UNIT is
            // activated" but "one hit" per success, and a multi-model carrier would raise the per-model
            // scoping question Sergeant cost a whole slice. Both are 1-model today.
            Assert.That(site.MinModels, Is.EqualTo(1), $"{site.Book}/{site.Unit}");
            Assert.That(site.MaxModels, Is.EqualTo(1), $"{site.Book}/{site.Unit}");
        }
    }

    [Test]
    public void TheEmbeddedBookCopies_CarryBothArms()
    {
        foreach (string book in new[] { "AlienHives", "HumanInquisition" })
        {
            SpecialRuleDefinition embedded = LoadBook(book).RuleDefinitions.Single(d => d.Name == RuleName);

            Assert.That(embedded.Activated.Single().Effect, Is.InstanceOf<Effect.DealPooledHits>(),
                $"{book}: army load reads the BOOK's copy - re-run --apply-rules if this fails");
            Assert.That(embedded.Passive.Single().Effect, Is.InstanceOf<Effect.DeferDeployment>(),
                $"{book}: without the deployment arm the Hive Burrower has no way onto the table at all");
        }
    }

    // ---- End to end: both carriers through the real compiler --------------------------------------------

    [Test]
    public void TheAssassin_ResolvesSurpriseAttackThree_FromItsOwnRules()
    {
        BookFile book = LoadBook("HumanInquisition");
        RosterUnit assassin = book.Units.Single(u => u.Name == "Espionage Ministry Assassin");

        BuiltArmyFile army = ListCompiler.Compile(book, new BuilderList
        {
            Name = "Test", BookName = book.Name, PointsLimit = 500,
            Units = { new BuilderUnit { RosterUnitId = assassin.Id! } },
        });

        Assert.That(army.Units.Single().SpecialRules.Select(r => r.PrintableName),
            Does.Contain("Surprise Attack(3)"), "the rating travels as the rule's argument");
    }

    // The Burrower's copy arrives on an ITEM bought by an upgrade that REPLACES the item granting Ambush -
    // so the deployment arm is not decoration there: without it the unit loses its only route onto the
    // table. This is the site that would break first if the passive were ever dropped.
    [Test]
    public void TheBurrower_TradingDeepDeploymentForBurrowAttack_SwapsAmbushForSurpriseAttack()
    {
        BookFile book = LoadBook("AlienHives");
        RosterUnit burrower = book.Units.Single(u => u.Name == "Hive Burrower");
        UpgradeSection section = burrower.Sections
            .Single(s => s.Options.Any(o => o.Label.Contains("Burrow Attack")));
        UpgradeOption burrow = section.Options.Single(o => o.Label.Contains("Burrow Attack"));

        BuiltArmyFile army = ListCompiler.Compile(book, new BuilderList
        {
            Name = "Test", BookName = book.Name, PointsLimit = 1000,
            Units =
            {
                new BuilderUnit
                {
                    RosterUnitId = burrower.Id!,
                    Choices = { new UpgradeChoice { SectionId = section.Id!, OptionId = burrow.Id!, Count = 1 } },
                },
            },
        });

        List<string> rules = army.Units.Single().SpecialRules.Select(r => r.PrintableName).ToList();
        Assert.That(rules, Does.Contain("Surprise Attack(5)"));
        Assert.That(rules, Does.Not.Contain("Ambush"),
            "the option replaces Deep Deployment, so the Infiltrate arm is the unit's only reserve rule");
    }
}
