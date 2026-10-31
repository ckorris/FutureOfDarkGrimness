using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #197 P17c - the shipped Reinforcement data (4 refs, Ratmen Clans). The mechanism (both trigger arms,
// the spent gate's double-prompt prevention, the copy-in-reserve, the mandatory band arrival) is pinned
// by the engine's ReinforcementRuleIntegrationTests; these pin the JSON - above all that BOTH entries
// gate on the SAME spent token, since a Shaken-arm accept kills the original onto the destruction seam
// where an ungated destroyed-arm entry would re-prompt.
[TestFixture]
public class ReinforcementShippedDataTests
{
    private const string RuleName = "Reinforcement";

    private static SpecialRuleDefinition Definition() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Assets", "Books", "GdfRuleSupplement.json")))
            .Single(r => r.Name == RuleName);

    [Test]
    public void BothArms_AreAuthored_AndShareTheSpentGate()
    {
        SpecialRuleDefinition definition = Definition();
        Assert.That(definition.Passive, Has.Count.EqualTo(2));
        Assert.That(definition.Passive.Select(e => e.HookID), Is.EquivalentTo(new[]
        {
            EHookID.Lifecycle_OnSelfDestroyed,   // 'or fully destroyed' - killer-less, so routs count
            EHookID.Morale_OnShakenApplied,      // 'is Shaken'
        }));

        foreach (HookEntry entry in definition.Passive)
        {
            Assert.That(entry.Effect, Is.InstanceOf<Effect.ReinforceUnit>());
            var gate = (Condition.And)entry.Condition;
            Assert.That(gate.Left, Is.InstanceOf<Condition.AllModelsHaveThisRule>(),
                "'a unit where all models have this rule'");
            var not = (Condition.Not)gate.Right;
            Assert.That(((Condition.TokenPresent)not.Inner).TType,
                Is.EqualTo(TokenType.ReinforcementSpent),
                "both arms must gate on the SAME spent token or the Shaken-arm's kill re-prompts");
        }
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
            if (!json.Contains("\"reinforceUnit\"", StringComparison.Ordinal))
            {
                missing.Add(Path.GetFileName(path));
            }
        }

        Assert.That(missing, Is.Empty,
            "books referencing Reinforcement without its embedded definition: " + string.Join(", ", missing));
    }
}
