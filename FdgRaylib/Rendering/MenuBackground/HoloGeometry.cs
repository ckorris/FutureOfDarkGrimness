using System.Numerics;

namespace FdgRaylib.Rendering.MenuBackground;

/// <summary>
/// Room the terrain must leave (#464): points that must stay outside every outline and more than their
/// clearance from it, and segments that must keep at least theirs. The menu fits the terrain round the
/// units with it, so placing units never has to search round terrain.
/// </summary>
internal sealed record HoloKeepOut(
    IReadOnlyList<(Vector2 At, float Clear)> Points,
    IReadOnlyList<(Vector2 A, Vector2 B, float Clear)> Segments)
{
    public static readonly HoloKeepOut None = new(Array.Empty<(Vector2, float)>(), Array.Empty<(Vector2, Vector2, float)>());

    /// <summary>True when the outline leaves every point and segment its room - the bounding circle first,
    /// the outline itself only when that is close.</summary>
    public bool Allows(IReadOnlyList<Vector2> poly)
    {
        Vector2 c = Vector2.Zero;
        for (int i = 0; i < poly.Count; i++) c += poly[i];
        c /= poly.Count;
        float r = 0f;
        for (int i = 0; i < poly.Count; i++) r = MathF.Max(r, Vector2.Distance(c, poly[i]));

        foreach (var (at, clear) in Points)
            if (Vector2.Distance(at, c) - r <= clear && HoloGeometry.PointPolygon(at, poly) <= clear) return false;
        foreach (var (a, b, clear) in Segments)
            if (HoloGeometry.PointSegment(c, a, b) - r < clear && HoloGeometry.SegmentPolygon(a, b, poly) < clear) return false;
        return true;
    }
}

/// <summary>
/// The little bit of 2D geometry the menu's war table needs (#448): point-in-polygon and distances
/// between points, segments and polygons on the table plane. Shared by the line-of-fire check, the random
/// terrain layout (pieces kept apart) and the random unit placement (bases kept off terrain).
/// </summary>
internal static class HoloGeometry
{
    public static bool Contains(IReadOnlyList<Vector2> poly, Vector2 p)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) &&
                p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                inside = !inside;
        return inside;
    }

    public static float PointSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float len2 = ab.LengthSquared();
        float t = len2 == 0f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f);
        return Vector2.Distance(p, a + ab * t);
    }

    /// <summary>Shortest distance between segments a-b and c-d (0 when they cross).</summary>
    public static float SegmentDistance(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        static float Cross(Vector2 o, Vector2 p, Vector2 q) => (p.X - o.X) * (q.Y - o.Y) - (p.Y - o.Y) * (q.X - o.X);
        float d1 = Cross(c, d, a), d2 = Cross(c, d, b), d3 = Cross(a, b, c), d4 = Cross(a, b, d);
        if ((d1 > 0) != (d2 > 0) && (d3 > 0) != (d4 > 0)) return 0f;
        return MathF.Min(MathF.Min(PointSegment(a, c, d), PointSegment(b, c, d)),
                         MathF.Min(PointSegment(c, a, b), PointSegment(d, a, b)));
    }

    /// <summary>Distance from a point to a polygon's boundary; 0 when the point is inside it.</summary>
    public static float PointPolygon(Vector2 p, IReadOnlyList<Vector2> poly)
    {
        if (Contains(poly, p)) return 0f;
        float best = float.MaxValue;
        for (int i = 0; i < poly.Count; i++) best = MathF.Min(best, PointSegment(p, poly[i], poly[(i + 1) % poly.Count]));
        return best;
    }

    /// <summary>Distance from segment a-b to a polygon; 0 when it touches, crosses or lies inside it.</summary>
    public static float SegmentPolygon(Vector2 a, Vector2 b, IReadOnlyList<Vector2> poly)
    {
        if (Contains(poly, a) || Contains(poly, b)) return 0f;
        float best = float.MaxValue;
        for (int i = 0; i < poly.Count; i++) best = MathF.Min(best, SegmentDistance(a, b, poly[i], poly[(i + 1) % poly.Count]));
        return best;
    }

    /// <summary>Gap between two polygons; 0 when they overlap or one contains the other.</summary>
    public static float PolygonPolygon(IReadOnlyList<Vector2> p, IReadOnlyList<Vector2> q)
    {
        if (Contains(p, q[0]) || Contains(q, p[0])) return 0f;
        float best = float.MaxValue;
        for (int i = 0; i < p.Count; i++) best = MathF.Min(best, SegmentPolygon(p[i], p[(i + 1) % p.Count], q));
        return best;
    }

    /// <summary>True when segment a-b keeps at least <paramref name="pad"/> from every footprint. A
    /// footprint whose bounding circle is already that far away needs no closer look.</summary>
    public static bool IsClear(Vector2 a, Vector2 b, IEnumerable<HoloFootprint> footprints, float pad)
    {
        foreach (HoloFootprint fp in footprints)
            if (PointSegment(fp.Centre, a, b) - fp.Radius < pad && SegmentPolygon(a, b, fp.Polygon) < pad) return false;
        return true;
    }

    /// <summary>True when point p is more than <paramref name="gap"/> from the footprint's outline and
    /// outside it, checking the bounding circle first.</summary>
    public static bool IsFarFrom(Vector2 p, HoloFootprint fp, float gap) =>
        Vector2.Distance(p, fp.Centre) - fp.Radius > gap || PointPolygon(p, fp.Polygon) > gap;

    /// <summary>True when two polygons' bounding circles are more than <paramref name="gap"/> apart, so
    /// the polygons are too; false means "look closer", not "too close".</summary>
    public static bool BoundsApart(IReadOnlyList<Vector2> p, IReadOnlyList<Vector2> q, float gap)
    {
        (Vector2 cp, float rp) = Bound(p);
        (Vector2 cq, float rq) = Bound(q);
        return Vector2.Distance(cp, cq) - rp - rq > gap;
    }

    private static (Vector2 Centre, float Radius) Bound(IReadOnlyList<Vector2> poly)
    {
        Vector2 c = Vector2.Zero;
        for (int i = 0; i < poly.Count; i++) c += poly[i];
        c /= poly.Count;
        float r = 0f;
        for (int i = 0; i < poly.Count; i++) r = MathF.Max(r, Vector2.Distance(c, poly[i]));
        return (c, r);
    }
}
