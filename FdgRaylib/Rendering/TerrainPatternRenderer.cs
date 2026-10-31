using System.Numerics;
using FDG;
using Raylib_cs;

namespace FdgRaylib.Rendering;

/// <summary>
/// Draws legibility patterns on top of a terrain piece's filled shape, one per rules-relevant flag so
/// they layer (a forest is Cover|Difficult -> chevrons + hatching; add Dangerous -> crimson Xs on top):
///   Difficult  -> diagonal hatching
///   Cover      -> inward-pointing chevrons along the edge
///   Dangerous  -> a field of faint crimson Xs
/// Hatching and the X field clip to any shape via <see cref="IZone.IsPointWithinZone"/> (sampled), so
/// circles/rotated/composite pieces stay clean; chevrons are drawn per primitive edge (recursing
/// composites/rotated) since they decorate the outline.
/// </summary>
internal static class TerrainPatternRenderer
{
    private static readonly Color HatchColor    = new(30, 26, 22, 175);   // dark diagonal lines
    private static readonly Color ChevronColor  = new(232, 222, 172, 230); // bright edge marks for cover
    private static readonly Color DangerColor    = new(200, 30, 30, 64);    // crimson, ~0.25 alpha

    private const float HatchSpacingPx = 11f;
    private const float HatchStepPx    = 4f;
    private const float HatchThickness = 1.5f;

    private const float DangerGridPx   = 20f;
    private const float DangerArmPx    = 5f;

    private const float ChevronSpacingPx = 16f;
    private const float ChevronSizePx    = 6f;

    public static void Draw(IZone shape, ETerrainType flags,
        float scale, int originX, int originY, float tableH)
    {
        if (flags.HasFlag(ETerrainType.Difficult))
            DrawHatch(shape, scale, originX, originY, tableH);

        if (flags.HasFlag(ETerrainType.Cover))
            DrawChevrons(shape, scale, originX, originY, tableH);

        if (flags.HasFlag(ETerrainType.Dangerous))
            DrawDangerField(shape, scale, originX, originY, tableH);
    }

    // ---- shape helpers -------------------------------------------------------------------------------

    private static Vector2 WorldToScreen(Float2 p, float scale, int originX, int originY, float tableH)
        => new(originX + p.X * scale, originY + (tableH - p.Y) * scale);

    private static Float2 ScreenToWorld(float sx, float sy, float scale, int originX, int originY, float tableH)
        => new((sx - originX) / scale, tableH - (sy - originY) / scale);

    // World-space axis-aligned bounds of a shape (recurses composites; rotates wrapped rect corners).
    private static (float minX, float minY, float maxX, float maxY) WorldBounds(IZone shape)
    {
        switch (shape)
        {
            case RectangularZone r:
                return (r.Left, r.Bottom, r.Right, r.Top);
            case CircularZone c:
                return (c.Center.X - c.Radius, c.Center.Y - c.Radius, c.Center.X + c.Radius, c.Center.Y + c.Radius);
            case CompositeZone comp:
            {
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                foreach (var part in comp.Parts)
                {
                    var b = WorldBounds(part);
                    minX = MathF.Min(minX, b.minX); minY = MathF.Min(minY, b.minY);
                    maxX = MathF.Max(maxX, b.maxX); maxY = MathF.Max(maxY, b.maxY);
                }
                return (minX, minY, maxX, maxY);
            }
            case RotatedZoneWrapper w when w.Inner is RectangularZone rr:
            {
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                foreach (var corner in RectCorners(rr))
                {
                    Float2 p = ZoneExtensions.RotateAround(corner, w.Pivot, w.AngleDegrees);
                    minX = MathF.Min(minX, p.X); minY = MathF.Min(minY, p.Y);
                    maxX = MathF.Max(maxX, p.X); maxY = MathF.Max(maxY, p.Y);
                }
                return (minX, minY, maxX, maxY);
            }
            case RotatedZoneWrapper wOther:
            {
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                foreach (var prim in ((IZone)wOther).Primitives())
                {
                    var b = WorldBounds(prim);
                    minX = MathF.Min(minX, b.minX); minY = MathF.Min(minY, b.minY);
                    maxX = MathF.Max(maxX, b.maxX); maxY = MathF.Max(maxY, b.maxY);
                }
                return (minX, minY, maxX, maxY);
            }
            default:
                return (0, 0, 0, 0);
        }
    }

    private static Float2[] RectCorners(RectangularZone r) => new[]
    {
        new Float2(r.Left, r.Bottom), new Float2(r.Right, r.Bottom),
        new Float2(r.Right, r.Top),   new Float2(r.Left, r.Top),
    };

    // ---- Difficult: diagonal hatching ----------------------------------------------------------------

    private static void DrawHatch(IZone shape, float scale, int originX, int originY, float tableH)
    {
        var (minX, minY, maxX, maxY) = WorldBounds(shape);
        Vector2 tl = WorldToScreen(new Float2(minX, maxY), scale, originX, originY, tableH); // screen top-left
        Vector2 br = WorldToScreen(new Float2(maxX, minY), scale, originX, originY, tableH); // screen bottom-right
        float x0 = tl.X, y0 = tl.Y, x1 = br.X, y1 = br.Y;
        float w = x1 - x0, h = y1 - y0;
        if (w <= 0 || h <= 0) return;

        // Family of 45-degree lines (screen slope +1: y = x + b). b spans the box diagonally.
        for (float b = (y0 - x1); b <= (y1 - x0); b += HatchSpacingPx * 1.41421f)
        {
            // Clip the infinite line y = x + b to the box, then walk it drawing inside sub-segments.
            float sx = MathF.Max(x0, y0 - b);
            float ex = MathF.Min(x1, y1 - b);
            if (ex <= sx) continue;

            DrawClippedPolyline(shape, sx, sx + b, ex, ex + b, scale, originX, originY, tableH);
        }
    }

    // Walk the screen segment (ax,ay)->(bx,by) in small steps, drawing sub-segments whose midpoint is
    // inside the zone. Batches consecutive inside steps into one DrawLine.
    private static void DrawClippedPolyline(IZone shape, float ax, float ay, float bx, float by,
        float scale, int originX, int originY, float tableH)
    {
        float len = MathF.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
        int steps = Math.Max(1, (int)(len / HatchStepPx));
        bool run = false;
        Vector2 runStart = default, prev = default;

        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            var pt = new Vector2(ax + (bx - ax) * t, ay + (by - ay) * t);
            Float2 world = ScreenToWorld(pt.X, pt.Y, scale, originX, originY, tableH);
            bool inside = shape.IsPointWithinZone(world);

            if (inside && !run) { run = true; runStart = pt; }
            else if (!inside && run) { Raylib.DrawLineEx(runStart, prev, HatchThickness, HatchColor); run = false; }
            prev = pt;
        }
        if (run) Raylib.DrawLineEx(runStart, prev, HatchThickness, HatchColor);
    }

    // ---- Dangerous: faint crimson X field ------------------------------------------------------------

    private static void DrawDangerField(IZone shape, float scale, int originX, int originY, float tableH)
    {
        var (minX, minY, maxX, maxY) = WorldBounds(shape);
        Vector2 tl = WorldToScreen(new Float2(minX, maxY), scale, originX, originY, tableH);
        Vector2 br = WorldToScreen(new Float2(maxX, minY), scale, originX, originY, tableH);
        float w = br.X - tl.X, h = br.Y - tl.Y;
        if (w <= 0 || h <= 0) return;

        // Center the grid within the bounds so the Xs sit uniformly (not offset from one corner).
        int cols = Math.Max(1, (int)(w / DangerGridPx));
        int rows = Math.Max(1, (int)(h / DangerGridPx));
        float startX = tl.X + (w - (cols - 1) * DangerGridPx) * 0.5f;
        float startY = tl.Y + (h - (rows - 1) * DangerGridPx) * 0.5f;

        // A small margin beyond the arm tips accounts for line thickness, so an X only draws when it fully
        // fits inside the zone -- nothing bleeds past the edge.
        float reach = DangerArmPx + 1.5f;
        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            float px = startX + c * DangerGridPx;
            float py = startY + r * DangerGridPx;
            if (!ArmTipsInside(shape, px, py, reach, scale, originX, originY, tableH)) continue;

            Raylib.DrawLineEx(new Vector2(px - DangerArmPx, py - DangerArmPx),
                              new Vector2(px + DangerArmPx, py + DangerArmPx), 2f, DangerColor);
            Raylib.DrawLineEx(new Vector2(px - DangerArmPx, py + DangerArmPx),
                              new Vector2(px + DangerArmPx, py - DangerArmPx), 2f, DangerColor);
        }
    }

    // True when all four corners of the X's bounding box are inside the zone. For the convex primitives
    // (rect/circle) that means the whole X fits; for composites it's a good, slightly-conservative test.
    private static bool ArmTipsInside(IZone shape, float px, float py, float reach,
        float scale, int originX, int originY, float tableH)
    {
        return shape.IsPointWithinZone(ScreenToWorld(px - reach, py - reach, scale, originX, originY, tableH))
            && shape.IsPointWithinZone(ScreenToWorld(px + reach, py - reach, scale, originX, originY, tableH))
            && shape.IsPointWithinZone(ScreenToWorld(px - reach, py + reach, scale, originX, originY, tableH))
            && shape.IsPointWithinZone(ScreenToWorld(px + reach, py + reach, scale, originX, originY, tableH));
    }

    // ---- Cover: inward chevrons along the edge -------------------------------------------------------

    private static void DrawChevrons(IZone shape, float scale, int originX, int originY, float tableH)
    {
        switch (shape)
        {
            case RectangularZone r:
                ChevronsAlongPolygon(RectCorners(r), scale, originX, originY, tableH);
                break;
            case RotatedZoneWrapper w when w.Inner is RectangularZone rr:
            {
                var corners = RectCorners(rr);
                for (int i = 0; i < corners.Length; i++)
                    corners[i] = ZoneExtensions.RotateAround(corners[i], w.Pivot, w.AngleDegrees);
                ChevronsAlongPolygon(corners, scale, originX, originY, tableH);
                break;
            }
            case CircularZone c:
                ChevronsAlongCircle(c, scale, originX, originY, tableH);
                break;
            case CompositeZone comp:
                foreach (var part in comp.Parts)
                    DrawChevrons(part, scale, originX, originY, tableH);
                break;
            case RotatedZoneWrapper wOther:
                foreach (var prim in ((IZone)wOther).Primitives())
                    DrawChevrons(prim, scale, originX, originY, tableH);
                break;
        }
    }

    // Chevrons along each edge of a closed polygon (world-space corners), apex pointing inward (toward
    // the polygon centroid).
    private static void ChevronsAlongPolygon(Float2[] cornersWorld, float scale, int originX, int originY, float tableH)
    {
        // Centroid in screen space, to orient chevrons inward.
        Vector2[] pts = new Vector2[cornersWorld.Length];
        Vector2 centroid = Vector2.Zero;
        for (int i = 0; i < cornersWorld.Length; i++)
        {
            pts[i] = WorldToScreen(cornersWorld[i], scale, originX, originY, tableH);
            centroid += pts[i];
        }
        centroid /= pts.Length;

        for (int i = 0; i < pts.Length; i++)
        {
            Vector2 a = pts[i], b = pts[(i + 1) % pts.Length];
            Vector2 edge = b - a;
            float len = edge.Length();
            if (len < 1f) continue;
            Vector2 dir = edge / len;
            // Inward normal: pick whichever of the two perpendiculars points toward the centroid.
            Vector2 n = new(-dir.Y, dir.X);
            if (Vector2.Dot(n, centroid - (a + b) * 0.5f) < 0) n = -n;

            int count = Math.Max(1, (int)(len / ChevronSpacingPx));
            for (int k = 1; k < count; k++)
            {
                Vector2 p = a + dir * (len * k / count);
                DrawChevron(p, dir, n);
            }
        }
    }

    private static void ChevronsAlongCircle(CircularZone c, float scale, int originX, int originY, float tableH)
    {
        Vector2 center = WorldToScreen(c.Center, scale, originX, originY, tableH);
        float rpx = c.Radius * scale;
        if (rpx < 4f) return;
        int count = Math.Max(6, (int)(2f * MathF.PI * rpx / ChevronSpacingPx));
        for (int k = 0; k < count; k++)
        {
            float ang = k * (2f * MathF.PI / count);
            Vector2 outward = new(MathF.Cos(ang), MathF.Sin(ang));
            Vector2 p = center + outward * rpx;
            Vector2 tangent = new(-outward.Y, outward.X);
            DrawChevron(p, tangent, -outward); // inward normal = -outward
        }
    }

    // A single chevron at p: apex pushed inward along n, two wings along +/- dir.
    private static void DrawChevron(Vector2 p, Vector2 dir, Vector2 n)
    {
        Vector2 apex = p + n * ChevronSizePx;
        Vector2 w1 = p - dir * ChevronSizePx;
        Vector2 w2 = p + dir * ChevronSizePx;
        Raylib.DrawLineEx(apex, w1, 2f, ChevronColor);
        Raylib.DrawLineEx(apex, w2, 2f, ChevronColor);
    }
}
