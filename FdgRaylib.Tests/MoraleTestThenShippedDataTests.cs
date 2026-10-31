using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #197 - the shipped Mind Control (4 refs: Jackals, Soul-Snatcher Cults, Wormhole Daemons of Lust) and
// Fatigue Debuff (3 refs: Wormhole Daemons of War). Both are the P6 pre-attack shape - "once per
// activation, before attacking, pick one enemy unit within 18\" in line of sight" - with a morale test
// gating the consequence. The mechanism is pinned engine-side by MoraleTestThenRuleIntegrationTests.
[TestFixture]
public class MoraleTestThenShippedDataTests
{
    private static IReadOnlyList<SpecialRuleDefinition> Supplement() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Books", "GdfRuleSupplement.json")));

    private static ActivatedAbility Ability(string name) =>
        Supplement().Single(r => r.Name == name).Activated.Single();

    [TestCase("Mind Control")]
    [TestCase("Fatigue Debuff")]
    public void BothAre_OncePerActivation_PreAttack_PickOneEnemyWithinEighteenInLineOfSight(string name)
    {
        ActivatedAbility ability = Ability(name);

        Assert.That(ability.TriggerHook, Is.EqualTo(EHookID.Activation_OnBeforeAttackAction),
            "'once per activation, before attacking'");
        Assert.That(ability.Cost, Is.InstanceOf<Cost.OncePerActivation>());
        Assert.That(ability.TargetSelector.RangeInches, Is.EqualTo(18f).Within(0.001f));
        Assert.That(ability.TargetSelector.MaxCount, Is.EqualTo(1), "'pick ONE enemy unit'");
        Assert.That(ability.TargetSelector.TargetAffinity, Is.EqualTo(ETargetAffinity.Foe));
        Assert.That(ability.TargetSelector.RequireLineOfSight, Is.True, "'in line of sight'");
        Assert.That(ability.Effect, Is.InstanceOf<Effect.MoraleTestThen>(),
            "'which must take a morale test' - the whole point of the rule is the conditional");
    }

    [Test]
    public void MindControl_MovesTheVictimUpToSixInches_Optionally()
    {
        var conditional = (Effect.MoraleTestThen)Ability("Mind Control").Effect;
        var move = conditional.OnFailure as Effect.TriggeredMove;

        Assert.That(move, Is.Not.Null, "'if failed you MAY move it by up to 6\"'");
        Assert.That(move!.MaxInches, Is.EqualTo(6f).Within(0.001f));
        Assert.That(move.IsOptional, Is.True,
            "'you may' - a mandatory move would force the controller to shove the victim every time");
    }

    [Test]
    public void FatigueDebuff_FatiguesTheVictim()
    {
        var conditional = (Effect.MoraleTestThen)Ability("Fatigue Debuff").Effect;

        Assert.That(conditional.OnFailure, Is.InstanceOf<Effect.ApplyFatigue>(),
            "'if failed, it becomes fatigued'");
    }

    [Test]
    public void EveryBookReferencingThem_EmbedsTheDefinition()
    {
        string booksDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Books");
        List<string> missing = new List<string>();

        foreach (string path in Directory.GetFiles(booksDir, "*.fdgbook"))
        {
            string json = File.ReadAllText(path);
            foreach (string name in new[] { "Mind Control", "Fatigue Debuff" })
            {
                if (!json.Contains($"\"name\": \"{name}\"", StringComparison.Ordinal)) continue;
                if (!json.Contains("\"moraleTestThen\"", StringComparison.Ordinal))
                {
                    missing.Add($"{Path.GetFileName(path)} ({name})");
                }
            }
        }

        Assert.That(missing, Is.Empty,
            "books referencing the rule without the embedded definition: " + string.Join(", ", missing));
    }
}
