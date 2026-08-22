using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG;
using FDG.ArmyBuilding;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #197 Extended Buff Range - the HDF radios: "relay non-spell Hero picks across 24in via another friendly
// unit with the rule." A dead no-definition name (9 refs, all Field/Vehicle Radio items) until 2026-07-29:
// the bearer now answers Lifecycle_OnCapabilityQuery with enableBuffRelay(12), and AbilityTargeting lets a
// FRIENDLY pick measure from the bearer's position - the ability twin of Spell Conduit, minus the roll
// bonus. The mechanism (both relay legs, the Foe/sight gates, the stage pipeline) is pinned engine-side by
// ExtendedBuffRangeRuleIntegrationTests; these pin the authored JSON, the embedded book copy, and the
// corpus shape.
[TestFixture]
public class ExtendedBuffRangeShippedDataTests
{
    private const string RuleName = "Extended Buff Range";

    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static SpecialRuleDefinition ExtendedBuffRange() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
                Path.Combine(BooksDirectory, "GdfRuleSupplement.json")))
            .Single(r => r.Name == RuleName);

    // ---- The authored definition ----------------------------------------------------------------------

    [Test]
    public void ExtendedBuffRange_IsAnUngatedCapabilityRelay()
    {
        SpecialRuleDefinition rule = ExtendedBuffRange();
        HookEntry entry = rule.Passive.Single();

        Assert.That(entry.HookID, Is.EqualTo(EHookID.Lifecycle_OnCapabilityQuery),
            "capabilities are asked for at the query hook - anywhere else nothing would collect the offer");
        Assert.That(entry.Condition, Is.InstanceOf<Condition.Always>(),
            "unlike Spell Conduit the corpus wording carries no Shaken gate, so none is authored");
        Assert.That(entry.Effect, Is.InstanceOf<Effect.EnableBuffRelay>());
        Assert.That(((Effect.EnableBuffRelay)entry.Effect).RangeInches, Is.EqualTo(12f),
            "the relay leg: the user must be within 12\" of the radio");
        Assert.That(rule.Activated, Is.Empty, "a capability answer, not an activated ability");
        Assert.That(rule.EngineArgumentCount, Is.EqualTo(0));
        Assert.That(rule.Scope, Is.EqualTo(ERuleScope.Unit));
    }

    // ---- The corpus -----------------------------------------------------------------------------------

    private record Site(string Book, string Unit, string Item);

    private static IEnumerable<Site> Sites()
    {
        foreach (string path in Directory.EnumerateFiles(BooksDirectory, "*" + BookFile.EXTENSION_WITH_PERIOD)
                     .OrderBy(p => p))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            string bookName = Path.GetFileNameWithoutExtension(path);

            foreach (RosterUnit unit in book.Units)
                foreach (UpgradeSection section in unit.Sections)
                    foreach (UpgradeOption option in section.Options)
                        foreach (ItemEntry item in option.ItemsGained)
                            foreach (SpecialRuleEntry rule in item.Rules)
                            {
                                if (rule is SpecialRuleEntry_Core core
                                    && string.Equals(core.Name, RuleName, StringComparison.Ordinal))
                                {
                                    yield return new Site(bookName, unit.Name, item.Name);
                                }
                            }
        }
    }

    [Test]
    public void EveryReference_IsARadioItemInHumanDefenseForce()
    {
        List<Site> sites = Sites().ToList();

        Assert.That(sites.Count, Is.EqualTo(9),
            "the audit's 9 references - a change here means the corpus moved, not the engine");
        Assert.That(sites.Select(s => s.Book).Distinct(), Is.EquivalentTo(new[] { "HumanDefenseForce" }));
        Assert.That(sites.Select(s => s.Item).Distinct(),
            Is.EquivalentTo(new[] { "Field Radio", "Vehicle Radio" }),
            "infantry carry Field Radios, vehicles carry Vehicle Radios - both grant the same rule");
    }

    [Test]
    public void TheEmbeddedBookCopy_CarriesTheRelay()
    {
        // --apply-rules embeds the supplement's definition into the referencing book. A slice that edits
        // the supplement and forgets to re-embed ships a book whose radios are still an unresolvable name.
        BookFile book = JsonSerializer.Deserialize<BookFile>(
            File.ReadAllText(Path.Combine(BooksDirectory, "HumanDefenseForce" + BookFile.EXTENSION_WITH_PERIOD)),
            RuleJson.Options)!;

        RuleResolver resolver = CoreRuleCatalog.CreateResolver();
        foreach (SpecialRuleDefinition definition in book.RuleDefinitions)
        {
            resolver.RegisterOrReplace(definition);
        }

        Assert.That(resolver.TryResolve(RuleName, out ResolvedRule resolved), Is.True);
        Assert.That(resolved.Definition.Passive.Any(e => e.Effect is Effect.EnableBuffRelay), Is.True,
            "the embedded copy carries the relay capability - re-run --apply-rules");
    }

    // ---- End to end: the shipped data through the real attach + capability path ------------------------

    [Test]
    public void AFieldRadio_OnAShippedBookUnit_AnswersTheCapabilityQuery()
    {
        // The full flow a real army-load runs: the book's embedded definition registered on a resolver,
        // the item's core entry resolved, attached at unit scope, and CapabilityRuleQueries collecting the
        // offer AbilityTargeting will scan for.
        BookFile book = JsonSerializer.Deserialize<BookFile>(
            File.ReadAllText(Path.Combine(BooksDirectory, "HumanDefenseForce" + BookFile.EXTENSION_WITH_PERIOD)),
            RuleJson.Options)!;

        RuleResolver resolver = CoreRuleCatalog.CreateResolver();
        foreach (SpecialRuleDefinition definition in book.RuleDefinitions)
        {
            resolver.RegisterOrReplace(definition);
        }

        ResolvedRule? resolved = ArmyListRuleResolution.ResolveForScope(resolver,
            new SpecialRuleEntry_Core(RuleName), ERuleScope.Unit, "Infantry Squad (test)");
        Assert.That(resolved, Is.Not.Null, "the radio's rule resolves at unit scope against the shipped book");

        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
            initialPosition: new Position(), gameDataStore: store);
        var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Infantry Squad", quality: 5, defense: 5,
            modelBindings: new List<DataBinding<ModelData>>
            {
                store.GetDataBinding<ModelData>(store.Create(model)),
            });
        unit.AttachRuleDefinition(resolved!);

        IReadOnlyList<RuleOperation.EnableBuffRelay> offers = CapabilityRuleQueries.BuffRelayOffers(
            unit, new RuleEvaluator(new ProbabilisticDiceRoller()));

        Assert.That(offers, Has.Count.EqualTo(1), "the radio squad offers exactly one relay");
        Assert.That(offers[0].RangeInches, Is.EqualTo(12f));
    }
}
