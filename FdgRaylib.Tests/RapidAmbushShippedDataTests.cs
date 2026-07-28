using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #197 P22 - the shipped Rapid Ambush data (4 refs: Dark Prime Brothers, Dark Brothers). The mechanism
// (per-unit MinArrivalRound at the round-start arrival pass) is pinned by the engine's
// RapidAmbushRuleIntegrationTests; these pin the JSON as authored - in particular that minArrivalRound
// actually deserializes to 1, since an entry that silently fell back to the default 2 would validate,
// lint and play exactly like core Ambush.
[TestFixture]
public class RapidAmbushShippedDataTests
{
    private const string RuleName = "Rapid Ambush";

    private static IReadOnlyList<SpecialRuleDefinition> Supplement() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Books", "GdfRuleSupplement.json")));

    [Test]
    public void RapidAmbush_IsAmbushWithARoundOneArrival()
    {
        HookEntry entry = Supplement().Single(r => r.Name == RuleName).Passive.Single();

        Assert.That(entry.HookID, Is.EqualTo(EHookID.Deployment_OnPreDeploymentSelect));
        var defer = (Effect.DeferDeployment)entry.Effect;
        Assert.That(defer.Timing, Is.EqualTo(EDeferTiming.LaterRound), "'counts as having Ambush'");
        Assert.That(defer.PlacementRangeInches, Is.EqualTo(9f).Within(0.001f),
            "the core Ambush over-9\" arrival constraint");
        Assert.That(defer.MinArrivalRound, Is.EqualTo(1),
            "'may be deployed at the start of any round, including the first'");
    }

    [Test]
    public void EveryBookReferencingIt_EmbedsTheDefinition()
    {
        string booksDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Books");
        List<string> missing = new List<string>();

        foreach (string path in Directory.GetFiles(booksDir, "*.fdgbook"))
        {
            string json = File.ReadAllText(path);
            if (!json.Contains($"\"name\": \"{RuleName}\"", StringComparison.Ordinal)) continue;
            if (!json.Contains("\"minArrivalRound\": 1", StringComparison.Ordinal))
            {
                missing.Add(Path.GetFileName(path));
            }
        }

        Assert.That(missing, Is.Empty,
            "books referencing Rapid Ambush without its embedded round-1 definition: "
            + string.Join(", ", missing));
    }
}
