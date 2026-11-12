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

                VolleyReport? impact = ChargeImpactRow(book.File, roster.Id, roster.BaseModelCount);
                string where = $"{book.Name} / {roster.Name} [{roster.BaseModelCount}]";
                Assert.That(impact, Is.Not.Null, $"{where}: no Impact row on the charge");
                Assert.That(impact!.AttackDice, Is.EqualTo(perModel * roster.BaseModelCount), where);
                checkedUnits++;
            }
        }

        Assert.That(checkedUnits, Is.GreaterThanOrEqualTo(42), "the census found 73 such units in the bundled 3.5 books, 42 in the 3.6.0 ones");
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

    // #438: the 3.6.0 population (the #447 hand-off's four, checked against the 2026-10-30 snapshots): the Orc
    // Hover Bikers' and AoF Ratmen Storm Ogres' items become one-model grants; the two Trample swaps stay
    // unit-wide (the profile already gives every model Trample). The 3.5 Dark Elf Raiders Razor Caltrops is gone.
    [Test]
    public void ThePerModelItemPopulation_IsTheFour36Sites()
    {
        List<ItemSite> sites = OneModelItemSites().ToList();

        Assert.That(sites.Select(s => $"{s.Book.Name} / {s.Unit.Name} / {s.Option.Label}"), Is.EquivalentTo(new[]
        {
            "OrcMarauders / Hover Bikers / Rotor Blade Attack (Impact(3))",
            "AoF-Ratmen / Storm Ogres / Roller-Fists (Heavy Impact(3))",
            "AoF-DragonEmpire / Dragonkin Horsemen / Dragon Glaive (A1, AP(2), Deadly(3), Thrust), Trample (Impact(1))",
            "AoF-GhostlyUndead / Dread Knights / Death Bell (A1, AP(1), Blast(3)), Trample (Impact(1))",
        }), "a new site means a new per-model Impact shape - check it compiles the same way before re-pinning");
        Assert.That(sites.Count(s => s.EveryModelHoldsIt), Is.EqualTo(2), "the two Trample swaps");
    }

    [Test]
    public void EveryPerModelItem_IsAOneModelGrant_AndRollsOnlyThatModelsDice()
    {
        foreach (ItemSite site in OneModelItemSites())
        {
            var choice = new UpgradeChoice { SectionId = site.Section.Id, OptionId = site.Option.Id, Count = 1 };
            UpgradeChoice[] choices = Prerequisites(site).Append(choice).ToArray();
            UnitFileEntry unit = ListCompiler.Compile(site.Book.File,
                ListOf(site.Book.File, site.Unit.Id, site.Unit.BaseModelCount, choices)).Units.Single();
            string where = $"{site.Book.Name} / {site.Unit.Name} / {site.Option.Label}";
            VolleyReport? impact = ChargeImpactRow(site.Book.File, site.Unit.Id, site.Unit.BaseModelCount, choices);

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

    // #438: a 3.6 item section may replace something the base loadout lacks - the Orc Hover Bikers' "Replace one
    // Hand Weapon -> Rotor Blade Attack" needs the "Replace all ... -> Assault Bike-Gun, Pistol, Hand Weapon" swap
    // first. Buy the first option of another section that supplies the target, so the item has something to replace.
    private static IEnumerable<UpgradeChoice> Prerequisites(ItemSite site)
    {
        if (ListCompiler.AvailableApplications(site.Unit.Weapons, site.Unit.Items, site.Section.Targets) > 0)
            yield break;
        foreach (UpgradeSection other in site.Unit.Sections.Where(s => !ReferenceEquals(s, site.Section)))
            foreach (UpgradeOption option in other.Options)
                if (ListCompiler.AvailableApplications(option.WeaponsGained.Select(w => new WeaponFileEntry
                        { Name = w.Name, Quantity = 1000 }).ToList(), option.ItemsGained, site.Section.Targets) > 0)
                {
                    yield return new UpgradeChoice { SectionId = other.Id, OptionId = option.Id, Count = 1 };
                    yield break;
                }
    }

    private static BuilderList ListOf(BookFile book, string rosterId, int models, params UpgradeChoice[] choices)
    {
        var unit = new BuilderUnit { RosterUnitId = rosterId, ModelCount = models };
        unit.Choices.AddRange(choices);
        return new BuilderList { Name = "447", BookName = book.Name, PointsLimit = 100000, Units = { unit } };
    }

    // The real compile, army load and ResolveImpactHitsStage, read back through the Combat Calculator.
    private static VolleyReport? ChargeImpactRow(BookFile book, string rosterId, int models, params UpgradeChoice[] choices)
    {
        BuiltArmyFile army = ListCompiler.Compile(book, ListOf(book, rosterId, models, choices));
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
