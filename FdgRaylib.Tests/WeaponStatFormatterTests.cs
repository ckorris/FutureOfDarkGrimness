using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// Weapon special-rule list shown wherever weapon stats appear (tooltip, shoot list, deploy panel, picker).
// Pure string join; the IWeapon overload just projects RequestedName onto this.
[TestFixture]
public class WeaponStatFormatterTests
{
    [Test]
    public void RuleList_EmptyWhenNoRules()
    {
        Assert.That(WeaponStatFormatter.RuleList(System.Array.Empty<string>()), Is.EqualTo(""));
    }

    [Test]
    public void RuleList_JoinsWithCommas()
    {
        Assert.That(WeaponStatFormatter.RuleList(new[] { "Rending" }), Is.EqualTo("Rending"));
        Assert.That(WeaponStatFormatter.RuleList(new[] { "Rending", "Blast(3)", "Deadly(3)" }),
            Is.EqualTo("Rending, Blast(3), Deadly(3)"));
    }

    [Test]
    public void RuleList_IsAscii()
    {
        string s = WeaponStatFormatter.RuleList(new[] { "Rending", "Blast(3)" });
        foreach (char c in s)
            Assert.That(c, Is.LessThanOrEqualTo((char)0x7F), $"non-ASCII char in \"{s}\"");
    }

    // #424: the compact range column reads the classification seam, so a weapon that is both ranged and
    // melee (Assault) says so.
    [Test]
    public void RangeLabel_ReadsTheSeam_RangedMeleeOrBoth()
    {
        var rifle = new FDG.Weapon("Rifle", 24f, 1, 0);
        var sword = new FDG.Weapon("Sword", 0f, 2, 0);
        var pistol = new FDG.Weapon("Heavy Pistol", 12f, 1, 1);
        pistol.AttachRuleDefinition(new FDG.Rules.Dispatch.ResolvedRule("Assault", AssaultTestRules.Assault));

        Assert.That(WeaponStatFormatter.RangeLabel(rifle, "melee"), Is.EqualTo("24\""));
        Assert.That(WeaponStatFormatter.RangeLabel(sword, "melee"), Is.EqualTo("melee"));
        Assert.That(WeaponStatFormatter.RangeLabel(sword, "Melee"), Is.EqualTo("Melee"), "the caller's word");
        Assert.That(WeaponStatFormatter.RangeLabel(pistol, "melee"), Is.EqualTo("12\"/melee"), "both, in one label");
        Assert.That(WeaponStatFormatter.RangeLabel(new FDG.Weapon("Long Rifle", 30.5f, 1, 0), "melee"), Is.EqualTo("30.5\""),
            "fractional ranges trim like every other range label");
    }
}

/// <summary>A weapon rule granting melee use - the shape the shipped Assault definition has - for the app's
/// presentation tests, without reading the supplement from disk in each.</summary>
internal static class AssaultTestRules
{
    public static FDG.Rules.Definitions.SpecialRuleDefinition Assault { get; } = new("Assault",
        new[]
        {
            new FDG.Rules.Definitions.HookEntry(FDG.Rules.Foundation.EHookID.Lifecycle_OnCapabilityQuery,
                new FDG.Rules.Definitions.Condition.Always(), new FDG.Rules.Definitions.Effect.EnableMeleeUse(),
                FDG.Rules.Foundation.ELifetime.UntilEndOfGame),
            new FDG.Rules.Definitions.HookEntry(FDG.Rules.Foundation.EHookID.Lifecycle_OnCapabilityQuery,
                new FDG.Rules.Definitions.Condition.Always(), new FDG.Rules.Definitions.Effect.EnableRangedUse(),
                FDG.Rules.Foundation.ELifetime.UntilEndOfGame),
        },
        System.Array.Empty<FDG.Rules.Definitions.ActivatedAbility>(),
        Scope: FDG.Rules.Foundation.ERuleScope.Weapon,
        Description: "Counts as both a ranged weapon and a melee weapon.");
}
