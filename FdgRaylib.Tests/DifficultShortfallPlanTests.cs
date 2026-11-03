using FDG.Stages;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// #317 — the movement ghost snapping back at difficult terrain read as a bug in playtest because nothing
/// on the table said why. The resolver now draws the would-be pose in gray with a two-line reason; these pin
/// when that phantom appears and that each of the clamp's two cases gets the sentence that actually applies.
/// </summary>
[TestFixture]
public class DifficultShortfallPlanTests
{
    private const float Cap = 6f;

    [Test]
    public void MovingThroughDifficultTerrain_ExplainsTheCap()
    {
        var hint = DifficultShortfallPlan.Build(
            MovementUtilities.EDifficultClampKind.CappedCrossing, shortfallInches: 3.5f, capInches: Cap);

        Assert.That(hint.Show, Is.True);
        Assert.That(hint.Header, Is.EqualTo("Difficult Terrain"));
        Assert.That(hint.Detail, Is.EqualTo("Can only move 6\""));
    }

    [Test]
    public void StoppedAtTheEdge_SaysTheMoveIsSpent_NotThatSixInchesAreAvailable()
    {
        var hint = DifficultShortfallPlan.Build(
            MovementUtilities.EDifficultClampKind.StoppedShortOfEdge, shortfallInches: 3.5f, capInches: Cap);

        Assert.That(hint.Show, Is.True);
        Assert.That(hint.Detail, Is.EqualTo("Cannot enter - 6\" used"),
            "this model has ALREADY moved its 6\" - telling it it may move 6\" explains nothing.");
    }

    [Test]
    public void TerrainDidNotShortenTheMove_NoPhantom()
    {
        var hint = DifficultShortfallPlan.Build(
            MovementUtilities.EDifficultClampKind.NotLimited, shortfallInches: 4f, capInches: Cap);

        Assert.That(hint.Show, Is.False,
            "a move the band cap / enemy bases / table edge shortened must not be blamed on terrain.");
    }

    [Test]
    public void ShortfallTooSmallToSee_NoPhantom()
    {
        var hint = DifficultShortfallPlan.Build(
            MovementUtilities.EDifficultClampKind.CappedCrossing,
            shortfallInches: DifficultShortfallPlan.MIN_SHORTFALL_INCHES - 0.01f, capInches: Cap);

        Assert.That(hint.Show, Is.False,
            "the phantom would sit on top of the real ghost and the dotted link would be a smudge.");
    }

    [Test]
    public void ShortfallAtTheThreshold_Draws()
    {
        var hint = DifficultShortfallPlan.Build(
            MovementUtilities.EDifficultClampKind.CappedCrossing,
            shortfallInches: DifficultShortfallPlan.MIN_SHORTFALL_INCHES, capInches: Cap);

        Assert.That(hint.Show, Is.True);
    }

    [TestCase(6f, "6")]
    [TestCase(6.5f, "6.5")]
    [TestCase(5.98f, "6")]
    public void CapReadsAsAWholeNumberWhenItIsOne(float cap, string expected)
    {
        var hint = DifficultShortfallPlan.Build(
            MovementUtilities.EDifficultClampKind.CappedCrossing, shortfallInches: 3f, capInches: cap);

        Assert.That(hint.Detail, Is.EqualTo($"Can only move {expected}\""));
    }

    [Test]
    public void LabelIsAsciiOnly()
    {
        // The ImGui font atlas bakes Basic Latin + Latin-1 only; anything above U+00FF renders as '?'.
        foreach (var kind in new[] { MovementUtilities.EDifficultClampKind.CappedCrossing,
                                     MovementUtilities.EDifficultClampKind.StoppedShortOfEdge })
        {
            var hint = DifficultShortfallPlan.Build(kind, shortfallInches: 3f, capInches: Cap);
            Assert.That(hint.Header + hint.Detail, Is.All.LessThanOrEqualTo((char)0x7F));
        }
    }
}
