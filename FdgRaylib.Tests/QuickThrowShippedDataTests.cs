using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FdgRaylib.Import;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #425 - Quick Throw and the supplement's Indirect clone, as SHIPPED. The mechanics are pinned engine-side
// (QuickThrowRuleIntegrationTests, IndirectLineOfSightRuleIntegrationTests) against hand-built copies; this pins that
// the authored JSON is exactly those copies and carries the 3.6.0 text.
[TestFixture]
public class QuickThrowShippedDataTests
{
    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static SpecialRuleDefinition Gdf(string name) =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(Path.Combine(BooksDirectory, "GdfRuleSupplement.json")))
            .Single(r => r.Name == name);

    [Test]
    public void QuickThrow_LiftsOnlyTheLineOfSightGate_OnAnIndirectWeapon()
    {
        SpecialRuleDefinition rule = Gdf("Quick Throw");

        Assert.That(rule.Scope, Is.EqualTo(ERuleScope.Weapon),
            "weapon-scoped: routed onto the carrying model's weapons ('This model ...').");
        Assert.That(rule.Description, Is.EqualTo(
            "This model may always target enemies that are not in line of sight when shooting with Indirect weapons " +
            "(instead of only when using Hold actions)."));
        HookEntry entry = rule.Passive.Single();
        Assert.That(entry.HookID, Is.EqualTo(EHookID.Shooting_OnSaveRollModifier));
        Assert.That(entry.Condition, Is.EqualTo(new Condition.WeaponHasRule("Indirect")),
            "no move condition ('always'), only on the weapon that is Indirect.");
        Assert.That(entry.Effect, Is.InstanceOf<Effect.IgnoreLineOfSight>(),
            "targeting only - never the Sight-Blocker-cover bypass, which stays Hold-only.");
        Assert.That(rule.Activated, Is.Empty);
    }

    // The AoF books that name it (Dragon Empire, Ratmen, Duchies of Vinci, Dwarves) bake against GDF + AoF merged
    // (RuleSupplementSet); the text is identical, so the GDF definition is the one that embeds.
    [Test]
    public void QuickThrow_ResolvesForAoFBakes_FromTheMergedSupplements()
    {
        List<SpecialRuleDefinition> merged = RuleSupplementSet.LoadMerged(
            RuleSupplementSet.BundledFileNames.Select(name => Path.Combine(BooksDirectory, name)));

        Assert.That(merged.Count(r => r.Name == "Quick Throw"), Is.EqualTo(1));
        Assert.That(merged.Single(r => r.Name == "Quick Throw").Passive.Single().Effect,
            Is.InstanceOf<Effect.IgnoreLineOfSight>());
    }

    // "Indirect when Shooting Aura" (still in 3.6.0 books) grants the catalog's 3.6 Indirect since #436, which deleted
    // the supplement's re-authored "Indirect when shooting" (a 3.5 name) and re-pointed the aura at "Indirect".
    [Test]
    public void IndirectWhenShootingAura_GrantsThe36Indirect()
    {
        SpecialRuleDefinition aura = Gdf("Indirect when Shooting Aura");

        Assert.That(aura.Passive.Single().Effect, Is.EqualTo(new Effect.Aura("Indirect")));
        Assert.That(CoreRuleCatalog.CreateResolver().TryResolve("Indirect", out ResolvedRule indirect), Is.True);
        var hold = new Condition.Not(new Condition.AfterMoving());
        Assert.That(indirect.Definition.Passive.Select(p => (p.HookID, p.Condition, p.Effect.GetType())), Is.EquivalentTo(new[]
        {
            (EHookID.Shooting_OnSaveRollModifier, (Condition)hold, typeof(Effect.IgnoreLineOfSight)),
            (EHookID.Shooting_OnSaveRollModifier, (Condition)hold, typeof(Effect.IgnoreSightBlockerCover)),
        }), "Hold-only LoS and Sight-Blocker-cover bypass; no -1 after moving, no all-cover ignore.");
        Assert.That(BookRuleSupplement.LoadDefinitions(File.ReadAllText(Path.Combine(BooksDirectory, "GdfRuleSupplement.json")))
            .Any(r => r.Name == "Indirect when shooting"), Is.False);
    }
}
