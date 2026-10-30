using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Calculator;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #447 - Impact(X) is per model ("When charging, before either unit attacks, roll X dice ..."; X is each carrying
// model's), and the engine used to roll it once per unit. The mechanism is pinned engine-side by
// ImpactPerModelRuleIntegrationTests; this pins the real data through the real compile + army load + charge
// stage (the Combat Calculator runs ResolveImpactHitsStage):
//   - every bundled multi-model unit whose PROFILE carries Impact / Heavy Impact rolls X per model;
//   - the per-model ITEMS ("Replace one CCW -> Rotor Blade Attack (Impact(3))") compile to a #407 model grant and
//     roll that one model's dice, not X x the unit - unless the profile already gives every model that item (3.6's
//     "Replace one Lance and Trample -> Trample (Impact(1))"), which stays unit-wide. Census 2026-10-30 of the
//     bundled (3.5) books: 2 such sites, both grants. The #438 re-bake changes both populations (3.6.0 has 42
//     multi-model profile carriers and 4 per-model items, 2 of them the Trample swap).
[TestFixture]
public class PerModelImpactShippedDataTests
{
    private record Book(string Name, BookFile File);

    private static IEnumerable<Book> Books() =>
        Directory.EnumerateFiles(ShippedBooks.Directory, "*" + BookFile.EXTENSION_WITH_PERIOD).OrderBy(p => p)
            .Select(p => new Book(Path.GetFileNameWithoutExtension(p),
                JsonSerializer.Deserialize<BookFile>(File.ReadAllText(p), RuleJson.Options)!));

    private static int? ImpactValue(SpecialRuleEntry rule) => rule switch
    {
        SpecialRuleEntry_CoreNumeric { Name: "Impact" or "Heavy Impact" } numeric => numeric.NumericValue,
        SpecialRuleEntry_Alias alias => ImpactValue(alias.AliasedRule),
        _ => null,
    };

    [Test]
    public void EveryMultiModelProfileCarrier_RollsXPerModel()
    {
        int checkedUnits = 0;
        foreach (Book book in Books())
        {
            foreach (RosterUnit roster in book.File.Units.Where(u => u.BaseModelCount > 1))
            {
                int perModel = roster.Rules.Select(ImpactValue).Sum(v => v ?? 0);
                if (perModel == 0) continue;

                VolleyReport? impact = ChargeImpactRow(book.File, roster.Id, roster.BaseModelCount, choice: null);
                string where = $"{book.Name} / {roster.Name} [{roster.BaseModelCount}]";
                Assert.That(impact, Is.Not.Null, $"{where}: no Impact row on the charge");
                Assert.That(impact!.AttackDice, Is.EqualTo(perModel * roster.BaseModelCount), where);
                checkedUnits++;
            }
        }

        Assert.That(checkedUnits, Is.GreaterThanOrEqualTo(42), "the census found 73 such units in the bundled books");
    }

    private record ItemSite(Book Book, RosterUnit Unit, UpgradeSection Section, UpgradeOption Option, string Item, int X)
    {
        // The profile already gives every model this item, so buying it for one model leaves it unit-wide (#447 D3).
        public bool EveryModelHoldsIt => Unit.Items.Any(i => i.Name == Item && i.Quantity >= Unit.BaseModelCount);
    }

    private static IEnumerable<ItemSite> OneModelItemSites()
    {
        foreach (Book book in Books())
            foreach (RosterUnit unit in book.File.Units.Where(u => Math.Max(u.BaseModelCount, u.MaxModels) > 1))
                foreach (UpgradeSection section in unit.Sections)
                {
                    if (section.Affects == UpgradeAffects.All || section.Variant == UpgradeVariant.AddModels) continue;
                    foreach (UpgradeOption option in section.Options)
                        foreach (ItemEntry item in option.ItemsGained)
                            if (item.Rules.Select(ImpactValue).FirstOrDefault(v => v != null) is int x)
                                yield return new ItemSite(book, unit, section, option, item.Name, x);
                }
    }

    [Test]
    public void ThePerModelItemPopulation_IsTheTwoBikers()
    {
        List<ItemSite> sites = OneModelItemSites().ToList();

        Assert.That(sites.Select(s => $"{s.Book.Name} / {s.Unit.Name} / {s.Option.Label}"), Is.EquivalentTo(new[]
        {
            "DarkElfRaiders / Jetbike Riders / Razor Caltrops (Impact(3))",
            "OrcMarauders / Hover Bikers / Rotor Blade Attack (Impact(3))",
        }), "a new site means a new per-model Impact shape - check it compiles the same way before re-pinning");
    }

    [Test]
    public void EveryPerModelItem_IsAOneModelGrant_AndRollsOnlyThatModelsDice()
    {
        foreach (ItemSite site in OneModelItemSites())
        {
            var choice = new UpgradeChoice { SectionId = site.Section.Id, OptionId = site.Option.Id, Count = 1 };
            UnitFileEntry unit = ListCompiler.Compile(site.Book.File,
                ListOf(site.Book.File, site.Unit.Id, site.Unit.BaseModelCount, choice)).Units.Single();
            string where = $"{site.Book.Name} / {site.Unit.Name} / {site.Option.Label}";
            VolleyReport? impact = ChargeImpactRow(site.Book.File, site.Unit.Id, site.Unit.BaseModelCount, choice);

            if (site.EveryModelHoldsIt)
            {
                Assert.That(unit.ModelGrants, Is.Null, $"{where}: every model still holds it - unit-wide");
                Assert.That(impact?.AttackDice, Is.EqualTo(site.X * site.Unit.BaseModelCount), where);
                continue;
            }

            Assert.That(unit.SpecialRules.Select(ImpactValue).Where(v => v != null), Is.Empty,
                $"{where}: the unit itself does not carry the item's Impact");
            Assert.That(unit.ModelGrants?.Sum(g => g.Count), Is.EqualTo(1), $"{where}: one model bought it");
            Assert.That(impact?.AttackDice, Is.EqualTo(site.X), $"{where}: one model x Impact({site.X})");
        }
    }

    // ---- harness --------------------------------------------------------------------------------------------

    private static BuilderList ListOf(BookFile book, string rosterId, int models, UpgradeChoice? choice)
    {
        var unit = new BuilderUnit { RosterUnitId = rosterId, ModelCount = models };
        if (choice != null) unit.Choices.Add(choice);
        return new BuilderList { Name = "447", BookName = book.Name, PointsLimit = 100000, Units = { unit } };
    }

    // The real compile, army load and ResolveImpactHitsStage, read back through the Combat Calculator.
    private static VolleyReport? ChargeImpactRow(BookFile book, string rosterId, int models, UpgradeChoice? choice)
    {
        BuiltArmyFile army = ListCompiler.Compile(book, ListOf(book, rosterId, models, choice));
        var targets = new ArmyListFile
        {
            Name = "Targets", Faction = "Test",
            Units = { new UnitFileEntry { Name = "Targets", ModelCount = 20, Quality = 4, Defense = 4, PointCost = 100 } },
        };

        CombatReport report = CombatCalculator.Run(army, targets,
            new CombatSituation(Mode: ECombatMode.Melee, AttackerCharging: true));
        return report.Volleys.FirstOrDefault(v => v.Weapon.Name == "Impact");
    }
}
