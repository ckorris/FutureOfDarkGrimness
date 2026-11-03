using System;
using System.IO;
using System.Linq;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #426 - the deployment-distance rules as SHIPPED in the supplements. The mechanics are pinned engine-side
// (VanguardRuleIntegrationTests, ReinforcementRuleIntegrationTests, SpawnRuleIntegrationTests,
// SplitRuleIntegrationTests); this pins that the authored JSON carries the 3.6.0 shapes and text.
[TestFixture]
public class DeploymentDistancesShippedDataTests
{
    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static SpecialRuleDefinition Load(string file, string name) =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(Path.Combine(BooksDirectory, file)))
            .Single(r => r.Name == name);

    // Volcanic Dwarves' Drakesworn is word for word Vanguard's 3.6 text, so it is Vanguard's shape: a placement
    // with the non-Vanguard enemy clause (it was a 9" triggered move, the pre-#426 Vanguard).
    [Test]
    public void Drakesworn_IsThe36VanguardPlacement()
    {
        SpecialRuleDefinition rule = Load("AofRuleSupplement.json", "Drakesworn");

        Assert.That(rule.Description, Is.EqualTo(CoreRuleCatalog.Vanguard.Description));
        ActivatedAbility ability = rule.Activated.Single();
        Assert.That(ability.TriggerHook, Is.EqualTo(EHookID.Deployment_OnUnitDeployed));
        Assert.That(ability.Cost, Is.InstanceOf<Cost.OncePerGame>());
        Assert.That(ability.AvailableWhen, Is.InstanceOf<Condition.MostModelsHaveThisRule>());
        Assert.That(ability.Effect, Is.EqualTo(CoreRuleCatalog.Vanguard.Activated.Single().Effect),
            "the same RepositionOnDeploy(9, 9, \"Vanguard\") the catalog Vanguard carries.");
    }

    [Test]
    public void Reinforcement_Spawn_Split_CarryThe36Text()
    {
        Assert.That(Load("GdfRuleSupplement.json", "Reinforcement").Description, Is.EqualTo(
            "When a unit where all models have this rule is Shaken, you may remove it from the table as destroyed. " +
            "At the beginning of the next round after a unit where all models have this rule is destroyed, after " +
            "Ambushers have been deployed, you may place a new copy of it fully within 9\" of any table edge, over " +
            "9\" away from enemy units. Units that deploy via Reinforcement can't seize or contest objectives on " +
            "the round they deploy, and this rule doesn't apply to the new copy of the unit."));
        Assert.That(Load("GdfRuleSupplement.json", "Spawn").Description, Does.EndWith(
            "Units placed via Spawn can't seize or contest objectives on the round they are placed."));
        Assert.That(Load("GdfRuleSupplement.json", "Split").Description, Does.EndWith(
            "Units placed via Split can't seize or contest objectives on the round they are placed."));
    }
}
