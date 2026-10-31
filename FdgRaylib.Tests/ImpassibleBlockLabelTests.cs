using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// #317 — impassible terrain refuses the placement outright rather than shortening it, so it has no would-be
/// phantom; it gets the same two-line "here is the rule that stopped you" text beside the red contact
/// footprint. Pins the wording and the ASCII-only rule (the ImGui atlas bakes Basic Latin + Latin-1 only).
/// </summary>
[TestFixture]
public class ImpassibleBlockLabelTests
{
    [Test]
    public void ReadsAsTheRuleThenTheConsequence()
    {
        Assert.That(ImpassibleBlockLabel.HEADER, Is.EqualTo("Impassible Terrain"));
        Assert.That(ImpassibleBlockLabel.DETAIL, Is.EqualTo("Cannot move through it"));
    }

    [Test]
    public void IsAsciiOnly()
    {
        foreach (char c in ImpassibleBlockLabel.HEADER + ImpassibleBlockLabel.DETAIL)
            Assert.That(c, Is.LessThanOrEqualTo((char)0x7F), "non-ASCII renders as '?' in the game font atlas");
    }

    [Test]
    public void HeaderMatchesTheDifficultTerrainLabelsShape()
    {
        // Both terrain rules name themselves on line 1 and explain on line 2; if one drifts to a sentence or
        // a lower-case key the two stop reading as the same kind of message on the table.
        var difficult = DifficultShortfallPlan.Build(
            FDG.Stages.MovementUtilities.EDifficultClampKind.CappedCrossing, shortfallInches: 2f, capInches: 6f);

        Assert.That(ImpassibleBlockLabel.HEADER, Does.EndWith("Terrain"));
        Assert.That(difficult.Header, Does.EndWith("Terrain"));
        Assert.That(ImpassibleBlockLabel.HEADER, Does.Not.EndWith("."));
        Assert.That(ImpassibleBlockLabel.DETAIL, Does.Not.EndWith("."));
    }
}
