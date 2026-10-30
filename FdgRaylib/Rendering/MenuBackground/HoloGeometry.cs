using System.Numerics;

namespace FdgRaylib.Rendering.MenuBackground;

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

    /// <summary>True when segment a-b keeps at least <paramref name="pad"/> from every footprint.</summary>
    public static bool IsClear(Vector2 a, Vector2 b, IEnumerable<HoloFootprint> footprints, float pad) =>
        footprints.All(fp => SegmentPolygon(a, b, fp.Polygon) >= pad);
}
