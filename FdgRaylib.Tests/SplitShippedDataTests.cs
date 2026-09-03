using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #197 P17b - the shipped Split data (3 refs, Wormhole Daemons of Change). The mechanism (the
// killer-less self-destroyed seam + the P17a creation machinery) is pinned by the engine's
// SplitRuleIntegrationTests; these pin the JSON and the real book's Split CHAIN - Change Horrors split
// into Lesser Change Horrors, which themselves split into Changelings, so a compiled army must carry
// BOTH links or the second split dies at the table.
[TestFixture]
public class SplitShippedDataTests
{
    private const string RuleName = "Split";

    [Test]
    public void Split_IsASelfDestroyedSpawn_WithATextArgument()
    {
        SpecialRuleDefinition definition = BookRuleSupplement.LoadDefinitions(File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Assets", "Books", "GdfRuleSupplement.json")))
            .Single(r => r.Name == RuleName);

        HookEntry entry = definition.Passive.Single();
        Assert.That(entry.HookID, Is.EqualTo(EHookID.Lifecycle_OnSelfDestroyed),
            "'when this unit is fully destroyed' - the killer-less hook, so a rout still splits");
        Assert.That(((Effect.SpawnUnit)entry.Effect).RadiusInches, Is.EqualTo(6f).Within(0.001f));
        Assert.That(definition.EngineArgumentCount, Is.EqualTo(1));
    }

    [Test]
    public void TheRealBooksSplitChain_CompilesBothLinksIntoTheArmy()
    {
        BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Books", "WormholeDaemonsofChange.fdgbook")),
            RuleJson.Options)!;
        Assert.That(book.RuleDefinitions.Any(d => d.Name == RuleName), Is.True,
            "the book embeds the Split definition");

        RosterUnit horrors = book.Units.Single(u => u.Name == "Change Horrors");
        BuiltArmyFile army = ListCompiler.Compile(book, new BuilderList
        {
            PointsLimit = 100000,
            Units = { new BuilderUnit { RosterUnitId = horrors.Id } },
        });

        Assert.That(army.AuxiliaryUnits, Is.Not.Null);
        UnitFileEntry lesser = army.AuxiliaryUnits!
            .Single(u => u.Id == "Lesser Change Horrors [5]");
        Assert.That(lesser.SpecialRules.OfType<SpecialRuleEntry_Text>()
                .Any(r => r.Name == RuleName && r.TextValue == "Changelings [10]"),
            Is.True, "the middle link keeps its own Split argument");
        Assert.That(army.AuxiliaryUnits.Any(u => u.Id == "Changelings [10]"), Is.True,
            "the chain's last link ships too - the recursion is what makes the second split work");
    }
}
