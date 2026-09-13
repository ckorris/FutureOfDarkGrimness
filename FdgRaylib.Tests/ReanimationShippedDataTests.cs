using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #197 P17d - the shipped Reanimation data (3 refs, all the Aura form in Robot Legions). The restore
// mechanism (decisive pool, wounds-first, auto-placed revives) is pinned by the engine's
// ReanimationRuleIntegrationTests; these pin the JSON - the base rule's shape, the aura link (all
// corpus refs are the Aura, so a broken link makes the whole rule unreachable), and the embedding.
[TestFixture]
public class ReanimationShippedDataTests
{
    private const string RuleName = "Reanimation";
    private const string AuraName = "Reanimation Aura";

    private static IReadOnlyList<SpecialRuleDefinition> Supplement() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Books", "GdfRuleSupplement.json")));

    [Test]
    public void Reanimation_IsAnActivationStartRestore_AtFivePlus()
    {
        HookEntry entry = Supplement().Single(r => r.Name == RuleName).Passive.Single();

        Assert.That(entry.HookID, Is.EqualTo(EHookID.Activation_OnActivationStart),
            "'when a unit ... is activated'");
        Assert.That(entry.Condition, Is.InstanceOf<Condition.AllModelsHaveThisRule>());
        Assert.That(((Effect.RestoreWounds)entry.Effect).MinRoll, Is.EqualTo(5), "'for each 5+'");
    }

    [Test]
    public void TheAura_ConfersTheRuleItself()
    {
        HookEntry entry = Supplement().Single(r => r.Name == AuraName).Passive.Single();

        Assert.That(entry.Effect, Is.InstanceOf<Effect.Aura>());
        Assert.That(((Effect.Aura)entry.Effect).RuleName, Is.EqualTo(RuleName),
            "every corpus reference is the Aura form - a broken link makes the rule unreachable");
    }

    [Test]
    public void EveryBookReferencingIt_EmbedsBothDefinitions()
    {
        string booksDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Books");
        List<string> missing = new List<string>();

        foreach (string path in Directory.GetFiles(booksDir, "*.fdgbook"))
        {
            string json = File.ReadAllText(path);
            if (!json.Contains($"\"name\": \"{AuraName}\"", StringComparison.Ordinal)) continue;
            if (!json.Contains("\"restoreWounds\"", StringComparison.Ordinal))
            {
                missing.Add(Path.GetFileName(path));
            }
        }

        Assert.That(missing, Is.Empty,
            "books referencing Reanimation Aura without the embedded base rule: " + string.Join(", ", missing));
    }
}
