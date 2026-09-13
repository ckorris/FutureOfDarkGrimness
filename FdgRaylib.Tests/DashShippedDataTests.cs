using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #197 Dash - the shipped data (6 refs, all in Custodian Brothers via the Envoy Banner option: 2 base +
// 4 Aura). The trigger, the no-Yes/No prompting and the all-models gate are pinned by the engine's
// DashRuleIntegrationTests; these pin the JSON, where the failure modes are silent ones - a rule authored
// at the wrong hook still validates and lints, and an aura pointing at nothing leaves 4 of the 6 refs dead
// while the other 2 keep working.
[TestFixture]
public class DashShippedDataTests
{
    private const string RuleName = "Dash";
    private const string AuraName = "Dash Aura";

    private static IReadOnlyList<SpecialRuleDefinition> Supplement() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Books", "GdfRuleSupplement.json")));

    [Test]
    public void Dash_IsAnEndOfActivationRepositionOfD3PlusOne_OncePerRound()
    {
        ActivatedAbility ability = Supplement().Single(r => r.Name == RuleName).Activated.Single();

        Assert.That(ability.TriggerHook, Is.EqualTo(EHookID.Activation_OnEndOfActivation),
            "'when a unit ... ends its activation' - authored at the activation-START hook it would still " +
            "validate and lint, and would silently be Bounding");
        Assert.That(ability.Cost, Is.InstanceOf<Cost.OncePerRound>(), "'once per round'");
        Assert.That(ability.AvailableWhen, Is.InstanceOf<Condition.AllModelsHaveThisRule>(),
            "#267: a unit-wide reposition must gate on all models, or one joined hero moves the squad");

        var effect = (Effect.RepositionAtActivation)ability.Effect;
        Assert.That(effect.Distance, Is.InstanceOf<DiceExpression.D3>());
        Assert.That(effect.PlusInches, Is.EqualTo(1), "'within D3+1 inches'");
    }

    [Test]
    public void TheAura_ConfersTheRuleItself()
    {
        HookEntry entry = Supplement().Single(r => r.Name == AuraName).Passive.Single();

        Assert.That(entry.Effect, Is.InstanceOf<Effect.Aura>());
        Assert.That(((Effect.Aura)entry.Effect).RuleName, Is.EqualTo(RuleName),
            "4 of the 6 corpus references are the Aura form - a broken link leaves them dead");
    }

    [Test]
    public void EveryBookReferencingIt_EmbedsTheDefinitions()
    {
        string booksDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Books");
        List<string> missing = new List<string>();

        foreach (string path in Directory.GetFiles(booksDir, "*.fdgbook"))
        {
            string json = File.ReadAllText(path);
            bool references = json.Contains($"\"name\": \"{RuleName}\"", StringComparison.Ordinal)
                || json.Contains($"\"name\": \"{AuraName}\"", StringComparison.Ordinal);
            if (!references) continue;

            if (!json.Contains("\"repositionAtActivation\"", StringComparison.Ordinal))
            {
                missing.Add(Path.GetFileName(path));
            }
        }

        Assert.That(missing, Is.Empty,
            "books referencing Dash without the embedded definition: " + string.Join(", ", missing));
    }
}
