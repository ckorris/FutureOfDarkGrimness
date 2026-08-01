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

// #197 P12 Regenerative Strength - "Place one marker on this model when it ignores a wound. When in melee,
// pick one of its weapons to get +X attacks, where X is the number of markers on it." The last dead
// no-definition name in the corpus (2 refs), deferred 2026-07-22 and shipped 2026-07-31.
//
// The mechanic is pinned engine-side by RegenerativeStrengthRuleIntegrationTests. These pin the authored
// JSON, the corpus census, the embedded book copies, and a real-book compile.
//
// The census is load-bearing, not decorative. Two facts about the corpus justify simplifications the
// engine side made, and both must break loudly if a book update invalidates them:
//   1. Both carriers are SINGLE-MODEL units, so "place a marker on this MODEL" and "on this unit" are the
//      same statement. The marker is granted to the unit (Effect.GrantIgnoredWoundMarker lands on
//      EffectiveTarget, which is the bearer), and model-level attribution - which the wound-ignore fold
//      genuinely cannot answer, since it ignores wounds from a unit-wide pool BEFORE allocation - is never
//      needed. A multi-model carrier would make that question real.
//   2. Both carriers own a wound-ignore rule (Regeneration / Resistance). Without one the rule is inert:
//      it can never place a marker, because nothing it does ever ignores a wound.
[TestFixture]
public class RegenerativeStrengthShippedDataTests
{
    private const string RuleName = "Regenerative Strength";
    private const string MarkerTokenId = "RegenerativeStrengthMarker";

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
    public void RegenerativeStrength_CountsIgnoredWounds_FromTheSubjectSeat()
    {
        SpecialRuleDefinition rule = Supplement();

        Assert.That(rule.Activated, Is.Empty);
        HookEntry entry = rule.Passive.Single();

        Assert.That(entry.HookID, Is.EqualTo(EHookID.Lifecycle_OnWoundIgnored),
            "the hook was declared since #042 but had no context and no fire site until this slice - a " +
            "rule authored here used to validate, lint clean and never fire");
        Assert.That(entry.Seat, Is.EqualTo(ERuleSeat.Subject),
            "the bearer is the unit being ATTACKED - it is the one shrugging off the wound; on the Actor " +
            "seat the rule would count wounds the bearer's own target ignored");
        Assert.That(entry.Condition, Is.InstanceOf<Condition.Always>());
        Assert.That(entry.Effect, Is.InstanceOf<Effect.GrantIgnoredWoundMarker>(),
            "plain grantToken cannot express the count: ValueSource.Resolve returns an int, and the " +
            "number of wounds ignored is fractional under the probabilistic roller");

        Assert.That(rule.Scope, Is.EqualTo(ERuleScope.Unit),
            "ListCompiler folds an item's rules onto the unit, so unit scope is where it lands");
        Assert.That(rule.EngineArgumentCount, Is.EqualTo(0));
    }

    [Test]
    public void TheMarker_IsAManualOnlyAccumulator_OfItsOwnTokenType()
    {
        var effect = (Effect.GrantIgnoredWoundMarker)Supplement().Passive.Single().Effect;

        Assert.That(effect.TType.Id, Is.EqualTo(MarkerTokenId),
            "its own type, not the shared AccumulatorTokens pool that Spell Accumulator lends from");
        Assert.That(effect.TType, Is.EqualTo(TokenType.RegenerativeStrengthMarker),
            "and the engine constant the read side looks up must be the same type the data grants");
        Assert.That(effect.Clear, Is.InstanceOf<TokenClearTrigger.ManualOnly>(),
            "markers are never spent and last the whole game - a RoundEnd sweep would erase them nightly");
    }

    // ---- The corpus census -----------------------------------------------------------------------------

    private record Site(string Book, string Unit, int MinModels, int MaxModels, IReadOnlyList<string> UnitRules);

    private static IEnumerable<Site> Sites()
    {
        foreach (string path in Directory.EnumerateFiles(BooksDirectory, "*" + BookFile.EXTENSION_WITH_PERIOD)
                     .OrderBy(p => p))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            string bookName = Path.GetFileNameWithoutExtension(path);

            foreach (RosterUnit unit in book.Units)
            {
                bool hit = unit.Rules.Any(Names)
                    || unit.Weapons.Any(w => w.SpecialRules.Any(Names))
                    || unit.Sections.Any(section => section.Options.Any(option =>
                        option.RulesGained.Any(Names)
                        || option.ItemsGained.Any(item => item.Rules.Any(Names))));

                if (hit)
                {
                    yield return new Site(bookName, unit.Name, unit.MinModels, unit.MaxModels,
                        unit.Rules.OfType<SpecialRuleEntry_Core>().Select(r => r.Name)
                            .Concat(unit.Rules.OfType<SpecialRuleEntry_CoreNumeric>().Select(r => r.Name))
                            .ToList());
                }
            }
        }
    }

    private static bool Names(SpecialRuleEntry entry) =>
        entry is SpecialRuleEntry_Core core && string.Equals(core.Name, RuleName, StringComparison.Ordinal);

    [Test]
    public void BothCarriers_AreSingleModelUnits()
    {
        // The simplification this pins: markers land on the UNIT, and with one model per unit that is
        // exactly what "place one marker on this model" means. A multi-model carrier would need
        // per-model attribution the wound-ignore fold cannot supply - it ignores wounds from a unit-wide
        // pool before allocation decides which model takes them.
        List<Site> sites = Sites().ToList();

        Assert.That(sites.Count, Is.EqualTo(2), "the audit's 2 references");
        Assert.That(sites.Select(s => s.Unit),
            Is.EquivalentTo(new[] { "Engine of Suffering", "Psycho-Rex" }));
        Assert.That(sites.Select(s => s.Book),
            Is.EquivalentTo(new[] { "DarkElfRaiders", "AlienHives" }));

        foreach (Site site in sites)
        {
            Assert.That(site.MinModels, Is.EqualTo(1), $"{site.Book}/{site.Unit}");
            Assert.That(site.MaxModels, Is.EqualTo(1),
                $"{site.Book}/{site.Unit} can never hold two models, so the unit IS the model. A book " +
                "update raising this makes model-level marker attribution a real question again.");
        }
    }

    [Test]
    public void BothCarriers_OwnAWoundIgnoreRule_OrTheMarkerCanNeverBePlaced()
    {
        // Standing lesson 1 in census form: the rule resolving is not the rule working. Regenerative
        // Strength triggers on ignoring a wound, so a carrier with no wound-ignore source would carry a
        // rule that is structurally live and practically inert.
        string[] woundIgnoreRules = { "Regeneration", "Resistance" };

        foreach (Site site in Sites())
        {
            Assert.That(site.UnitRules.Intersect(woundIgnoreRules), Is.Not.Empty,
                $"{site.Book}/{site.Unit} carries Regenerative Strength but nothing that ignores wounds - " +
                $"it has: {string.Join(", ", site.UnitRules)}");
        }
    }

    [Test]
    public void TheEmbeddedBookCopies_CarryTheIgnoredWoundGrant()
    {
        // Army load reads the BOOK's copy, not the supplement - re-run --apply-rules if this fails.
        foreach (string book in new[] { "DarkElfRaiders", "AlienHives" })
        {
            SpecialRuleDefinition embedded = LoadBook(book).RuleDefinitions.Single(d => d.Name == RuleName);
            HookEntry entry = embedded.Passive.Single();

            Assert.That(entry.Effect, Is.InstanceOf<Effect.GrantIgnoredWoundMarker>(), book);
            Assert.That(entry.HookID, Is.EqualTo(EHookID.Lifecycle_OnWoundIgnored), book);
            Assert.That(entry.Seat, Is.EqualTo(ERuleSeat.Subject),
                $"{book}: the seat has to travel with the embedded copy too");
        }
    }

    // ---- End to end: a real book unit through the real compiler ----------------------------------------

    [Test]
    public void EngineOfSuffering_BuyingPainFueled_ResolvesTheRuleAlongsideRegeneration()
    {
        BookFile book = LoadBook("DarkElfRaiders");
        RosterUnit engine = book.Units.Single(u => u.Name == "Engine of Suffering");
        UpgradeSection section = engine.Sections
            .Single(s => s.Options.Any(o => o.Label.Contains("Pain Fueled")));
        UpgradeOption painFueled = section.Options.Single(o => o.Label.Contains("Pain Fueled"));

        BuiltArmyFile army = ListCompiler.Compile(book, new BuilderList
        {
            Name = "Test", BookName = book.Name, PointsLimit = 500,
            Units =
            {
                new BuilderUnit
                {
                    RosterUnitId = engine.Id!,
                    Choices = { new UpgradeChoice { SectionId = section.Id!, OptionId = painFueled.Id!, Count = 1 } },
                },
            },
        });

        UnitFileEntry compiled = army.Units.Single();
        List<string> rules = compiled.SpecialRules.Select(r => r.PrintableName).ToList();

        Assert.That(rules, Does.Contain(RuleName), "the item's rule flattens onto the unit");
        Assert.That(rules, Does.Contain("Regeneration"),
            "and the wound-ignore source it feeds off rides along - the pair is what makes it work in play");
        Assert.That(compiled.ModelCount, Is.EqualTo(1),
            "one model, which is why a unit-scoped marker is a faithful reading of 'on this model'");
    }
}
