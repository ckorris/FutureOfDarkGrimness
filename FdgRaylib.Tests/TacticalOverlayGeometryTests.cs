using System;
using System.Collections.Generic;
using System.Linq;
using FDG;
using FdgRaylib.Rendering.TacticalOverlay;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #162: the display-independent core of the tactical overlay -- disc rasterization into the field grid
// and marching-squares boundary extraction. The visual/interaction layers get eyeballed in a GUI pass
// and cross-checked by the in-game fidelity sampler; this locks down the geometry that both rely on.
[TestFixture]
public class TacticalOverlayGeometryTests
{
    // 10 texels/inch over a 20"x20" area -> a small, fast grid.
    private const float Tpi = 10f;
    private static FieldMask NewMask() => new FieldMask(200, 200, Tpi);

    private static float Dist(Float2 p, float cx, float cz) =>
        MathF.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cz) * (p.Y - cz));

    [Test]
    public void RasterizeDisc_SetsCentreInside_LeavesOutsideClear()
    {
        var mask = NewMask();
        mask.RasterizeDiscMax(10f, 10f, 3f, 1);

        // Centre cell is inside.
        int ccx = (int)(10f * Tpi);
        int ccy = (int)(10f * Tpi);
        Assert.That(mask.Cells[ccy * mask.W + ccx], Is.EqualTo(1), "centre should be set");

        // A cell well outside the radius is clear.
        int ox = (int)(16f * Tpi);
        Assert.That(mask.Cells[ccy * mask.W + ox], Is.EqualTo(0), "point 6\" away should be clear for a 3\" disc");
    }

    [Test]
    public void RasterizeDisc_MaxBlend_KeepsHigherBandValue()
    {
        var mask = NewMask();
        mask.RasterizeDiscMax(10f, 10f, 5f, 1); // outer band
        mask.RasterizeDiscMax(10f, 10f, 2f, 3); // inner band, higher value

        int ccx = (int)(10f * Tpi), ccy = (int)(10f * Tpi);
        Assert.That(mask.Cells[ccy * mask.W + ccx], Is.EqualTo(3), "inner higher band wins at centre");

        // A cell inside the outer but outside the inner keeps the outer band, not overwritten downward.
        int mx = (int)(13.5f * Tpi);
        Assert.That(mask.Cells[ccy * mask.W + mx], Is.EqualTo(1), "outer band retained where inner doesn't reach");
    }

    [Test]
    public void MarchingSquares_SingleDisc_IsOneClosedLoopAtRadius()
    {
        var mask = NewMask();
        const float cx = 10f, cz = 10f, r = 4f;
        mask.RasterizeDiscMax(cx, cz, r, 1);

        List<List<Float2>> polys = MarchingSquares.Extract(mask, 1, simplifyEpsilonInches: 0f);

        Assert.That(polys.Count, Is.EqualTo(1), "a lone disc yields exactly one boundary loop");
        List<Float2> loop = polys[0];

        // Every vertex sits within about a cell of the true radius.
        foreach (Float2 v in loop)
            Assert.That(Dist(v, cx, cz), Is.EqualTo(r).Within(0.2f),
                $"boundary vertex {v.X:0.00},{v.Y:0.00} should lie ~{r}\" from centre");

        // The loop closes back on itself.
        Assert.That(Dist(loop[0], loop[^1].X, loop[^1].Y), Is.LessThan(0.2f), "loop should close");
    }

    [Test]
    public void MarchingSquares_TwoSeparatedDiscs_YieldTwoLoops()
    {
        var mask = NewMask();
        mask.RasterizeDiscMax(5f, 10f, 2f, 1);
        mask.RasterizeDiscMax(15f, 10f, 2f, 1);

        List<List<Float2>> polys = MarchingSquares.Extract(mask, 1, simplifyEpsilonInches: 0f);
        Assert.That(polys.Count, Is.EqualTo(2), "two disjoint discs yield two boundary loops");
    }

    [Test]
    public void MarchingSquares_OverlappingDiscs_MergeToOneLoop()
    {
        var mask = NewMask();
        // Centres 3" apart, radius 2.5" each -> heavily overlapping, single connected region.
        mask.RasterizeDiscMax(9f, 10f, 2.5f, 1);
        mask.RasterizeDiscMax(12f, 10f, 2.5f, 1);

        List<List<Float2>> polys = MarchingSquares.Extract(mask, 1, simplifyEpsilonInches: 0f);
        Assert.That(polys.Count, Is.EqualTo(1), "overlapping discs union into one boundary loop");
    }

    [Test]
    public void FidelitySampler_CountsMismatchesBetweenClaimAndTruth()
    {
        var sampler = new FidelitySampler();
        // Claim: always inside. Truth: inside a 4" disc at (10,10). Mismatches = samples OUTSIDE the disc.
        var channels = new List<FidelitySampler.Channel>
        {
            new("t", (x, z) => true,
                     (x, z) => (x - 10f) * (x - 10f) + (z - 10f) * (z - 10f) <= 16f),
        };
        FidelitySampler.Report report = sampler.Run(20f, 20f, 1f, channels);

        Assert.That(report.SampleCount, Is.EqualTo(20 * 20), "20x20 area at 1\" spacing");
        // Disc area ~= pi*16 ~= 50 sq in of 400 -> most samples are outside -> most mismatch.
        Assert.That(report.PerChannel[0].mismatches, Is.GreaterThan(300));
        Assert.That(report.MismatchPercent(1), Is.GreaterThan(75f));
    }

    [Test]
    public void FidelitySampler_ThreatMaskMatchesDiscTruth_EdgeNoiseOnly()
    {
        // The sampler's real job: catch the rasterized mask disagreeing with the rules-truth geometry.
        // A correctly built mask should match a point-in-disc truth except within an edge texel or two.
        var cache = new ThreatFrontierCache(200, 200, Tpi);
        cache.Rebuild(new List<ThreatDisc> { new ThreatDisc(10f, 10f, 4f, 0f) }, simplifyEpsilonInches: 0f);

        var sampler = new FidelitySampler();
        var channels = new List<FidelitySampler.Channel>
        {
            new("charge",
                (x, z) => cache.SampleChargeInside(x, z),
                (x, z) => (x - 10f) * (x - 10f) + (z - 10f) * (z - 10f) <= 16f),
        };
        FidelitySampler.Report report = sampler.Run(20f, 20f, 0.5f, channels);

        Assert.That(report.MismatchPercent(1), Is.LessThan(3f),
            "mask should track the disc truth to within edge texels");
    }

    [Test]
    public void MarchingSquares_Simplify_ReducesVertexCountButKeepsRadius()
    {
        var mask = NewMask();
        const float cx = 10f, cz = 10f, r = 5f;
        mask.RasterizeDiscMax(cx, cz, r, 1);

        int raw       = MarchingSquares.Extract(mask, 1, 0f)[0].Count;
        var simplified = MarchingSquares.Extract(mask, 1, 1f / Tpi)[0];

        Assert.That(simplified.Count, Is.LessThan(raw), "Douglas-Peucker should drop collinear-ish vertices");
        foreach (Float2 v in simplified)
            Assert.That(Dist(v, cx, cz), Is.EqualTo(r).Within(0.35f), "simplified vertices still track the circle");
    }
}
