using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #407 - the shipped books' one-model Tough upgrades. Census 2026-10-28: across all bundled books, a
// per-model section (Affects One/Any) on a MULTI-model unit grants Tough through an item at exactly 16
// sites, every one a "Weapon Team" - HDF Infantry Squad and Veterans, and AoF Ratmen Warriors. Each used
// to compile to a unit-wide Tough(3) (30 wounds on a 10-model squad). The mechanism is pinned engine-side
// by PerModelToughGrantTests; this pins the real data: the population, and that every site now compiles
// to a one-model grant with the unit itself left un-Tough.
[TestFixture]
public class PerModelToughShippedDataTests
{
    private record Site(string Book, BookFile File, RosterUnit Unit, UpgradeSection Section, UpgradeOption Option);

    private static IEnumerable<Site> Sites()
    {
        foreach (string path in Directory.EnumerateFiles(ShippedBooks.Directory, "*" + BookFile.EXTENSION_WITH_PERIOD)
                     .OrderBy(p => p))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            foreach (RosterUnit unit in book.Units)
            {
                if (Math.Max(unit.BaseModelCount, unit.MaxModels) <= 1) continue;
                foreach (UpgradeSection section in unit.Sections)
                {
                    if (section.Affects == UpgradeAffects.All || section.Variant == UpgradeVariant.AddModels) continue;
                    foreach (UpgradeOption option in section.Options)
                        if (option.ItemsGained.Any(item => item.Rules.Any(IsTough)))
                            yield return new Site(Path.GetFileNameWithoutExtension(path), book, unit, section, option);
                }
            }
        }
    }

    private static bool IsTough(SpecialRuleEntry rule) =>
        rule is SpecialRuleEntry_CoreNumeric { Name: "Tough" }
        || rule is SpecialRuleEntry_Alias { AliasedRule: SpecialRuleEntry_CoreNumeric { Name: "Tough" } };

    [Test]
    public void ThePopulation_IsTheSixteenWeaponTeams()
    {
        List<Site> sites = Sites().ToList();

        Assert.That(sites, Has.Count.EqualTo(16),
            "a new site means a new per-model Tough shape - check it compiles the same way before re-pinning");
        Assert.That(sites.Select(s => s.Book).Distinct(), Is.EquivalentTo(new[] { "HumanDefenseForce", "AoF-Ratmen" }));
        Assert.That(sites.Select(s => s.Option.ItemsGained.Single(i => i.Rules.Any(IsTough)).Name).Distinct(),
            Is.EquivalentTo(new[] { "Weapon Team" }));
    }

    [Test]
    public void EverySite_CompilesToAOneModelGrant_NotAToughUnit()
    {
        foreach (Site site in Sites())
        {
            var list = new BuilderList
            {
                Name = "Test", BookName = site.File.Name, PointsLimit = 5000,
                Units =
                {
                    new BuilderUnit
                    {
                        RosterUnitId = site.Unit.Id, ModelCount = site.Unit.BaseModelCount,
                        Choices = { new UpgradeChoice { SectionId = site.Section.Id, OptionId = site.Option.Id, Count = 1 } },
                    },
                },
            };

            UnitFileEntry unit = ListCompiler.Compile(site.File, list).Units.Single();
            string where = $"{site.Book} / {site.Unit.Name} / {site.Option.Label}";

            Assert.That(unit.SpecialRules.Where(IsTough), Is.Empty, $"{where}: the unit itself is not Tough");
            Assert.That(unit.ModelGrants, Has.Count.EqualTo(1), where);
            Assert.That(unit.ModelGrants![0].Count, Is.EqualTo(1), $"{where}: one Weapon Team, one model");
            Assert.That(unit.ModelGrants[0].Rules, Is.EqualTo(new[] { new SpecialRuleEntry_CoreNumeric("Tough", 3) }), where);
            Assert.That(ForgeUnitDetail.ToughValue(unit), Is.EqualTo("3 (1 model)"),
                $"{where}: the Forge pill says how many models are Tough");
        }
    }
}
