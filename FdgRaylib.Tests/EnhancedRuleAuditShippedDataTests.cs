using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #441 F5 (signed off B, 2026-10-31): the Army Builder's validation pane lists "Unit: Enhanced X does nothing without
// X" (engine EnhancedRuleAudit) for a hand-edited list. Pinned here against a SHIPPED 3.6 book's definitions, so the
// check reads the real #433 gate shape: Enhanced Lustbound gates on UnitHasRule("Lustbound"); Enhanced Lustbound Aura
// gates on nothing (it grants, it does not depend) and so says nothing.
[TestFixture]
public class EnhancedRuleAuditShippedDataTests
{
    private static BookFile DaemonsOfLust() =>
        JsonSerializer.Deserialize<BookFile>(
            File.ReadAllText(Path.Combine(ShippedBooks.Directory, "AoF-DaemonsofLust" + BookFile.EXTENSION_WITH_PERIOD)),
            RuleJson.Options)!;

    private static BookFile WatchBrothers() =>
        JsonSerializer.Deserialize<BookFile>(
            File.ReadAllText(Path.Combine(ShippedBooks.Directory, "WatchBrothers" + BookFile.EXTENSION_WITH_PERIOD)),
            RuleJson.Options)!;

    private static ArmyListFile HandEdited(params string[] rules) => HandEdited(DaemonsOfLust(), "Lust Daemons", rules);

    private static ArmyListFile HandEdited(BookFile book, string unitName, params string[] rules)
    {
        UnitFileEntry unit = new() { Name = unitName, ModelCount = 5, Quality = 4, Defense = 4, PointCost = 100 };
        foreach (string rule in rules) unit.SpecialRules.Add(new SpecialRuleEntry_Core(rule));
        ArmyListFile army = new() { Name = "Hand-edited", GameSystem = book.GameSystem, PointsLimit = 750, Units = { unit } };
        army.RuleDefinitions.AddRange(book.RuleDefinitions);
        return army;
    }

    [Test]
    public void EnhancedLustboundWithoutLustbound_IsFlagged_FromTheShippedDefinition()
    {
        Assert.That(EnhancedRuleAudit.Audit(HandEdited("Enhanced Lustbound")),
            Is.EqualTo(new[] { "Lust Daemons: Enhanced Lustbound does nothing without Lustbound" }));
    }

    [Test]
    public void WithLustbound_OrOnlyTheAura_NothingIsSaid()
    {
        Assert.Multiple(() =>
        {
            Assert.That(EnhancedRuleAudit.Audit(HandEdited("Lustbound", "Enhanced Lustbound")), Is.Empty);
            Assert.That(EnhancedRuleAudit.Audit(HandEdited("Enhanced Lustbound Aura")), Is.Empty,
                "the aura grants Enhanced Lustbound to others; it depends on no base rule of its bearer");
        });
    }

    // #441 F7: Enhanced Watchborn gates on the helpers Watchborn's activation pick grants ("Watchborn (Piercing)" /
    // "(Precision)"), which no list carries; the dependency is on Watchborn itself, so a Forge-built Watch Brothers
    // unit (Watchborn + Enhanced Watchborn) is clean and a hand-edited one without Watchborn names Watchborn, not
    // the helpers.
    [Test]
    public void EnhancedWatchborn_DependsOnWatchborn_NotOnTheActivationHelpersItGatesOn()
    {
        Assert.Multiple(() =>
        {
            Assert.That(EnhancedRuleAudit.Audit(HandEdited(WatchBrothers(), "Watch Battle Brothers", "Watchborn", "Enhanced Watchborn")),
                Is.Empty, "the shipped shape: the unit carries Watchborn");
            Assert.That(EnhancedRuleAudit.Audit(HandEdited(WatchBrothers(), "Watch Battle Brothers", "Enhanced Watchborn")),
                Is.EqualTo(new[] { "Watch Battle Brothers: Enhanced Watchborn does nothing without Watchborn" }));
        });
    }
}
