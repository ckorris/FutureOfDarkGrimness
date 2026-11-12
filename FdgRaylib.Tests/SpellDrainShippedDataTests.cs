using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using FdgRaylib.Import;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #429 - Spell Drain, new in 3.6 (GDF Blessed Sisters, Daemons of Change, Human Inquisition; AoF Beastmen, Daemons
// of Change, Sky-City Dwarves): "Once per activation, before attacking, pick one enemy model within 18" in line of
// sight, which must take a Morale Test. If failed, it loses 2 spell tokens." Authored once in the shared GDF
// supplement - the AoF text is identical, so the AoF bake reaches it through the GDF-then-AoF merge. The mechanism
// is pinned engine-side by SpellDrainRuleIntegrationTests through the real BeforeAttackActionStage.
[TestFixture]
public class SpellDrainShippedDataTests
{
    private static string BooksDir => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static SpecialRuleDefinition GdfDefinition() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(Path.Combine(BooksDir, "GdfRuleSupplement.json")))
            .Single(r => r.Name == "Spell Drain");

    [Test]
    public void SpellDrain_IsTheOncePerActivationPreAttackMoraleTestShape()
    {
        SpecialRuleDefinition definition = GdfDefinition();
        ActivatedAbility ability = definition.Activated.Single();

        Assert.That(definition.Passive, Is.Empty);
        Assert.That(ability.TriggerHook, Is.EqualTo(EHookID.Activation_OnBeforeAttackAction),
            "'once per activation, before attacking'");
        Assert.That(ability.Cost, Is.InstanceOf<Cost.OncePerActivation>());
        Assert.That(ability.TargetSelector.RangeInches, Is.EqualTo(18f).Within(0.001f));
        Assert.That(ability.TargetSelector.MinCount, Is.EqualTo(1));
        Assert.That(ability.TargetSelector.MaxCount, Is.EqualTo(1), "'pick ONE enemy model'");
        Assert.That(ability.TargetSelector.TargetAffinity, Is.EqualTo(ETargetAffinity.Foe));
        Assert.That(ability.TargetSelector.RequireLineOfSight, Is.True, "'in line of sight'");
        Assert.That(ability.TargetSelector.RequiredToken, Is.Null,
            "any enemy model, as written - not only a Caster (D6 in WorkItems/429)");
    }

    [Test]
    public void SpellDrain_OnAFailedTest_DrainsTwoSpellTokens()
    {
        var conditional = (Effect.MoraleTestThen)GdfDefinition().Activated.Single().Effect;
        var drain = conditional.OnFailure as Effect.ConsumeToken;

        Assert.That(drain, Is.Not.Null, "'If failed, it loses 2 spell tokens'");
        Assert.That(drain!.TType, Is.EqualTo(TokenType.SpellTokens));
        Assert.That(drain.Count, Is.EqualTo(2));
    }

    [Test]
    public void SpellDrain_CarriesTheCore360BookWording_InAscii()
    {
        string description = GdfDefinition().Description;

        Assert.That(description, Is.EqualTo("Once per activation, before attacking, pick one enemy model within 18\" in " +
            "line of sight, which must take a Morale Test. If failed, it loses 2 spell tokens."));
        Assert.That(description.All(c => c < 128), Is.True, "game text is ASCII-only");
    }

    [Test]
    public void TheAofBake_SeesTheSameDefinition_ThroughTheSupplementMerge()
    {
        List<SpecialRuleDefinition> merged = RuleSupplementSet.LoadMerged(
            RuleSupplementSet.BundledFileNames.Select(name => Path.Combine(BooksDir, name)));

        SpecialRuleDefinition aof = merged.Single(r => r.Name == "Spell Drain");
        Assert.That(aof.Description, Is.EqualTo(GdfDefinition().Description),
            "the AoF text is identical, so the AoF file does not redefine it");
    }
}
