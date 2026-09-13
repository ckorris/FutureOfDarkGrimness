using FDG;
using FDG.Data;
using FdgRaylib.Placement;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// #269 — a reposition placement (Teleport, Fanatic, reposition-at-activation) judges cohesion once over the
/// finished formation instead of gating each click on "within 1in of an already-placed model", which
/// confined every model after the first to a thin band around model 1. These pin the verdict, including
/// the lenient "not worsened" half that keeps an already-scattered unit from being trapped.
/// </summary>
[TestFixture]
public class PlacementCohesionTests
{
    private const float BaseRadius = 0.5f;   // 1" circular base, so centre gap - 1" = base-to-base gap
    private static readonly Float2 Facing = new(0f, 1f);

    private static PlacementCohesion.Footprint At(float x, float z) =>
        new(new Position(x, z), new CircleBase(BaseRadius), Facing);

    [Test]
    public void ModelsPackedTogether_AreCohesive()
    {
        // Centres 1.5" apart -> 0.5" base-to-base, inside the 1" nearest-neighbour limit.
        var start = new[] { At(10f, 10f), At(11.5f, 10f), At(13f, 10f) };
        var end   = new[] { At(20f, 20f), At(21.5f, 20f), At(23f, 20f) };

        PlacementCohesion.Report report = PlacementCohesion.Evaluate(start, end);

        Assert.That(report.IsAcceptable, Is.True);
        Assert.That(PlacementCohesion.Describe(report), Is.Null);
    }

    [Test]
    public void AModelTeleportedAwayFromTheUnit_IsStranded()
    {
        var start = new[] { At(10f, 10f), At(11.5f, 10f), At(13f, 10f) };
        // Model 2 lands 6" off - the whole point of the Done gate is to catch exactly this.
        var end   = new[] { At(10f, 10f), At(11.5f, 10f), At(19f, 10f) };

        PlacementCohesion.Report report = PlacementCohesion.Evaluate(start, end);

        Assert.That(report.IsAcceptable, Is.False);
        Assert.That(report.StrandedCount, Is.EqualTo(1));
        Assert.That(report.WorstNearestGapInches, Is.EqualTo(6.5f).Within(0.01f),
            "centres 7.5\" apart less two 0.5\" radii.");
        Assert.That(PlacementCohesion.Describe(report), Does.Contain("out of cohesion"));
    }

    [Test]
    public void AnAlreadyScatteredUnit_MayStandStill()
    {
        // A mid-unit casualty left these survivors 4" base-to-base apart. Standing still must stay legal,
        // or the prompt traps the player: no placement at all would be acceptable.
        var scattered = new[] { At(10f, 10f), At(15f, 10f) };

        PlacementCohesion.Report report = PlacementCohesion.Evaluate(scattered, scattered);

        Assert.That(report.IsAcceptable, Is.True,
            "the rule is 'not worsened', so a formation that was already broken is never made illegal by " +
            "declining to move.");
    }

    [Test]
    public void AnAlreadyScatteredUnit_MayCloseTheGapWithoutFullyRepairingIt()
    {
        var start = new[] { At(10f, 10f), At(15f, 10f) };   // 4" base-to-base
        var end   = new[] { At(10f, 10f), At(13f, 10f) };   // 2" base-to-base - better, still over the limit

        Assert.That(PlacementCohesion.Evaluate(start, end).IsAcceptable, Is.True,
            "a re-forming placement that shrinks the gap is legal even before it reaches 1\".");
    }

    [Test]
    public void AnAlreadyScatteredUnit_MayNotScatterFurther()
    {
        var start = new[] { At(10f, 10f), At(15f, 10f) };   // 4" base-to-base
        var end   = new[] { At(10f, 10f), At(17f, 10f) };   // 6" base-to-base - worse

        PlacementCohesion.Report report = PlacementCohesion.Evaluate(start, end);

        Assert.That(report.IsAcceptable, Is.False);
        Assert.That(report.StrandedCount, Is.EqualTo(2), "both models' nearest gap widened.");
    }

    [Test]
    public void ChainedModels_AreCohesive_EvenThoughTheEndsAreFarApart()
    {
        // Nearest-neighbour, not all-pairs: a conga line is legal as long as each link is within 1" and no
        // pair exceeds 9". This is the case the old per-click check could not express, because it demanded
        // proximity to whichever model happened to be placed first.
        var start = new[] { At(10f, 10f), At(11.5f, 10f), At(13f, 10f), At(14.5f, 10f) };
        var end   = new[] { At(30f, 10f), At(31.5f, 10f), At(33f, 10f), At(34.5f, 10f) };

        Assert.That(PlacementCohesion.Evaluate(start, end).IsAcceptable, Is.True);
    }

    [Test]
    public void ModelsSpreadBeyondTheAllPairsLimit_AreScattered()
    {
        // Each link is 0.5" base-to-base, but the two ends end up over 9" apart.
        var start = new List<PlacementCohesion.Footprint>();
        var end = new List<PlacementCohesion.Footprint>();
        for (int i = 0; i < 9; i++)
        {
            start.Add(At(10f, 10f + i * 0.2f));         // a tight blob to start
            end.Add(At(10f + i * 1.5f, 20f));           // strung out to 12" end-to-end
        }

        PlacementCohesion.Report report = PlacementCohesion.Evaluate(start, end);

        Assert.That(report.IsAcceptable, Is.False);
        Assert.That(report.StrandedCount, Is.Zero, "every model still has a neighbour within 1\".");
        Assert.That(report.ScatteredCount, Is.GreaterThan(0));
        Assert.That(PlacementCohesion.Describe(report), Does.Contain("too far from the rest of the unit"));
    }

    [Test]
    public void ASingleModelUnit_IsAlwaysCohesive()
    {
        var one = new[] { At(10f, 10f) };

        Assert.That(PlacementCohesion.Evaluate(one, new[] { At(40f, 40f) }).IsAcceptable, Is.True,
            "a lone model (a hero, a monster) has no cohesion to break.");
    }

    [Test]
    public void MismatchedLists_ReportNothing()
    {
        // Defensive: callers build both lists from the same placement, so this should never happen - but a
        // half-built formation must not raise a warning about a state the player is mid-way through fixing.
        var before = new[] { At(10f, 10f), At(11.5f, 10f) };
        var after  = new[] { At(20f, 20f) };

        Assert.That(PlacementCohesion.Evaluate(before, after).IsAcceptable, Is.True);
    }
}
