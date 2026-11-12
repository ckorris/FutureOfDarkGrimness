using System.Collections.Generic;
using System.Linq;
using FdgRaylib.Rendering.Presentation;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #399 - the dust cloud that marks a unit arriving from reserve. The drawing is hand-verified; the
// shape of the animation is pinned here, plus the property that actually matters for a networked game:
// it is a pure function of the beat, so two players watching the same ambush land see the same cloud.
[TestFixture]
public class DustCloudPlanTests
{
    private const float ModelRadius = 0.55f;

    private static IReadOnlyList<DustCloudPlan.Puff> At(float t, int seed = 0)
        => DustCloudPlan.Puffs(t, ModelRadius, seed);

    [Test]
    public void NothingIsDrawnOutsideTheEnvelope()
    {
        Assert.That(At(0f), Is.Empty, "the cloud starts when the beat does");
        Assert.That(At(1f), Is.Empty, "and is gone when it ends - no puff left sitting on the table");
        Assert.That(DustCloudPlan.RingAlpha(0f), Is.Zero);
        Assert.That(DustCloudPlan.RingAlpha(1f), Is.Zero);
    }

    [Test]
    public void PuffsAreDeterministic()
    {
        // The whole reason there is no RNG in here: the beat crosses the wire, so the visual has to be
        // a function of it. Two clients drawing the same arrival must agree puff for puff.
        var a = At(0.4f, seed: 3);
        var b = At(0.4f, seed: 3);

        Assert.That(a, Has.Count.EqualTo(b.Count));
        for (int i = 0; i < a.Count; i++)
        {
            Assert.That(a[i].OffsetXInches, Is.EqualTo(b[i].OffsetXInches).Within(0.00001f));
            Assert.That(a[i].OffsetZInches, Is.EqualTo(b[i].OffsetZInches).Within(0.00001f));
            Assert.That(a[i].RadiusInches, Is.EqualTo(b[i].RadiusInches).Within(0.00001f));
            Assert.That(a[i].Alpha, Is.EqualTo(b[i].Alpha).Within(0.00001f));
        }
    }

    [Test]
    public void NeighbouringModels_DoNotDrawTheIdenticalFigure()
    {
        var first  = At(0.5f, seed: 0);
        var second = At(0.5f, seed: 1);

        Assert.That(first, Is.Not.Empty);
        Assert.That(first.Zip(second, (x, y) => x.OffsetXInches == y.OffsetXInches
                                             && x.OffsetZInches == y.OffsetZInches).All(same => same),
            Is.False, "a rank of models must not stamp the same cloud out N times");
    }

    [Test]
    public void PuffsDriftOutwardAndGrow()
    {
        float early = At(0.35f).Max(p => Reach(p));
        float late  = At(0.85f).Max(p => Reach(p));
        Assert.That(late, Is.GreaterThan(early), "the cloud throws outward as it goes");

        Assert.That(At(0.85f).Max(p => p.RadiusInches),
            Is.GreaterThan(At(0.35f).Max(p => p.RadiusInches)), "and each puff swells");
    }

    [Test]
    public void PuffAlpha_RisesThenThins()
    {
        // Dust, not a flashbulb: up fast, down slow, and never fully opaque at the plan level (the
        // overlay scales it down further).
        float rising  = At(0.15f).Max(p => p.Alpha);
        float peakish = At(0.35f).Max(p => p.Alpha);
        float dying   = At(0.9f).Max(p => p.Alpha);

        Assert.That(peakish, Is.GreaterThan(rising));
        Assert.That(dying, Is.LessThan(peakish));
        Assert.That(At(0.5f).Max(p => p.Alpha), Is.LessThanOrEqualTo(1f));
    }

    [Test]
    public void GroundRing_IsOverWellBeforeTheCloudIs()
    {
        // The ring is the impact; the cloud is the aftermath. If the ring outlived the cloud the
        // landing would read as happening at the end of the beat instead of the start.
        Assert.That(DustCloudPlan.RingAlpha(0.1f), Is.GreaterThan(0f));
        Assert.That(DustCloudPlan.RingAlpha(0.6f), Is.Zero);
        Assert.That(At(0.6f), Is.Not.Empty, "while the cloud is still going");
    }

    [Test]
    public void GroundRing_ExpandsFromNothing()
    {
        Assert.That(DustCloudPlan.RingRadiusInches(0f, ModelRadius), Is.Zero);
        Assert.That(DustCloudPlan.RingRadiusInches(0.3f, ModelRadius),
            Is.GreaterThan(DustCloudPlan.RingRadiusInches(0.1f, ModelRadius)));
    }

    [Test]
    public void CloudScalesWithTheBase()
    {
        Assert.That(DustCloudPlan.Puffs(0.5f, 1.2f, 0).Max(p => p.RadiusInches),
            Is.GreaterThan(DustCloudPlan.Puffs(0.5f, 0.4f, 0).Max(p => p.RadiusInches)),
            "a bigger base throws a bigger cloud");
    }

    [Test]
    public void Stagger_RipplesAcrossTheUnit_ButEveryModelStillFinishes()
    {
        Assert.That(DustCloudPlan.Staggered(0.5f, 0, 5),
            Is.GreaterThan(DustCloudPlan.Staggered(0.5f, 4, 5)),
            "the first model is further along than the last - the rank ripples");

        for (int i = 0; i < 5; i++)
        {
            Assert.That(DustCloudPlan.Staggered(1f, i, 5), Is.EqualTo(1f).Within(0.00001f),
                $"model {i} must reach the end of its own animation inside the beat");
        }
    }

    [Test]
    public void SingleModelUnit_HasNoStagger()
    {
        Assert.That(DustCloudPlan.Staggered(0.42f, 0, 1), Is.EqualTo(0.42f).Within(0.00001f));
    }

    private static float Reach(DustCloudPlan.Puff p)
        => System.MathF.Sqrt(p.OffsetXInches * p.OffsetXInches + p.OffsetZInches * p.OffsetZInches);
}
