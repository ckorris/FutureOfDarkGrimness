using System.Collections.Generic;
using FDG;

namespace FdgRaylib.Rendering.TacticalOverlay;

/// <summary>
/// A 2D "shadow map" for one sight source (a target model's centre): N angle buckets, each holding the
/// distance at which the nearest Blocking (and, separately, nearest Cover) piece first interrupts a ray
/// from the source in that direction. Built once per source from the blocker silhouettes -- microseconds
/// -- after which classifying any world point is one atan2 + two array compares instead of a
/// segment-vs-every-blocker test. This is what makes the field's LoS/cover pass fast (#162 perf rework).
///
/// Semantics mirror <see cref="FDG.Stages.LineOfSightUtilities.EvaluateSightLine"/> exactly:
/// <list type="bullet">
/// <item>a sight segment P&lt;-&gt;S is Blocking iff some Blocking-flagged piece intersects it -- i.e. the
/// piece's nearest ray entry from S is closer than P;</item>
/// <item>else Cover iff some (Cover &amp;&amp; !Blocking) piece intersects it (a piece flagged both acts as
/// Blocking only, matching <c>TerrainData.EvaluateSightLine</c>'s flag priority);</item>
/// <item>else Clear.</item>
/// </list>
/// Fills are exact ray-primitive intersections evaluated at bucket-centre angles, so the only
/// approximation is angular quantization: error &lt;= (pi/N)*distance, ~0.05" at 30" with 2048 buckets --
/// under half a texel. The engine's zone tree is reduced via <see cref="ZoneExtensions.Primitives"/> to
/// exactly three leaf kinds (rect, circle, rotated rect), which is the whole "silhouette adapter".
///
/// Used ONLY by the field/picture pipeline. Pips, counts and the promoted measurement keep calling the
/// real engine functions (spec section 0); the fidelity sampler now genuinely referees this map against
/// them.
/// </summary>
internal sealed class PolarSightMap
{
    private readonly float[] _blockDepth;
    private readonly float[] _coverDepth;
    private readonly int _buckets;
    private readonly float _bucketWidth;     // radians per bucket
    private readonly float _invBucketWidth;
    public readonly Float2 Source;

    public PolarSightMap(Float2 source, int buckets)
    {
        Source = source;
        _buckets = buckets;
        _bucketWidth = 2f * MathF.PI / buckets;
        _invBucketWidth = 1f / _bucketWidth;
        _blockDepth = new float[buckets];
        _coverDepth = new float[buckets];
        System.Array.Fill(_blockDepth, float.PositiveInfinity);
        System.Array.Fill(_coverDepth, float.PositiveInfinity);
    }

    /// <summary>Builds a map for a source against the given pieces (terrain + model-base blockers).</summary>
    public static PolarSightMap Build(Position source, IReadOnlyList<ITerrain> pieces, int buckets)
    {
        var map = new PolarSightMap(new Float2(source.x, source.z), buckets);
        foreach (ITerrain piece in pieces)
        {
            bool blocking = piece.TerrainType.HasFlag(ETerrainType.Blocking);
            bool cover    = !blocking && piece.TerrainType.HasFlag(ETerrainType.Cover);
            if (!blocking && !cover) continue;
            map.AddZone(piece.Shape, blocking ? map._blockDepth : map._coverDepth);
        }
        return map;
    }

    /// <summary>
    /// The sight verdict for a segment from the world point (x, z) to this map's source, per the engine's
    /// flag priority. Exact up to angular quantization.
    /// </summary>
    public ESightLineEffect Evaluate(float x, float z)
    {
        float dx = x - Source.X;
        float dz = z - Source.Y;
        float dist = MathF.Sqrt(dx * dx + dz * dz);
        if (dist < 1e-6f) return ESightLineEffect.Clear; // standing on the source

        int idx = BucketIndex(MathF.Atan2(dz, dx));
        if (dist > _blockDepth[idx]) return ESightLineEffect.Blocking;
        if (dist > _coverDepth[idx]) return ESightLineEffect.Cover;
        return ESightLineEffect.Clear;
    }

    private int BucketIndex(float angle)
    {
        int i = (int)((angle + MathF.PI) * _invBucketWidth);
        // atan2 returns [-pi, pi]; +pi lands exactly on _buckets -- wrap.
        if (i >= _buckets) i -= _buckets;
        if (i < 0) i += _buckets;
        return i;
    }

    // ---- Raw access for the GPU fan renderer / harness ---------------------------------------------

    public int BucketCount => _buckets;

    /// <summary>Nearest Blocking entry distance per bucket (+inf = open). Do not mutate.</summary>
    public float[] BlockDepthRaw => _blockDepth;

    /// <summary>Nearest Cover entry distance per bucket (+inf = open). Do not mutate.</summary>
    public float[] CoverDepthRaw => _coverDepth;

    /// <summary>The angle of bucket edge i (bucket i spans [edge i, edge i+1)).</summary>
    public float BucketEdgeAngle(int i) => i * _bucketWidth - MathF.PI;

    /// <summary>
    /// Classifies every band cell of <paramref name="band"/> against <paramref name="maps"/> (best sight
    /// over all sources, engine priority) into the shadow/cover masks. THE shared CPU classification --
    /// the controller's field rebuild and the GPU-diff harness both call this, so the reference pixels
    /// the harness diffs against are byte-identical to what the CPU path renders in-game.
    /// </summary>
    public static void ClassifyInto(FieldMask band, FieldMask shadow, FieldMask cover,
        IReadOnlyList<PolarSightMap> maps)
    {
        shadow.Clear();
        cover.Clear();
        if (maps.Count == 0) return;

        int W = band.W, H = band.H;
        System.Threading.Tasks.Parallel.For(0, H, cy =>
        {
            int rowBase = cy * W;
            float cz = band.CellCenterZ(cy);
            for (int cx = 0; cx < W; cx++)
            {
                if (band.Cells[rowBase + cx] == 0) continue;
                float x = band.CellCenterX(cx);

                ESightLineEffect best = ESightLineEffect.Blocking;
                foreach (PolarSightMap map in maps)
                {
                    ESightLineEffect eff = map.Evaluate(x, cz);
                    if (eff < best) best = eff;                    // Clear(0) < Cover(1) < Blocking(2)
                    if (best == ESightLineEffect.Clear) break;
                }

                if (best == ESightLineEffect.Blocking)   shadow.Cells[rowBase + cx] = 1;
                else if (best == ESightLineEffect.Cover) cover.Cells[rowBase + cx]  = 1;
            }
        });
    }

    // Bucket-centre angle for an (unwrapped) bucket ordinal. Only ever fed to cos/sin, which are
    // periodic, so no wrapping needed here.
    private float BucketCenterAngle(int j) => (j + 0.5f) * _bucketWidth - MathF.PI;

    // ---- Primitive fills ---------------------------------------------------------------------------

    private void AddZone(IZone zone, float[] depth)
    {
        foreach (IZone prim in zone.Primitives())
        {
            switch (prim)
            {
                case CircularZone c:
                    FillCircle(c.Center, c.Radius, depth);
                    break;

                case RectangularZone r:
                    FillRect(r, angleDegrees: 0f, pivot: default, wrapper: null, depth);
                    break;

                case RotatedZoneWrapper w when w.Inner is RectangularZone rr:
                    FillRect(rr, w.AngleDegrees, w.Pivot, w, depth);
                    break;

                default:
                    // Unknown primitive: fall back to "blocks everywhere it could" is worse than being
                    // honest -- treat as no silhouette and let the fidelity sampler surface the gap.
                    break;
            }
        }
    }

    private void FillRect(RectangularZone r, float angleDegrees, Float2 pivot, IZone? wrapper, float[] depth)
    {
        // Source inside the rect -> everything in this layer is interrupted at distance 0.
        IZone containment = wrapper ?? r;
        if (containment.IsPointWithinZone(Source))
        {
            System.Array.Fill(depth, 0f);
            return;
        }

        Float2 c0 = Corner(r.Left, r.Bottom, angleDegrees, pivot);
        Float2 c1 = Corner(r.Left, r.Top, angleDegrees, pivot);
        Float2 c2 = Corner(r.Right, r.Top, angleDegrees, pivot);
        Float2 c3 = Corner(r.Right, r.Bottom, angleDegrees, pivot);

        FillSegment(c0, c1, depth);
        FillSegment(c1, c2, depth);
        FillSegment(c2, c3, depth);
        FillSegment(c3, c0, depth);
    }

    private static Float2 Corner(float x, float y, float angleDegrees, Float2 pivot) =>
        angleDegrees == 0f ? new Float2(x, y) : ZoneExtensions.RotateAround(new Float2(x, y), pivot, angleDegrees);

    /// <summary>
    /// Min-fills the depth buckets whose centre rays hit the segment C1-C2. The candidate bucket range is
    /// the angular interval subtended by the endpoints, padded a bucket each side; each candidate does an
    /// exact ray-segment intersection, so padding can't over-fill.
    /// </summary>
    private void FillSegment(Float2 c1, Float2 c2, float[] depth)
    {
        float a0 = MathF.Atan2(c1.Y - Source.Y, c1.X - Source.X);
        float a1 = MathF.Atan2(c2.Y - Source.Y, c2.X - Source.X);
        float sweep = WrapAngle(a1 - a0);
        if (sweep < 0f) { (a0, a1) = (a1, a0); sweep = -sweep; }

        float ex = c2.X - c1.X, ez = c2.Y - c1.Y;
        float qx = c1.X - Source.X, qz = c1.Y - Source.Y;

        int jStart = (int)MathF.Floor((a0 + MathF.PI) * _invBucketWidth) - 1;
        int count = (int)MathF.Ceiling(sweep * _invBucketWidth) + 3;
        if (count >= _buckets) { jStart = 0; count = _buckets; }

        for (int j = jStart; j < jStart + count; j++)
        {
            float theta = BucketCenterAngle(j);
            float ux = MathF.Cos(theta), uz = MathF.Sin(theta);

            float denom = ux * ez - uz * ex;         // cross(u, e)
            if (MathF.Abs(denom) < 1e-9f) continue;  // ray parallel to edge

            float t = (qx * ez - qz * ex) / denom;   // distance along ray
            float s = (qx * uz - qz * ux) / denom;   // position along segment
            if (t < 0f || s < 0f || s > 1f) continue;

            int idx = ((j % _buckets) + _buckets) % _buckets;
            if (t < depth[idx]) depth[idx] = t;
        }
    }

    private void FillCircle(Float2 center, float radius, float[] depth)
    {
        float cx = center.X - Source.X, cz = center.Y - Source.Y;
        float d = MathF.Sqrt(cx * cx + cz * cz);
        if (d <= radius) { System.Array.Fill(depth, 0f); return; } // source inside the circle

        float a = MathF.Atan2(cz, cx);
        float half = MathF.Asin(System.Math.Clamp(radius / d, 0f, 1f));

        int jStart = (int)MathF.Floor((a - half + MathF.PI) * _invBucketWidth) - 1;
        int count = (int)MathF.Ceiling(2f * half * _invBucketWidth) + 3;
        if (count >= _buckets) { jStart = 0; count = _buckets; }

        for (int j = jStart; j < jStart + count; j++)
        {
            float theta = BucketCenterAngle(j);
            float delta = WrapAngle(theta - a);
            float sPerp = d * MathF.Sin(delta);
            if (MathF.Abs(sPerp) > radius) continue;       // ray misses the disc
            float along = d * MathF.Cos(delta);
            if (along <= 0f) continue;                     // disc is behind the source

            float entry = along - MathF.Sqrt(MathF.Max(0f, radius * radius - sPerp * sPerp));
            if (entry < 0f) entry = 0f;

            int idx = ((j % _buckets) + _buckets) % _buckets;
            if (entry < depth[idx]) depth[idx] = entry;
        }
    }

    private static float WrapAngle(float a)
    {
        while (a > MathF.PI) a -= 2f * MathF.PI;
        while (a <= -MathF.PI) a += 2f * MathF.PI;
        return a;
    }
}
