using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #197 misc - Heavy Impact ("Impact(X) with hits that have AP(1)"). The engine's ImpactRuleIntegrationTests
// prove the AP threads through the charge-impact pipeline into the save roll; this pins that the shipped
// JSON authors the effect as ChargeImpactHits(Arg(0), AP 1) - a missing/zero AP would silently degrade it
// to core Impact, which --validate-rules and RuleFireLint cannot catch.
[TestFixture]
public class HeavyImpactShippedDataTests
{
    [Test]
    public void HeavyImpact_RollsArgDice_WithArmorPenetrationOne_OnChargeContact()
    {
        SpecialRuleDefinition rule = BookRuleSupplement.LoadDefinitions(File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Assets", "Books", "GdfRuleSupplement.json")))
            .Single(r => r.Name == "Heavy Impact");

        Assert.That(rule.EngineArgumentCount, Is.EqualTo(1), "Heavy Impact(X) - X is the dice count.");

        HookEntry entry = rule.Passive.Single();
        Assert.That(entry.HookID, Is.EqualTo(EHookID.Melee_OnChargeContact));
        Assert.That(entry.Effect, Is.InstanceOf<Effect.ChargeImpactHits>());

        var impact = (Effect.ChargeImpactHits)entry.Effect;
        Assert.That(impact.ArmorPenetration, Is.EqualTo(1),
            "the whole point of Heavy Impact: its hits carry AP(1). Zero would make it plain Impact.");
        Assert.That(impact.DiceCount, Is.InstanceOf<ValueSource.Arg>());
        Assert.That(((ValueSource.Arg)impact.DiceCount).Index, Is.EqualTo(0));
    }
}
