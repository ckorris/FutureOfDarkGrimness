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

// #197 Vengeance - "when this unit is destroyed, place a marker on the unit that destroyed it; friendly
// units get +1 to hit against that unit for the rest of the game." Dead no-definition (3 refs, the Eternal
// Dynasty "Honor-Bound" item) until 2026-07-30.
//
// The placement primitive is pinned engine-side by VengeanceRuleIntegrationTests (Effect.GrantTokenToKiller
// over IHasKillerUnit); the read side is P14b's PersistentHitBonusMarker, pinned by TargetBonusMarkerTests.
// These pin the authored JSON, the corpus census, the embedded book copy, and a real-book compile.
//
// The census test is load-bearing rather than decorative: the marker count is authored as a LITERAL 1
// (owner-signed 2026-07-30), and what makes that exact is that every carrier buys the rule through an
// Affects.One upgrade section - one Honor-Bound item per unit, hence one model with the rule. If a book
// update ever puts Vengeance on a section that applies more than once, that count needs re-deciding.
[TestFixture]
public class VengeanceShippedDataTests
{
    private const string RuleName = "Vengeance";
    private const string Book = "EternalDynasty";
    private const string ItemName = "Honor-Bound";

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
    public void Vengeance_MarksItsKiller_FromTheSubjectSeatOfTheDestructionHook()
    {
        SpecialRuleDefinition rule = Supplement();

        Assert.That(rule.Activated, Is.Empty);
        HookEntry entry = rule.Passive.Single();

        Assert.That(entry.HookID, Is.EqualTo(EHookID.Shooting_OnUnitDestroyed),
            "the KILLER's hook - the one destruction seam that has an attributable killer to mark");
        Assert.That(entry.Seat, Is.EqualTo(ERuleSeat.Subject),
            "the bearer is the unit that DIED, not the one that killed; on the Actor seat this rule " +
            "would mark whoever the bearer itself destroyed, which is Piercing Frenzy's mechanic");
        Assert.That(entry.Condition, Is.InstanceOf<Condition.Always>());
        Assert.That(entry.Effect, Is.InstanceOf<Effect.GrantTokenToKiller>(),
            "plain grantToken would land the marker on the corpse");

        Assert.That(rule.Scope, Is.EqualTo(ERuleScope.Unit),
            "ListCompiler folds an item's rules onto the unit, so unit scope is where it lands");
        Assert.That(rule.EngineArgumentCount, Is.EqualTo(0));
    }

    [Test]
    public void TheMarker_IsOnePersistentHitBonus_ThatOutlivesThePlacer()
    {
        var effect = (Effect.GrantTokenToKiller)Supplement().Passive.Single().Effect;

        Assert.That(effect.TType, Is.EqualTo(TokenType.PersistentHitBonus),
            "'+1 to hit for the rest of the game' - persistent, not the spendable sibling that a single " +
            "attack claims and removes");
        Assert.That(effect.Count, Is.InstanceOf<ValueSource.Literal>());
        Assert.That(((ValueSource.Literal)effect.Count).Value, Is.EqualTo(1),
            "one Honor-Bound item per unit - see EveryCarrier_BuysItThroughAnAffectsOneSection");
        Assert.That(effect.Clear, Is.InstanceOf<TokenClearTrigger.ManualOnly>(),
            "an OwnerDestroyed marker would be self-cancelling: the placer is dead by construction");
        Assert.That(effect.MaxTotal, Is.EqualTo(0),
            "the source states no cap, unlike the Frenzy family's 'max 2' - two avenged units stack");
    }

    // ---- The corpus census -----------------------------------------------------------------------------

    private record Site(string Book, string Unit, UpgradeAffects Affects, int MaxApplications, int MaxPicks);

    private static IEnumerable<Site> Sites()
    {
        foreach (string path in Directory.EnumerateFiles(BooksDirectory, "*" + BookFile.EXTENSION_WITH_PERIOD)
                     .OrderBy(p => p))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            string bookName = Path.GetFileNameWithoutExtension(path);

            foreach (RosterUnit unit in book.Units)
            {
                // Unit-level and weapon-level references would need different handling; the census asserts
                // there are none, so they are gathered as a section with no bounds rather than skipped.
                if (unit.Rules.Any(Names) || unit.Weapons.Any(w => w.SpecialRules.Any(Names)))
                {
                    yield return new Site(bookName, unit.Name, UpgradeAffects.All, 0, 0);
                }

                foreach (UpgradeSection section in unit.Sections)
                {
                    bool hit = section.Options.Any(option =>
                        option.RulesGained.Any(Names)
                        || option.ItemsGained.Any(item => item.Rules.Any(Names)));

                    if (hit)
                    {
                        yield return new Site(bookName, unit.Name, section.Affects,
                            section.MaxApplications, section.MaxPicks);
                    }
                }
            }
        }
    }

    private static bool Names(SpecialRuleEntry entry) =>
        entry is SpecialRuleEntry_Core core && string.Equals(core.Name, RuleName, StringComparison.Ordinal);

    [Test]
    public void EveryCarrier_BuysItThroughAnAffectsOneSection()
    {
        List<Site> sites = Sites().ToList();

        Assert.That(sites.Count, Is.EqualTo(3), "the audit's 3 references");
        Assert.That(sites.Select(s => s.Book).Distinct(), Is.EquivalentTo(new[] { Book }));
        Assert.That(sites.Select(s => s.Unit),
            Is.EquivalentTo(new[] { "Warriors", "Royal Guard", "ONIs" }));

        foreach (Site site in sites)
        {
            // This is what makes ValueSource.Literal(1) exact rather than a guess: ListCompiler's
            // Applications() caps an Affects.One section at a single application, so the unit can hold
            // exactly one Honor-Bound item, i.e. exactly one model with the rule.
            Assert.That(site.Affects, Is.EqualTo(UpgradeAffects.One), $"{site.Book}/{site.Unit}");
            Assert.That(site.MaxApplications, Is.EqualTo(0), $"{site.Book}/{site.Unit}");
            Assert.That(site.MaxPicks, Is.EqualTo(1), $"{site.Book}/{site.Unit}");
        }
    }

    [Test]
    public void TheEmbeddedBookCopy_CarriesTheKillerGrant()
    {
        SpecialRuleDefinition embedded = LoadBook(Book).RuleDefinitions.Single(d => d.Name == RuleName);
        HookEntry entry = embedded.Passive.Single();

        Assert.That(entry.Effect, Is.InstanceOf<Effect.GrantTokenToKiller>(),
            "army load reads the BOOK's copy - re-run --apply-rules if this fails");
        Assert.That(entry.HookID, Is.EqualTo(EHookID.Shooting_OnUnitDestroyed));
        Assert.That(entry.Seat, Is.EqualTo(ERuleSeat.Subject),
            "the seat has to travel with the embedded copy too - on Actor the rule silently inverts");
    }

    // ---- End to end: a real book unit through the real compiler ----------------------------------------

    [Test]
    public void Warriors_BuyingHonorBound_ResolveVengeanceOnTheUnit()
    {
        BookFile book = LoadBook(Book);
        RosterUnit warriors = book.Units.Single(u => u.Name == "Warriors");
        UpgradeSection section = warriors.Sections
            .Single(s => s.Options.Any(o => o.Label.Contains(ItemName)));
        UpgradeOption honorBound = section.Options.Single(o => o.Label.Contains(ItemName));

        BuiltArmyFile army = ListCompiler.Compile(book, new BuilderList
        {
            Name = "Test", BookName = book.Name, PointsLimit = 500,
            Units =
            {
                new BuilderUnit
                {
                    RosterUnitId = warriors.Id!,
                    Choices = { new UpgradeChoice { SectionId = section.Id!, OptionId = honorBound.Id!, Count = 1 } },
                },
            },
        });

        UnitFileEntry compiled = army.Units.Single();
        Assert.That(compiled.SpecialRules.Select(r => r.PrintableName), Does.Contain(RuleName),
            "the item's rule flattens onto the unit");
        Assert.That(compiled.ModelCount, Is.GreaterThan(1),
            "a MULTI-model unit holding a unit-scoped rule is exactly why the count is a literal and not " +
            "ValueSource.RuleCarrierCount, which would credit every model");
    }
}
