using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #197 P22 - the shipped Ambush Re-Deployment data (4 refs, Elven Jesters). The mechanism (the
// end-of-activation ability seam, the executable removal, the mandatory return) is pinned by the
// engine's AmbushRedeployRuleIntegrationTests over a LOCAL definition; these pin the JSON as authored -
// above all that the two halves meet on the SAME token, since a defer gated on a token the removal
// never stamps would strand the unit off-table forever.
[TestFixture]
public class AmbushRedeployShippedDataTests
{
    private const string RuleName = "Ambush Re-Deployment";

    private static SpecialRuleDefinition Definition() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Assets", "Books", "GdfRuleSupplement.json")))
            .Single(r => r.Name == RuleName);

    [Test]
    public void TheRemoval_IsAOncePerGameOptionalAbility_AtActivationEnd()
    {
        ActivatedAbility ability = Definition().Activated.Single();

        Assert.That(ability.TriggerHook, Is.EqualTo(EHookID.Activation_OnEndOfActivation),
            "'when a unit ... ends its activation'");
        Assert.That(ability.Cost, Is.InstanceOf<Cost.OncePerGame>(), "'once per game'");
        Assert.That(ability.AvailableWhen, Is.InstanceOf<Condition.AllModelsHaveThisRule>(),
            "'a unit where all models have this rule'");
        Assert.That(ability.Effect, Is.InstanceOf<Effect.AmbushRedeploy>());
    }

    [Test]
    public void TheReturnLeg_IsAMandatoryAmbushDefer_GatedOnTheTokenTheRemovalStamps()
    {
        HookEntry entry = Definition().Passive.Single();

        Assert.That(entry.HookID, Is.EqualTo(EHookID.Deployment_OnPreDeploymentSelect));
        var gate = (Condition.TokenPresent)entry.Condition;
        Assert.That(gate.TType, Is.EqualTo(TokenType.PendingAmbushArrival),
            "the SAME token Effect.AmbushRedeploy stamps - gating on any other strands the unit off-table");

        var defer = (Effect.DeferDeployment)entry.Effect;
        Assert.That(defer.Timing, Is.EqualTo(EDeferTiming.LaterRound), "'as if it had Ambush'");
        Assert.That(defer.PlacementRangeInches, Is.EqualTo(9f).Within(0.001f));
        Assert.That(defer.MandatoryArrival, Is.True,
            "owner-ruled 2026-07-28: the return at the next round start is mandatory, not offered");
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
            if (!json.Contains("\"ambushRedeploy\"", StringComparison.Ordinal))
            {
                missing.Add(Path.GetFileName(path));
            }
        }

        Assert.That(missing, Is.Empty,
            "books referencing Ambush Re-Deployment without its embedded definition: "
            + string.Join(", ", missing));
    }
}
