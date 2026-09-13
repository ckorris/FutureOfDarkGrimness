using System.Linq;
using FdgRaylib.Rendering.Resolvers;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// The pure formatter behind the deploy / activate unit-picker content (bright heading + dim detail lines).
[TestFixture]
public class UnitOptionLabelTests
{
    [Test]
    public void Build_HeadingIsName_DetailsAreStatsThenWeapons()
    {
        var (heading, details) = UnitOptionLabel.Build("Warriors", 5, 4, 4,
            new[] { ("Rifle", 24f), ("Blade", 0f) });

        Assert.That(heading, Is.EqualTo("Warriors"));
        Assert.That(details, Has.Count.EqualTo(2));
        Assert.That(details[0], Is.EqualTo("5 models, Q4+ D4+"));
        Assert.That(details[1], Is.EqualTo("Rifle (24\"), Blade (melee)"));
    }

    [Test]
    public void Build_SingleModel_UsesSingularNoun()
    {
        var (heading, details) = UnitOptionLabel.Build("Hero", 1, 3, 2,
            new[] { ("Power Sword", 0f) });

        Assert.That(heading, Is.EqualTo("Hero"));
        Assert.That(details[0], Is.EqualTo("1 model, Q3+ D2+"));
        Assert.That(details[1], Does.Contain("Power Sword (melee)"));
    }

    [Test]
    public void Build_FractionalRange_TrimsCleanly()
    {
        var (_, details) = UnitOptionLabel.Build("Snipers", 2, 4, 4,
            new[] { ("Long Rifle", 30.5f) });

        Assert.That(details[1], Does.Contain("Long Rifle (30.5\")"));
    }

    [Test]
    public void Build_NoWeapons_ShowsPlaceholder()
    {
        var (_, details) = UnitOptionLabel.Build("Unarmed", 3, 5, 5,
            System.Array.Empty<(string, float)>());

        Assert.That(details[1], Is.EqualTo("(no weapons)"));
    }

    [Test]
    public void Build_OmitsUnitWideSpecialRules()
    {
        // Unit-WIDE special rules are intentionally not part of the button content (they belong in the hover
        // tooltip). Per-WEAPON rules are shown via the rules overload below.
        var (heading, details) = UnitOptionLabel.Build("Shadow Squad", 5, 4, 4,
            new[] { ("Rifle", 24f) });

        Assert.That(heading + string.Join(" ", details), Does.Not.Contain("Stealth"));
    }

    [Test]
    public void Build_WithWeaponRules_AppendsRulesAfterRange()
    {
        var (_, details) = UnitOptionLabel.Build("Snipers", 3, 4, 4,
            new[] { ("Shredder Cannon", 18f, "Rending, Blast(3)"), ("Blade", 0f, "") });

        // Range then rules inside the parens; a ruleless weapon stays "(melee)".
        Assert.That(details[1], Is.EqualTo("Shredder Cannon (18\", Rending, Blast(3)), Blade (melee)"));
    }
}
