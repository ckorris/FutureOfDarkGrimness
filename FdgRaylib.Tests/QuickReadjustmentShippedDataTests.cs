using System;
using System.IO;
using System.Linq;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #197 misc - Quick Readjustment, between #425 and #436. 3.5 authored it as a weapon-scoped +1 to hit after moving
// on an Indirect weapon, to cancel Indirect's -1. Core Rules v3.6.0 Indirect has no such penalty (#425), so that +1
// would be a free buff; the rule is dropped in 3.6 and #436 deletes the entry. Until then it ships inert - the
// engine's QuickReadjustmentRuleIntegrationTests show why (the old shape nets +1 against 3.6 Indirect).
[TestFixture]
public class QuickReadjustmentShippedDataTests
{
    [Test]
    public void QuickReadjustment_ShipsInert_UntilItIsDeleted()
    {
        SpecialRuleDefinition rule = BookRuleSupplement.LoadDefinitions(File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Assets", "Books", "GdfRuleSupplement.json")))
            .Single(r => r.Name == "Quick Readjustment");

        Assert.That(rule.Scope, Is.EqualTo(ERuleScope.Weapon), "still routed onto weapons, so old lists keep loading.");
        Assert.That(rule.Passive, Is.Empty, "no +1: 3.6 Indirect has no after-moving penalty to cancel.");
        Assert.That(rule.Activated, Is.Empty);
    }
}
