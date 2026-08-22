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

// #197 Strafing - the 12 corpus references, which were dead as a scope mismatch until the catalog rule was
// re-authored at weapon scope. Unlike most #197 slices this one changed NO book data: the books were right
// all along (every reference sits on a bomb weapon) and the catalog was the approximation.
//
// What is pinned here is that agreement, plus the two properties whose failure modes are silent:
//  - the rule attaches at Weapon scope, without which army load drops all 12 references again;
//  - it carries no fly-over passive, and its carriers therefore need Aircraft or Flying of their own. If a
//    future import adds a Strafing bomb to a grounded unit, the weapon can never be used - the engine warns,
//    but a shipped book should never reach that state.
// The mechanism (the attack, the pick, the weapon restriction) is pinned engine-side by
// StrafingRuleIntegrationTests.
[TestFixture]
public class StrafingShippedDataTests
{
    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    [Test]
    public void TheCatalogRule_IsWeaponScoped_AndAttacksWithTheCarryingWeapon()
    {
        SpecialRuleDefinition strafing = CoreRuleCatalog.Strafing;

        Assert.That(strafing.Scope, Is.EqualTo(ERuleScope.Weapon),
            "every corpus reference sits on a weapon - at unit scope army load drops all 12");

        ActivatedAbility ability = strafing.Activated.Single();
        Assert.That(ability.TriggerHook, Is.EqualTo(EHookID.Movement_OnMoveThroughEnemy));
        Assert.That(ability.Cost, Is.InstanceOf<Cost.OncePerActivation>(), "'once per activation'");
        Assert.That(ability.TargetSelector.MaxCount, Is.EqualTo(1), "'pick ONE of them'");
        Assert.That(ability.TargetSelector.TargetAffinity, Is.EqualTo(ETargetAffinity.Foe));
        Assert.That(ability.Effect, Is.InstanceOf<Effect.AttackWithThisWeapon>(),
            "'attack it with this weapon as if it was shooting' - a fixed hit count would ignore the " +
            "weapon's own Attacks, AP and Blast");
    }

    [Test]
    public void TheCatalogRule_GrantsNoFlyOver()
    {
        // The source rule presupposes a unit that can already move through enemies rather than granting it.
        // Keeping the passive here would also be inert: it is weapon-scoped now, and the movement hook that
        // MovementRuleQueries.CanMoveThroughEnemies fires never reads weapon rules.
        Assert.That(CoreRuleCatalog.Strafing.Passive, Is.Empty,
            "Aircraft and Flying grant the fly-over; Strafing only uses it");
    }

    // Every Strafing reference in the corpus, with the unit it belongs to and whether that unit can fly.
    private record StrafeSite(string Book, string Unit, string Weapon, bool CanFlyOver);

    private static IEnumerable<StrafeSite> StrafeSites()
    {
        foreach (string path in Directory.EnumerateFiles(BooksDirectory, "*" + BookFile.EXTENSION_WITH_PERIOD)
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
        string.Equals(NameOf(rule), "Strafing", StringComparison.OrdinalIgnoreCase);

    private static bool CanFly(IEnumerable<string> ruleNames) => ruleNames.Any(name =>
        string.Equals(name, "Flying", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "Aircraft", StringComparison.OrdinalIgnoreCase));

    [Test]
    public void EveryStrafingReference_SitsOnAWeapon_AndResolvesCleanly()
    {
        List<StrafeSite> sites = StrafeSites().ToList();

        Assert.That(sites.Count, Is.EqualTo(12),
            "the audit's 12 Strafing references - a change here means the corpus moved, not the engine");
        Assert.That(CoreRuleCatalog.CreateResolver().TryResolve("Strafing", out ResolvedRule resolved), Is.True);
        Assert.That(resolved.Definition.Scope, Is.EqualTo(ERuleScope.Weapon));
    }

    [Test]
    public void EveryStrafingCarrier_CanMoveThroughEnemies()
    {
        List<string> grounded = StrafeSites()
            .Where(site => !site.CanFlyOver)
            .Select(site => $"{site.Book}: {site.Unit} ({site.Weapon})")
            .ToList();

        Assert.That(grounded, Is.Empty,
            "Strafing grants no fly-over, so a carrier without Aircraft or Flying can never use the " +
            "weapon at all: " + string.Join(", ", grounded));
    }
}
