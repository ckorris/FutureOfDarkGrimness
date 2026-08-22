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
}
