using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #197 misc - Quick Readjustment. The engine's QuickReadjustmentRuleIntegrationTests prove the net effect;
// this pins that the shipped JSON authors it weapon-scoped (so slice-0 routing lands it on each weapon) and
// gates the +1 on WeaponHasRule("Indirect") - drop that gate and it becomes a blanket after-moving hit
// bonus on every weapon, which neither --validate-rules nor RuleFireLint would catch.
[TestFixture]
public class QuickReadjustmentShippedDataTests
{
    [Test]
    public void QuickReadjustment_IsWeaponScoped_AndGatesThePlusOneOnIndirectAfterMoving()
    {
        SpecialRuleDefinition rule = BookRuleSupplement.LoadDefinitions(File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Assets", "Books", "GdfRuleSupplement.json")))
            .Single(r => r.Name == "Quick Readjustment");

        Assert.That(rule.Scope, Is.EqualTo(ERuleScope.Weapon),
            "weapon-scoped so slice-0 routes it onto each of the unit's weapons, one instance per weapon.");

        HookEntry entry = rule.Passive.Single();
        Assert.That(entry.HookID, Is.EqualTo(EHookID.Shooting_OnHitRollModifier));

        // The condition tree must contain WeaponHasRule("Indirect"), AfterMoving, and Not(IsMelee).
        List<Condition> flat = Flatten(entry.Condition).ToList();
        Assert.That(flat.OfType<Condition.WeaponHasRule>().Any(w => w.RuleName == "Indirect"), Is.True,
            "the +1 fires only on the weapon that also carries Indirect.");
        Assert.That(flat.OfType<Condition.AfterMoving>().Any(), Is.True, "only after moving.");
        Assert.That(flat.OfType<Condition.Not>().Any(n => n.Inner is Condition.IsMelee), Is.True,
            "shooting only.");

        Assert.That(entry.Effect, Is.InstanceOf<Effect.RollModifier>());
        var mod = (Effect.RollModifier)entry.Effect;
        Assert.That(mod.RollKind, Is.EqualTo(ERollKind.Hit));
        Assert.That(mod.Delta, Is.EqualTo(1), "+1 to cancel Indirect's -1.");
    }

    private static IEnumerable<Condition> Flatten(Condition c)
    {
        yield return c;
        switch (c)
        {
            case Condition.And and:
                foreach (Condition x in Flatten(and.Left)) yield return x;
                foreach (Condition x in Flatten(and.Right)) yield return x;
                break;
            case Condition.Or or:
                foreach (Condition x in Flatten(or.Left)) yield return x;
                foreach (Condition x in Flatten(or.Right)) yield return x;
                break;
            case Condition.Not not:
                foreach (Condition x in Flatten(not.Inner)) yield return x;
                break;
        }
    }
}
