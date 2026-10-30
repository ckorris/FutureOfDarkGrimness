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

// #197 Strafing / #431 Crossing Strike - the strafe weapons in the shipped books. 3.6 renamed Strafing to Crossing
// Strike (a GDF supplement rule, the same activated ability - WeaponRules36ShippedDataTests pins it), and #436 deleted
// the catalog Strafing, retiring this file's catalog-shape tests and its "12 references resolve" pin. The bundled
// books are still the 3.5 bake until #438, so their Strafing references are dead in the interim (the #168 drop);
// the re-bake gives the bombs Crossing Strike.
//
// What is still pinned is the property whose failure mode is silent: neither rule grants a fly-over, so every carrier
// needs Aircraft or Flying of its own. If an import adds a strafe bomb to a grounded unit, the weapon can never be
// used - the engine warns, but a shipped book should never reach that state. It reads both names so it holds before
// and after the re-bake. The mechanism is pinned engine-side by StrafingRuleIntegrationTests.
[TestFixture]
public class StrafingShippedDataTests
{
    // Every Strafing reference in the corpus, with the unit it belongs to and whether that unit can fly.
    private record StrafeSite(string Book, string Unit, string Weapon, bool CanFlyOver);

    private static IEnumerable<StrafeSite> StrafeSites()
    {
        foreach (string path in ShippedBooks.GdfPaths()
                     .OrderBy(p => p))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            string bookName = Path.GetFileNameWithoutExtension(path);

            foreach (RosterUnit unit in book.Units)
            {
                // A unit's own rules, plus any granted by wargear items it starts with.
                var baseRules = unit.Rules.Select(NameOf)
                    .Concat(unit.Items.SelectMany(item => item.Rules.Select(NameOf)))
                    .ToList();

                foreach (WeaponFileEntry weapon in unit.Weapons)
                {
                    if (!weapon.SpecialRules.Any(IsStrafing)) continue;
                    yield return new StrafeSite(bookName, unit.Name, weapon.Name, CanFly(baseRules));
                }

                foreach (UpgradeSection section in unit.Sections)
                    foreach (UpgradeOption option in section.Options)
                    {
                        foreach (WeaponFileEntry weapon in option.WeaponsGained)
                        {
                            if (!weapon.SpecialRules.Any(IsStrafing)) continue;

                            // The upgrade that grants the bomb may also grant the wings - Saurian's Gecko
                            // Champion is a footslogger until it buys the Pterodactyl, which brings both.
                            List<string> withOption = baseRules
                                .Concat(option.RulesGained.Select(NameOf))
                                .Concat(option.ItemsGained.SelectMany(item => item.Rules.Select(NameOf)))
                                .ToList();
                            yield return new StrafeSite(bookName, unit.Name, weapon.Name, CanFly(withOption));
                        }
                    }
            }
        }
    }

    private static string NameOf(SpecialRuleEntry rule) =>
        ArmyListRuleResolution.DescribeRuleEntry(rule).lookupName;

    private static bool IsStrafing(SpecialRuleEntry rule) =>
        string.Equals(NameOf(rule), "Strafing", StringComparison.OrdinalIgnoreCase)
        || string.Equals(NameOf(rule), "Crossing Strike", StringComparison.OrdinalIgnoreCase);

    private static bool CanFly(IEnumerable<string> ruleNames) => ruleNames.Any(name =>
        string.Equals(name, "Flying", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "Aircraft", StringComparison.OrdinalIgnoreCase));

    [Test]
    public void TheShippedBooks_StillCarryStrafeWeapons()
    {
        Assert.That(StrafeSites().Count(), Is.GreaterThan(0),
            "the carrier check below must have something to check - a zero here means the name moved again");
    }

    [Test]
    public void EveryStrafingCarrier_CanMoveThroughEnemies()
    {
        List<string> grounded = StrafeSites()
            .Where(site => !site.CanFlyOver)
            .Select(site => $"{site.Book}: {site.Unit} ({site.Weapon})")
            .ToList();

        Assert.That(grounded, Is.Empty,
            "Strafing / Crossing Strike grant no fly-over, so a carrier without Aircraft or Flying can never use the " +
            "weapon at all: " + string.Join(", ", grounded));
    }
}
