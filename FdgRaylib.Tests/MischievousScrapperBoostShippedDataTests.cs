using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #197 - the shipped Mischievous Boost / Scrapper Boost data (6 dead refs: Mischievous Boost Aura 4 in
// Goblin Reclaimers, Scrapper Boost Aura 2 in Jackals; both base Boosts are also granted by each book's
// spell, which is why the base rule ships even though only the Aura registered as dead).
//
// The threshold mechanism is pinned engine-side by RerollThresholdRuleIntegrationTests. What is pinned
// here is the authoring, whose failure modes are all SILENT: a minValue that fails to deserialize falls
// back to 6, and the rule then validates, lints and plays exactly like its own base rule - i.e. the Boost
// looks installed and does nothing.
[TestFixture]
public class MischievousScrapperBoostShippedDataTests
{
    private static IReadOnlyList<SpecialRuleDefinition> Supplement() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Books", "GdfRuleSupplement.json")));

    [TestCase("Mischievous")]
    [TestCase("Scrapper")]
    public void TheBoost_WidensTheSaveRerollToFiveSix_BeyondNineInches(string family)
    {
        HookEntry entry = Supplement().Single(r => r.Name == $"{family} Boost").Passive.Single();

        Assert.That(entry.HookID, Is.EqualTo(EHookID.Shooting_OnSaveRollComplete));
        Assert.That(entry.Seat, Is.EqualTo(ERuleSeat.Actor),
            "the ATTACKER carries it - on the Subject seat it would boost the defender it is meant to hurt");

        var gate = entry.Condition as Condition.AttackedFromOverInches;
        Assert.That(gate, Is.Not.Null, "'when it shoots or charges enemies over 9\" away'");
        Assert.That(gate!.DistanceInches, Is.EqualTo(9f).Within(0.001f));

        var reroll = (Effect.Reroll)entry.Effect;
        Assert.That(reroll.Roll, Is.EqualTo(ERollKind.Save));
        var condition = reroll.Condition as RerollCondition.OnUnmodifiedValue;
        Assert.That(condition, Is.Not.Null);
        Assert.That(condition!.MinValue, Is.EqualTo(5),
            "'re-roll successful unmodified defense results of 5-6' - a silent fallback to the default 6 " +
            "would validate, lint and play exactly like the unboosted base rule");
    }

    [TestCase("Mischievous")]
    [TestCase("Scrapper")]
    public void TheBoost_IsWeaponScoped_LikeItsBase(string family)
    {
        IReadOnlyList<SpecialRuleDefinition> supplement = Supplement();

        Assert.That(supplement.Single(r => r.Name == $"{family} Boost").Scope,
            Is.EqualTo(supplement.Single(r => r.Name == family).Scope),
            "the Boost must sit where its base sits, or the two never meet in the same reroll fold");
    }

    [TestCase("Mischievous")]
    [TestCase("Scrapper")]
    public void TheAura_ConfersTheBoost(string family)
    {
        HookEntry entry = Supplement().Single(r => r.Name == $"{family} Boost Aura").Passive.Single();

        Assert.That(((Effect.Aura)entry.Effect).RuleName, Is.EqualTo($"{family} Boost"),
            "every dead reference is the Aura form - a broken link leaves all of them dead");
    }

    [Test]
    public void TheBaseRules_StillRerollOnlySixes()
    {
        // The pair only composes correctly because the base states 6 and the Boost states the full 5-6,
        // folded by minimum. If the base were ever re-authored to 5 the Boost would become a no-op.
        foreach (string family in new[] { "Mischievous", "Scrapper" })
        {
            HookEntry entry = Supplement().Single(r => r.Name == family).Passive.Single();
            var condition = (RerollCondition.OnUnmodifiedValue)((Effect.Reroll)entry.Effect).Condition;
            Assert.That(condition.MinValue, Is.EqualTo(6), $"{family} re-rolls unmodified 6s");
        }
    }

    [Test]
    public void EveryBookReferencingTheAuras_EmbedsTheDefinitions()
    {
        string booksDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Books");
        List<string> missing = new List<string>();

        foreach (string path in Directory.GetFiles(booksDir, "*.fdgbook"))
        {
            string json = File.ReadAllText(path);
            foreach (string family in new[] { "Mischievous", "Scrapper" })
            {
                if (!json.Contains($"\"name\": \"{family} Boost Aura\"", StringComparison.Ordinal)) continue;
                if (!json.Contains($"\"name\": \"{family} Boost\"", StringComparison.Ordinal))
                {
                    missing.Add($"{Path.GetFileName(path)} ({family})");
                }
            }
        }

        Assert.That(missing, Is.Empty,
            "books referencing a Boost Aura without the embedded Boost: " + string.Join(", ", missing));
    }
}
