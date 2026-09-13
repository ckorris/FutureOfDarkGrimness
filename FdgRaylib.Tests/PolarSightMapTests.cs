using System;
using System.Collections.Generic;
using FDG;
using FDG.Stages;
using FdgRaylib.Rendering.TacticalOverlay;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #162 perf rework: PolarSightMap must reproduce LineOfSightUtilities.EvaluateSightLine's verdicts
// (Blocking / Cover / Clear, with the engine's flag priority) to within angular quantization. These
// tests referee the map against the REAL engine function -- deterministic cases exactly, randomized
// scenes statistically (mismatches allowed only near verdict boundaries).
[TestFixture]
public class PolarSightMapTests
{
    private const int Buckets = 2048;

    private static ITerrain Piece(ETerrainType type, IZone shape) => new TerrainData(type, shape);

    private static PolarSightMap BuildMap(Position source, params ITerrain[] pieces) =>
        PolarSightMap.Build(source, pieces, Buckets);

    private static ESightLineEffect Engine(Position from, Position source, ITerrain[] pieces) =>
        LineOfSightUtilities.EvaluateSightLine(from, source, pieces);

    // ---- Deterministic cases (points well away from any verdict boundary) -------------------------

    [Test]
    public void BlockingRect_ShadowsBehind_ClearBeside()
    {
        var pieces = new[] { Piece(ETerrainType.Blocking, new RectangularZone(10f, 12f, 9f, 11f)) };
        var source = new Position(5f, 10f);   // west of the rect
        PolarSightMap map = BuildMap(source, pieces);

        // Directly behind the rect (east side): blocked.
        Assert.That(map.Evaluate(20f, 10f), Is.EqualTo(ESightLineEffect.Blocking));
        Assert.That(Engine(new Position(20f, 10f), source, pieces), Is.EqualTo(ESightLineEffect.Blocking));

        // Well off to the side: clear.
        Assert.That(map.Evaluate(20f, 20f), Is.EqualTo(ESightLineEffect.Clear));
        Assert.That(Engine(new Position(20f, 20f), source, pieces), Is.EqualTo(ESightLineEffect.Clear));

        // Between source and rect (in front of it): clear.
        Assert.That(map.Evaluate(8f, 10f), Is.EqualTo(ESightLineEffect.Clear));
    }

    [Test]
    public void CoverRect_MarksCoverBehind_NotBlocking()
    {
        var pieces = new[] { Piece(ETerrainType.Cover, new RectangularZone(10f, 12f, 9f, 11f)) };
        var source = new Position(5f, 10f);
        PolarSightMap map = BuildMap(source, pieces);

        Assert.That(map.Evaluate(20f, 10f), Is.EqualTo(ESightLineEffect.Cover));
        Assert.That(Engine(new Position(20f, 10f), source, pieces), Is.EqualTo(ESightLineEffect.Cover));
        Assert.That(map.Evaluate(20f, 20f), Is.EqualTo(ESightLineEffect.Clear));
    }

    [Test]
    public void BlockingAndCoverFlags_ActsAsBlockingOnly_MatchingEnginePriority()
    {
        // A piece flagged Blocking|Cover must behave as Blocking (TerrainData checks Blocking first).
        var pieces = new[] { Piece(ETerrainType.Blocking | ETerrainType.Cover, new RectangularZone(10f, 12f, 9f, 11f)) };
        var source = new Position(5f, 10f);
        PolarSightMap map = BuildMap(source, pieces);

        Assert.That(map.Evaluate(20f, 10f), Is.EqualTo(ESightLineEffect.Blocking));
        Assert.That(Engine(new Position(20f, 10f), source, pieces), Is.EqualTo(ESightLineEffect.Blocking));
    }

    [Test]
    public void BlockingCircle_ShadowWedge_MatchesTangents()
    {
        var pieces = new[] { Piece(ETerrainType.Blocking, new CircularZone(10f, 10f, 1.5f)) };
        var source = new Position(2f, 10f);
        PolarSightMap map = BuildMap(source, pieces);

        Assert.That(map.Evaluate(20f, 10f), Is.EqualTo(ESightLineEffect.Blocking), "dead behind the disc");
        Assert.That(map.Evaluate(20f, 16f), Is.EqualTo(ESightLineEffect.Clear), "outside the tangent wedge");
        Assert.That(map.Evaluate(6f, 10f), Is.EqualTo(ESightLineEffect.Clear), "in front of the disc");
    }

    [Test]
    public void RotatedRect_ShadowMatchesEngine()
    {
        // 45-degree rotated rect between source and probe points.
        var rect = new RectangularZone(9f, 13f, 9.5f, 10.5f);
        var zone = new RotatedZoneWrapper(rect, 45f, new Float2(11f, 10f));
        var pieces = new[] { Piece(ETerrainType.Blocking, zone) };
        var source = new Position(11f, 2f);   // south of it
        PolarSightMap map = BuildMap(source, pieces);

        // Behind along the source->pivot line: blocked (the rotated rect still straddles the pivot).
        Assert.That(map.Evaluate(11f, 20f), Is.EqualTo(ESightLineEffect.Blocking));
        Assert.That(Engine(new Position(11f, 20f), source, pieces), Is.EqualTo(ESightLineEffect.Blocking));

        // Far off-axis: clear.
        Assert.That(map.Evaluate(30f, 4f), Is.EqualTo(ESightLineEffect.Clear));
    }

    [Test]
    public void SourceInsideBlocker_EverythingBlocked()
    {
        var pieces = new[] { Piece(ETerrainType.Blocking, new RectangularZone(8f, 12f, 8f, 12f)) };
        var source = new Position(10f, 10f);  // inside
        PolarSightMap map = BuildMap(source, pieces);

        Assert.That(map.Evaluate(20f, 10f), Is.EqualTo(ESightLineEffect.Blocking));
        Assert.That(map.Evaluate(10f, 25f), Is.EqualTo(ESightLineEffect.Blocking));
    }

    [Test]
    public void CompositeZone_PartsAllCast()
    {
        // L-shape: two rects. Points behind either arm are blocked.
        var zone = new CompositeZone(new IZone[]
        {
            new RectangularZone(10f, 14f, 10f, 11f),   // horizontal arm
            new RectangularZone(10f, 11f, 10f, 14f),   // vertical arm
        });
        var pieces = new[] { Piece(ETerrainType.Blocking, zone) };
        var source = new Position(5f, 5f);
        PolarSightMap map = BuildMap(source, pieces);

        Assert.That(map.Evaluate(20f, 13.5f), Is.EqualTo(ESightLineEffect.Blocking), "behind the horizontal arm");
        Assert.That(map.Evaluate(13.5f, 20f), Is.EqualTo(ESightLineEffect.Blocking), "behind the vertical arm");
        Assert.That(map.Evaluate(20f, 3f), Is.EqualTo(ESightLineEffect.Clear));
    }

    // ---- Randomized referee: statistical agreement with the real engine function ------------------

    [Test]
    public void RandomScenes_AgreeWithEngine_ExceptNearVerdictBoundaries()
    {
        var rng = new Random(162_001);
        int total = 0, mismatch = 0, offBoundaryMismatch = 0;

        for (int scene = 0; scene < 20; scene++)
        {
            var pieces = new List<ITerrain>();
            int n = 3 + rng.Next(5);
            for (int i = 0; i < n; i++)
            {
                float cx = 5f + (float)rng.NextDouble() * 60f;
                float cz = 5f + (float)rng.NextDouble() * 38f;
                ETerrainType type = rng.Next(3) switch
                {
                    0 => ETerrainType.Blocking,
                    1 => ETerrainType.Cover,
                    _ => ETerrainType.Blocking | ETerrainType.Cover,
                };
                switch (rng.Next(3))
                {
                    case 0:
                        pieces.Add(Piece(type, new RectangularZone(cx, cx + 1f + (float)rng.NextDouble() * 6f,
                                                                   cz, cz + 1f + (float)rng.NextDouble() * 6f)));
                        break;
                    case 1:
                        pieces.Add(Piece(type, new CircularZone(cx, cz, 0.5f + (float)rng.NextDouble() * 2.5f)));
                        break;
                    default:
                        var r = new RectangularZone(cx, cx + 2f + (float)rng.NextDouble() * 5f, cz, cz + 1f + (float)rng.NextDouble() * 2f);
                        pieces.Add(Piece(type, new RotatedZoneWrapper(r, (float)rng.NextDouble() * 360f,
                            new Float2(cx + 2f, cz + 1f))));
                        break;
                }
            }

            var source = new Position(2f + (float)rng.NextDouble() * 68f, 2f + (float)rng.NextDouble() * 44f);
            ITerrain[] arr = pieces.ToArray();
            PolarSightMap map = PolarSightMap.Build(source, arr, Buckets);

            for (int s = 0; s < 400; s++)
            {
                var p = new Position(1f + (float)rng.NextDouble() * 70f, 1f + (float)rng.NextDouble() * 46f);
                ESightLineEffect truth = Engine(p, source, arr);
                ESightLineEffect claim = map.Evaluate(p.x, p.z);
                total++;
                if (claim == truth) continue;
                mismatch++;

                // A mismatch is tolerable only if the point sits near a verdict boundary: nudging the
                // probe point by more than the map's quantization error flips the engine verdict. That
                // error is angular -- (2*pi/Buckets) * distance-from-source -- so the nudge must scale
                // with range (at 60" it's ~0.18"; a fixed epsilon under-estimates far from the source).
                if (!NearBoundary(p, source, arr, truth)) offBoundaryMismatch++;
            }
        }

        Assert.That(offBoundaryMismatch, Is.EqualTo(0),
            $"off-boundary disagreements with the engine: {offBoundaryMismatch} (total mismatch {mismatch}/{total})");
        Assert.That(mismatch / (float)total, Is.LessThan(0.02f),
            "even boundary-adjacent mismatch should stay rare");
    }

    // True if perturbing the probe point by the map's worst-case quantization error (one full bucket
    // angle x distance from source, plus a small floor) changes the engine's verdict, i.e. the point
    // sits on a verdict edge where quantization disagreements are expected.
    private static bool NearBoundary(Position p, Position source, ITerrain[] pieces, ESightLineEffect truth)
    {
        float dxs = p.x - source.x, dzs = p.z - source.z;
        float dist = MathF.Sqrt(dxs * dxs + dzs * dzs);
        float eps = 0.02f + dist * (2f * MathF.PI / Buckets);

        Span<(float dx, float dz)> nudges = stackalloc (float, float)[]
            { (eps, 0f), (-eps, 0f), (0f, eps), (0f, -eps) };
        foreach ((float dx, float dz) in nudges)
        {
            if (Engine(new Position(p.x + dx, p.z + dz), source, pieces) != truth)
                return true;
        }
        return false;
    }
}
