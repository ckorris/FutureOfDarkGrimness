using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.SaveLoad;
using FdgRaylib.Import;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #344 - the two rules a live game reported as "not implemented" (Heavy Impact on Ripjawdactyl Riders,
// Vengeance on Royal Guard / ONIs) were implemented, authored and embedded in their books; the LISTS
// were saved before those commits and froze a copy of the book's definitions without them. The engine
// half of the backfill is pinned by ArmyRulebookBackfillIntegrationTests against a stub rulebook; this
// pins the app half against the REAL shipped assets - that the faction on a saved army actually finds
// its book on disk, and that the book actually carries the definition the stale list is missing.
[TestFixture]
public class BundledBookRulebookTests
{
    private static readonly (string Faction, string Rule, string Unit)[] ReportedCases =
    {
        ("Saurian Starhost", "Heavy Impact", "Ripjawdactyl Riders"),
        ("Eternal Dynasty", "Vengeance", "Royal Guard"),
    };

    [TearDown]
    public void ClearInstalledRulebook() => CurrentRulebook.Installed = null;

    [Test]
    public void EveryReportedCase_ResolvesThroughTheBundledBook()
    {
        BundledBookRulebook.Install();

        foreach ((string faction, string rule, string unit) in ReportedCases)
        {
            IReadOnlyList<SpecialRuleDefinition> definitions = CurrentRulebook.DefinitionsForFaction(faction);

            Assert.That(definitions.Select(d => d.Name), Does.Contain(rule),
                $"the bundled {faction} book must define {rule} - it is what a list too old to carry " +
                $"the definition backfills from ({unit} loses the rule otherwise).");
            Assert.That(CurrentRulebook.Defines(rule), Is.True,
                $"{rule} must also be known rulebook-wide, so a list with no matching faction reports " +
                "'this list is outdated' rather than 'not implemented'.");
        }
    }

    [Test]
    public void AStaleList_LoadsTheRuleItIsTooOldToDefine()
    {
        // The shipped shape of the reported bug: unit rule reference present, definition absent.
        BundledBookRulebook.Install();

        ArmyListFile stale = new ArmyListFile
        {
            Name = "Saurian Starhost 3k",
            Faction = "Saurian Starhost",
            Units =
            {
                new UnitFileEntry
                {
                    Name = "Ripjawdactyl Riders", ModelCount = 3, Quality = 4, Defense = 4,
                    SpecialRules = { new SpecialRuleEntry_CoreNumeric("Heavy Impact", 3) },
                    Weapons = { new WeaponFileEntry { Name = "Claws", Quantity = 3, Attacks = 2 } },
                },
            },
        };

        Assert.That(ArmyRuleAudit.Audit(stale).Drops, Is.Empty,
            "with the bundled book consulted, the saved list fields Heavy Impact(3) again.");
    }

    [Test]
    public void AnUnknownFaction_BackfillsNothing_AndDoesNotThrow()
    {
        BundledBookRulebook.Install();

        Assert.That(CurrentRulebook.DefinitionsForFaction("Not A Real Faction"), Is.Empty);
        Assert.That(CurrentRulebook.DefinitionsForFaction(null), Is.Empty);
        Assert.That(CurrentRulebook.Defines("Frobnicate"), Is.False);
    }

    [Test]
    public void Install_IsIdempotent_AndNeverDisplacesAnInstalledSource()
    {
        BundledBookRulebook.Install();
        ICurrentRulebook? first = CurrentRulebook.Installed;

        BundledBookRulebook.Install();

        Assert.That(CurrentRulebook.Installed, Is.SameAs(first));
    }
}
