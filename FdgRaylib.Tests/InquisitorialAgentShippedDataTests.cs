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

// #197 Inquisitorial Agent - 20 references, all in Human Inquisition, and the single largest name left in
// the audit. The mechanism is pinned engine-side by InquisitorialAgentRuleIntegrationTests; these pin the
// authored JSON, and one structural assumption the engine leans on.
[TestFixture]
public class InquisitorialAgentShippedDataTests
{
    private const string RuleName = "Inquisitorial Agent";

    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static IReadOnlyList<SpecialRuleDefinition> Supplement() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
            Path.Combine(BooksDirectory, "GdfRuleSupplement.json")));

    private static SpecialRuleDefinition Definition() =>
        Supplement().Single(r => string.Equals(r.Name, RuleName, StringComparison.OrdinalIgnoreCase));

    [Test]
    public void ItIsAOncePerGameSelfReactivation_WithBothRiders()
    {
        ActivatedAbility ability = Definition().Activated.Single();

        Assert.That(ability.TriggerHook, Is.EqualTo(EHookID.Activation_OnNextActivatorRequested),
            "the hook DeterminePlayerTurnStage fires before the player picks their next unit");
        Assert.That(ability.Cost, Is.InstanceOf<Cost.OncePerGame>(), "'once per game'");
        Assert.That(ability.TargetSelector!.TargetAffinity, Is.EqualTo(ETargetAffinity.Self));
        Assert.That(ability.AvailableWhen, Is.InstanceOf<Condition.AllModelsHaveThisRule>(),
            "'if ALL models in this unit have this rule'");
        Assert.That(ability.Effect, Is.EqualTo(
                new Effect.Reactivate(ClearsFatigue: true, ArmyRoundQuotaDivisor: 3)),
            "'stops being fatigued when activated for the second time', and 'only up to one THIRD of the " +
            "units in the army with this rule ... may use it in a single round'");
    }

    [Test]
    public void MartialProwess_KeepsNeitherRider()
    {
        // The core rule this one is modelled on. Both riders default to off, so a future edit that moves
        // either into the shared primitive would show up here.
        var effect = (Effect.Reactivate)CoreRuleCatalog.MartialProwess.Activated.Single().Effect;

        Assert.That(effect.ClearsFatigue, Is.False, "Martial Prowess says nothing about fatigue");
        Assert.That(effect.ArmyRoundQuotaDivisor, Is.Zero, "...and has no army-wide cap");
    }

    // ---- The corpus ----------------------------------------------------------------------------------

    private record Site(string Book, string Unit);

    private static IEnumerable<Site> Sites()
    {
        foreach (string path in ShippedBooks.GdfPaths()
                     .OrderBy(p => p))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            string bookName = Path.GetFileNameWithoutExtension(path);

            foreach (RosterUnit unit in book.Units)
                foreach (string name in RuleNamesOn(unit))
                    if (string.Equals(name, RuleName, StringComparison.OrdinalIgnoreCase))
                        yield return new Site(bookName, unit.Name);
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
    public void EveryReference_ResolvesAgainstItsOwnBook()
    {
        List<Site> sites = Sites().ToList();

        Assert.That(sites.Count, Is.EqualTo(20),
            "the audit's 20 references - a change here means the corpus moved, not the engine");

        foreach (string bookName in sites.Select(s => s.Book).Distinct())
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(
                File.ReadAllText(Path.Combine(BooksDirectory, bookName + BookFile.EXTENSION_WITH_PERIOD)),
                RuleJson.Options)!;

            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            foreach (SpecialRuleDefinition definition in book.RuleDefinitions)
                resolver.RegisterOrReplace(definition);

            Assert.That(resolver.TryResolve(RuleName, out ResolvedRule resolved), Is.True, bookName);
            Assert.That(resolved.Definition.Activated, Is.Not.Empty, bookName);
        }
    }

    // The quota counts the roster off ArmyData.UnitBindings, which is append-only - so it equals the
    // GAME-START roster only as long as nothing adds units to the army mid-game. Spawn, Split and
    // Reinforcement all do. No book pairs one with Inquisitorial Agent today; if one ever does, the
    // quota would silently drift upward, so this fails loudly instead.
    [Test]
    public void NoBookPairsItWithARuleThatCreatesUnitsMidGame()
    {
        string[] unitCreating = { "Spawn", "Split", "Reinforcement" };

        var offenders = new List<string>();
        foreach (string bookName in Sites().Select(s => s.Book).Distinct())
        {
            string json = File.ReadAllText(
                Path.Combine(BooksDirectory, bookName + BookFile.EXTENSION_WITH_PERIOD));

            foreach (string creator in unitCreating)
            {
                if (json.Contains($"\"{creator}\"", StringComparison.Ordinal))
                    offenders.Add($"{bookName} carries {creator}");
            }
        }

        Assert.That(offenders, Is.Empty,
            "a unit-creating rule in the same book would inflate the game-start roster the quota counts: "
            + string.Join(", ", offenders));
    }
}
