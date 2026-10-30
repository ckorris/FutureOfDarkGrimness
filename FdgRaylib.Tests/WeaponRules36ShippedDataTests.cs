using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FDG;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #431 - the OPR 3.6 weapon-rule family as SHIPPED in GdfRuleSupplement.json (the AoF supplement inherits the
// GDF names). The mechanics are pinned engine-side by WeaponRules36SaveRuleIntegrationTests,
// WeaponRules36ApRuleIntegrationTests, ScourgeRuleIntegrationTests, CrossingStrikeRuleIntegrationTests and
// UnwieldyRuleIntegrationTests, against hand-built copies of these definitions (WeaponRules36Definitions); this
// fixture pins that the authored JSON is exactly those copies - entry for entry - and carries the 3.6.0 book text
// verbatim, so the engine tests and the data cannot drift apart.
[TestFixture]
public class WeaponRules36ShippedDataTests
{
    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static List<SpecialRuleDefinition> Load(string fileName) =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(Path.Combine(BooksDirectory, fileName)));

    private static SpecialRuleDefinition Gdf(string name) => Load("GdfRuleSupplement.json").Single(r => r.Name == name);

    private static readonly Condition Always = new Condition.Always();
    private static readonly HookEntry IgnoreRegeneration = new(EHookID.Shooting_OnSaveRollComplete, Always,
        new Effect.IgnoreRule("Regeneration"), ELifetime.ThisAttack);
    private static readonly HookEntry IgnoreCover = new(EHookID.Shooting_OnSaveRollModifier, Always,
        new Effect.IgnoreCover(), ELifetime.ThisAttack);

    private static HookEntry Ap(Condition condition, int ap) => new(EHookID.Shooting_OnHitRollComplete, condition,
        new Effect.RollModifier(ERollKind.Save, -ap), ELifetime.ThisAttack);

    private static HookEntry Reroll(Condition condition, int minValue) => new(EHookID.Shooting_OnSaveRollComplete,
        condition, new Effect.Reroll(ERollKind.Save, new RerollCondition.OnUnmodifiedValue(minValue)),
        ELifetime.ThisAttack);

    // name, the 3.6.0 book text, the expected passive entries.
    private static IEnumerable<TestCaseData> WeaponRules()
    {
        yield return Case("Anti-Cover", "This weapon ignores Cover.", IgnoreCover);
        yield return Case("Anti-Cover when Shooting", "This model gets Anti-Cover on its weapons when shooting.",
            IgnoreCover);
        yield return Case("Anti-Regen", "This weapon ignores Regeneration.", IgnoreRegeneration);
        yield return Case("Smash",
            "When attacking units over half of this weapon's range away, the target must re-roll unmodified Defense " +
            "results of 6.",
            Reroll(new Condition.TargetOverHalfWeaponRange(), 6));
        yield return Case("Pierce",
            "Ignores all positive Defense modifiers on the target, and when attacking units over half of this " +
            "weapon's range away, this weapon gets AP(+1).",
            IgnoreCover,
            new HookEntry(EHookID.Shooting_OnHitRollComplete, Always, new Effect.IgnorePositiveDefenseModifiers(),
                ELifetime.ThisAttack),
            Ap(new Condition.TargetOverHalfWeaponRange(), 1));
        yield return Case("Fracture", "For each unmodified result of 6 to hit, those hits get AP(+2).",
            new HookEntry(EHookID.Shooting_OnHitRollComplete, new Condition.UnmodifiedRollEquals(6),
                new Effect.PerHitSaveModifier(6, -2), ELifetime.ThisAttack));
        yield return Case("Fragment",
            "When attacking units where most models have Defense 5+ to Defense 6+, the target must re-roll " +
            "successful unmodified Defense results of 5-6.",
            Reroll(new Condition.TargetMostModelsDefenseInRange(5, 6), 5));
        yield return Case("Puncture",
            "Ignores Regeneration, and when attacking units where most models have Defense 2+ to Defense 3+, this " +
            "weapon gets AP(+2).",
            IgnoreRegeneration, Ap(new Condition.TargetMostModelsDefenseInRange(2, 3), 2));
        yield return Case("Obliterate",
            "Ignores Regeneration, and when attacking units where most models have Tough(3) or higher, the target " +
            "must re-roll successful unmodified Defense results of 5-6.",
            IgnoreRegeneration, Reroll(new Condition.TargetMajorityHasTough(3), 5));
        yield return Case("Decimate",
            "When attacking units with more than half of their total size or Tough value (for single-model units), " +
            "the target must re-roll unmodified Defense results of 6.",
            Reroll(new Condition.TargetAboveHalfStrength(), 6));
        yield return Case("Destructive",
            "When attacking units with more than half of their total size or Tough value (for single-model units), " +
            "those hits get AP(+1).",
            Ap(new Condition.TargetAboveHalfStrength(), 1));
        yield return Case("Disintegrate",
            "Ignores Regeneration, and when attacking units where most models have Tough(3) or higher, this weapon " +
            "gets AP(+2).",
            IgnoreRegeneration, Ap(new Condition.TargetMajorityHasTough(3), 2));
        yield return Case("Demolish",
            "Ignores Cover, and when attacking units with most models within 1\" of terrain, this weapon gets AP(+1).",
            IgnoreCover, Ap(new Condition.TargetMostModelsWithinInchesOfTerrain(1f), 1));
        yield return Case("Purge", "When attacking units that are Shaken or Fatigued, this weapon gets AP(+2).",
            Ap(new Condition.TargetShakenOrFatigued(), 2));
        yield return Case("Reap",
            "When attacking units that are Shaken or Fatigued, the target must re-roll successful unmodified Defense " +
            "results of 5-6.",
            Reroll(new Condition.TargetShakenOrFatigued(), 5));
        yield return Case("Scourge",
            "For each unmodified result of 1 to block hits, enemies count as having taken 2 extra wounds when " +
            "checking who won melee, or if shooting wounds would trigger Morale Tests.",
            new HookEntry(EHookID.Shooting_OnSaveRollComplete, Always, new Effect.CountsAsWoundsTaken(1, 2),
                ELifetime.ThisAttack));
        yield return Case("Unwieldy", "Strikes last when charging.",
            new HookEntry(EHookID.Melee_OnCounterTrigger, Always, new Effect.StrikeLast(), ELifetime.ThisAttack));
        yield return Case("Imprecise", "Gets -1 to hit when attacking.",
            new HookEntry(EHookID.Shooting_OnHitRollModifier, Always, new Effect.RollModifier(ERollKind.Hit, -1),
                ELifetime.ThisAttack));
    }

    private static TestCaseData Case(string name, string text, params HookEntry[] entries) =>
        new TestCaseData(name, text, entries).SetArgDisplayNames(name);

    [TestCaseSource(nameof(WeaponRules))]
    public void TheShippedDefinition_IsTheTestedShape_WithThe36Text(string name, string text, HookEntry[] entries)
    {
        SpecialRuleDefinition rule = Gdf(name);

        Assert.That(rule.Scope, Is.EqualTo(ERuleScope.Weapon), "every one of these is printed on weapons");
        Assert.That(rule.EngineArgumentCount, Is.EqualTo(0));
        Assert.That(rule.Activated, Is.Empty);
        Assert.That(rule.Passive, Is.EqualTo(entries), "entry for entry - hook, condition, effect, lifetime, seat");
        Assert.That(rule.Description, Is.EqualTo(text), "the 3.6.0 book text, verbatim");
        Assert.That(rule.Valence, Is.EqualTo(name is "Unwieldy" or "Imprecise" ? EValence.Negative : EValence.Positive));
    }

    [Test]
    public void CrossingStrike_IsStrafingsAbility_UnderThe36Name()
    {
        SpecialRuleDefinition crossing = Gdf("Crossing Strike");
        ActivatedAbility strafing = CoreRuleCatalog.Strafing.Activated.Single();

        Assert.That(crossing.Scope, Is.EqualTo(ERuleScope.Weapon));
        Assert.That(crossing.Passive, Is.Empty, "no fly-over permission - the carrier needs Flying or Aircraft");
        ActivatedAbility ability = crossing.Activated.Single();
        Assert.That(ability.TriggerHook, Is.EqualTo(strafing.TriggerHook));
        Assert.That(ability.Cost, Is.EqualTo(strafing.Cost), "once per activation");
        Assert.That(ability.TargetSelector, Is.EqualTo(strafing.TargetSelector));
        Assert.That(ability.Effect, Is.EqualTo(strafing.Effect), "attack with THIS weapon, as if shooting");
        Assert.That(crossing.Description, Is.EqualTo(
            "Once per activation, when this model moves through enemy units, pick one of them and attack it with " +
            "this weapon as if it was shooting. This weapon may only be used in this way."));

        var bombs = new Weapon("Flame Bombs", rangeInches: 0f, attacks: 1, armorPenetration: 0);
        bombs.AttachRuleDefinition(new ResolvedRule(crossing.Name, crossing));
        Assert.That(StrafingRules.IsStrafeOnly(bombs), Is.True,
            "'This weapon may only be used in this way' - the supplement copy is restricted like the catalog rule");
    }

    [TestCase("Anti-Cover Aura")]
    [TestCase("Anti-Cover when Shooting Aura")]
    public void TheAntiCoverAuras_GrantAntiCoverWhenShooting(string name)
    {
        SpecialRuleDefinition aura = Gdf(name);

        HookEntry entry = aura.Passive.Single();
        Assert.That(aura.Scope, Is.EqualTo(ERuleScope.Unit));
        Assert.That(entry.HookID, Is.EqualTo(EHookID.Lifecycle_OnUnitCreated));
        Assert.That(entry.Effect, Is.EqualTo(new Effect.Aura("Anti-Cover when Shooting")));
        Assert.That(aura.Description, Is.EqualTo("This model and its unit get Anti-Cover on its weapons when shooting."));
        Assert.That(Load("GdfRuleSupplement.json").Any(r => r.Name == "Anti-Cover when Shooting"), Is.True,
            "the granted rule exists under that exact name");
    }

    [Test]
    public void TheOld35ShapesOfDestructiveAndDisintegrate_AreGone()
    {
        // 3.5 Destructive was AP(+4) on natural 6s; 3.5 Disintegrate keyed on Defense 2+.
        Assert.That(Gdf("Destructive").Passive.Any(e => e.Condition is Condition.UnmodifiedRollEquals), Is.False);
        Assert.That(Gdf("Disintegrate").Passive.Any(e => e.Condition is Condition.StatGreaterOrEqualTo), Is.False);
    }

    [Test]
    public void TheAofSupplement_InheritsTheseNames_RatherThanRedefiningThem()
    {
        string[] names =
        {
            "Anti-Cover", "Anti-Regen", "Smash", "Pierce", "Fracture", "Fragment", "Puncture", "Obliterate",
            "Decimate", "Destructive", "Disintegrate", "Demolish", "Purge", "Reap", "Scourge", "Unwieldy",
            "Imprecise", "Crossing Strike",
        };

        List<SpecialRuleDefinition> aof = Load("AofRuleSupplement.json");
        Assert.That(aof.Where(r => names.Contains(r.Name)).Select(r => r.Name), Is.Empty);
    }

    [Test]
    public void TheMergedSupplements_StillValidateForAuthoring()
    {
        var validator = new RuleValidator();
        foreach (SpecialRuleDefinition rule in Load("GdfRuleSupplement.json").Where(r => r.Name is "Pierce"
                     or "Scourge" or "Demolish" or "Fragment" or "Puncture" or "Decimate" or "Purge" or "Smash"))
        {
            Assert.That(validator.ValidateAuthoring(rule), Is.Empty, rule.Name);
        }
    }
}
