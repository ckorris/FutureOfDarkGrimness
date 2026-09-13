using System;
using System.Collections.Generic;
using System.Numerics;
using FDG;
using ImGuiNET;
using Raylib_cs;

namespace FdgRaylib.Rendering;

/// <summary>
/// Shared draw routines for a model's <see cref="IBaseShape"/> (#149), mirroring <see cref="ZoneRenderer"/>.
/// Two flavors: Raylib primitives for the table canvas (drawn before rlImGui.Begin) and an ImGui draw list
/// for resolver overlays (drawn after).
///
/// A rectangle is drawn oriented by the model's <see cref="IModel.Facing"/> (#150) — a yaw unit normal along
/// the base's local +Z (forward / height) axis. Table Z maps to screen −y, so the screen-space rotation that
/// takes the local axes to the facing has <c>sin = −facing.X, cos = −facing.Y</c>. Facing defaults to forward
/// (+Z) when the caller passes none or a zero vector, reproducing the pre-#150 axis-aligned box. Any shape not
/// given an explicit case falls back to its bounding circle (rotation-invariant), so a future shape still renders.
/// </summary>
public static class ModelBaseRenderer
{
    private const int CircleSegments = 32;

    /// <summary>
    /// Filled base + outline on the Raylib canvas, centred at pixel (<paramref name="cx"/>, <paramref name="cy"/>),
    /// optionally inflated by <paramref name="inflateInches"/> so a highlight sits just outside the base.
    /// </summary>
    public static void DrawFilledRaylib(IBaseShape shape, float cx, float cy, float scale, Color fill, Color outline,
        Float2 facing = default, float inflateInches = 0f)
    {
        if (shape is RectangleBase r)
        {
            Float2 f = Forward(facing);
            float w = (r.WidthInches + 2f * inflateInches) * scale;
            float h = (r.HeightInches + 2f * inflateInches) * scale;
            float deg = MathF.Atan2(-f.X, -f.Y) * (180f / MathF.PI);
            // Filled via the built-in rotated-rect (handles winding internally); outline via the matching corners.
            Raylib.DrawRectanglePro(new Rectangle(cx, cy, w, h), new Vector2(w * 0.5f, h * 0.5f), deg, fill);
            Vector2[] c = RectCorners(r, new Vector2(cx, cy), scale, f, inflateInches);
            for (int i = 0; i < 4; i++) Raylib.DrawLineEx(c[i], c[(i + 1) % 4], 1f, outline);
            return;
        }

        float pr = (shape.BoundingRadiusInches + inflateInches) * scale; // CircleBase → its radius; fallback → bounding circle.
        Raylib.DrawCircle((int)cx, (int)cy, pr, fill);
        Raylib.DrawCircleLines((int)cx, (int)cy, pr, outline);
    }

    /// <summary>
    /// Outline-only base on the Raylib canvas (selection / spotlight rings) — the canvas counterpart to
    /// <see cref="DrawOutlineImGui"/>, inflated by <paramref name="inflateInches"/> so the ring sits
    /// outside the base.
    /// </summary>
    public static void DrawOutlineRaylib(IBaseShape shape, float cx, float cy, float scale, Color outline,
        float thickness = 1f, float inflateInches = 0f, Float2 facing = default)
    {
        if (shape is RectangleBase r)
        {
            Vector2[] c = RectCorners(r, new Vector2(cx, cy), scale, Forward(facing), inflateInches);
            for (int i = 0; i < 4; i++) Raylib.DrawLineEx(c[i], c[(i + 1) % 4], thickness, outline);
            return;
        }

        float pr = (shape.BoundingRadiusInches + inflateInches) * scale;
        Raylib.DrawCircleLines((int)cx, (int)cy, pr, outline);
    }

    /// <summary> Filled base + outline on an ImGui draw list, centred at pixel <paramref name="center"/>. </summary>
    public static void DrawFilledImGui(ImDrawListPtr dl, IBaseShape shape, Vector2 center, float scale,
        uint fill, uint outline, float thickness = 1.5f, Float2 facing = default)
    {
        if (shape is RectangleBase r)
        {
            Vector2[] c = RectCorners(r, center, scale, Forward(facing));
            dl.AddQuadFilled(c[0], c[1], c[2], c[3], fill);
            dl.AddQuad(c[0], c[1], c[2], c[3], outline, thickness);
            return;
        }

        float pr = shape.BoundingRadiusInches * scale;
        dl.AddCircleFilled(center, pr, fill, CircleSegments);
        dl.AddCircle(center, pr, outline, CircleSegments, thickness);
    }

    /// <summary>
    /// Outline-only base on an ImGui draw list (selection/start rings), optionally inflated by
    /// <paramref name="inflateInches"/> so the ring sits just outside the base.
    /// </summary>
    public static void DrawOutlineImGui(ImDrawListPtr dl, IBaseShape shape, Vector2 center, float scale,
        uint outline, float thickness = 1.5f, float inflateInches = 0f, Float2 facing = default)
    {
        if (shape is RectangleBase r)
        {
            Vector2[] c = RectCorners(r, center, scale, Forward(facing), inflateInches);
            dl.AddQuad(c[0], c[1], c[2], c[3], outline, thickness);
            return;
        }

        float pr = (shape.BoundingRadiusInches + inflateInches) * scale;
        dl.AddCircle(center, pr, outline, CircleSegments, thickness);
    }

    /// <summary>
    /// The outline of a base grown outward by <paramref name="bandInches"/> — the EXACT Minkowski sum of the
    /// base with a disc of that radius (#334, the 1" forced-charge standoff band).
    ///
    /// <see cref="IBaseShape.Footprint"/> hands back a rounded convex hull (#150: corners + a rounding
    /// radius), so growing it is just <c>Rounding + band</c>: a circle becomes a bigger circle, an oriented
    /// rectangle becomes a rounded rectangle whose corners are band-radius arcs. Deliberately NOT
    /// <see cref="DrawOutlineImGui"/>'s <c>inflateInches</c>, which pushes a rectangle's half-extents out and
    /// leaves square corners — fine for a selection ring, but here the outline IS a rules boundary, and
    /// square corners would claim ground the rule leaves legal. Any future shape gets this for free, since
    /// the footprint seam is all this reads.
    /// </summary>
    public static void DrawBandOutlineImGui(ImDrawListPtr dl, IBaseShape shape, Vector2 center, float scale,
        float bandInches, uint color, float thickness = 1.5f, Float2 facing = default)
    {
        Float2[] outline = BandOutline(shape, Forward(facing), bandInches);
        if (outline.Length < 3) return;

        // Table Z maps to screen −y.
        foreach (Float2 p in outline)
            dl.PathLineTo(new Vector2(center.X + p.X * scale, center.Y - p.Y * scale));
        dl.PathStroke(color, ImDrawFlags.Closed, thickness);
    }

    // Arc resolution per hull corner. A circle base is one "corner" and gets the full ring.
    private const int BandArcSegments = 16;

    /// <summary>
    /// The band outline as offsets from the base centre, in table inches (X right, Y = table Z) — the pure
    /// geometry behind <see cref="DrawBandOutlineImGui"/>, split out so it can be unit-tested without an ImGui
    /// context (the same split <c>ModelRoster</c> uses for its layout arithmetic). Every returned point is
    /// exactly <paramref name="bandInches"/> from the base's surface.
    /// </summary>
    internal static Float2[] BandOutline(IBaseShape shape, Float2 facing, float bandInches)
    {
        BaseFootprint fp = shape.Footprint(new Position(0f, 0f), Forward(facing));
        float radius = fp.Rounding + bandInches;
        Float2[] hull = fp.Corners;
        if (hull.Length == 0 || radius <= 0f) return System.Array.Empty<Float2>();

        var points = new List<Float2>(hull.Length * BandArcSegments);

        // A single hull point (any circular base) is just a ring about it.
        if (hull.Length == 1)
        {
            for (int i = 0; i < CircleSegments; i++)
            {
                float a = i * MathF.PI * 2f / CircleSegments;
                points.Add(new Float2(hull[0].X + radius * MathF.Cos(a), hull[0].Y + radius * MathF.Sin(a)));
            }
            return points.ToArray();
        }

        // Wind counter-clockwise (shoelace > 0 in table space) so the outward normal of edge a→b is
        // consistently (dy, −dx) and the corner arcs sweep in increasing angle.
        var v = new List<Float2>(hull);
        double area = 0;
        for (int i = 0; i < v.Count; i++)
        {
            Float2 a = v[i], b = v[(i + 1) % v.Count];
            area += (double)a.X * b.Y - (double)b.X * a.Y;
        }
        if (area < 0) v.Reverse();

        // One arc per corner, from the incoming edge's outward normal round to the outgoing edge's. The arc
        // endpoints ARE the ends of the offset straight edges, so consecutive arcs joined by the closing
        // polyline reproduce the whole rounded outline — no separate edge pass needed.
        int n = v.Count;
        for (int i = 0; i < n; i++)
        {
            float aIn  = OutwardNormalAngle(v[(i - 1 + n) % n], v[i]);
            float aOut = OutwardNormalAngle(v[i], v[(i + 1) % n]);
            while (aOut < aIn - 1e-5f) aOut += MathF.PI * 2f;

            for (int s = 0; s <= BandArcSegments; s++)
            {
                float a = aIn + (aOut - aIn) * s / BandArcSegments;
                points.Add(new Float2(v[i].X + radius * MathF.Cos(a), v[i].Y + radius * MathF.Sin(a)));
            }
        }
        return points.ToArray();
    }

    // Angle of the outward normal of the edge a→b, for a hull wound counter-clockwise so (dy, −dx) is outward.
    private static float OutwardNormalAngle(Float2 a, Float2 b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        return MathF.Atan2(-dx, dy);
    }

    // Heading marker (#150): a small triangle at the model's front, pointing along its Facing. Drawn just
    // outside the base edge in the facing direction. Sizes are in inches so the marker scales with zoom.
    private const float HeadingFrontGapInches = 0.05f;
    private const float HeadingLengthInches = 0.28f;
    private const float HeadingHalfWidthInches = 0.16f;

    /// <summary> Heading triangle on the Raylib canvas, centred on the model at pixel (cx, cy). </summary>
    public static void DrawHeadingRaylib(IBaseShape shape, float cx, float cy, float scale, Float2 facing, Color color)
    {
        if (!HeadingPoints(shape, new Vector2(cx, cy), scale, facing, out Vector2 apex, out Vector2 b1, out Vector2 b2))
            return;
        // Raylib's DrawTriangle backface-culls and our winding depends on the facing, so draw both orderings —
        // the front-facing one renders, the other is culled (no double-blend).
        Raylib.DrawTriangle(apex, b1, b2, color);
        Raylib.DrawTriangle(apex, b2, b1, color);
    }

    /// <summary> Heading triangle on an ImGui draw list, centred on the model at pixel <paramref name="center"/>. </summary>
    public static void DrawHeadingImGui(ImDrawListPtr dl, IBaseShape shape, Vector2 center, float scale, Float2 facing, uint color)
    {
        if (!HeadingPoints(shape, center, scale, facing, out Vector2 apex, out Vector2 b1, out Vector2 b2))
            return;
        dl.AddTriangleFilled(apex, b1, b2, color);
    }

    // The three pixel corners of the heading triangle: apex out front along facing, base across it at the edge.
    private static bool HeadingPoints(IBaseShape shape, Vector2 center, float scale, Float2 facing,
        out Vector2 apex, out Vector2 b1, out Vector2 b2)
    {
        apex = b1 = b2 = default;
        Float2 f = Forward(facing);
        // Screen-space facing: table Z maps to screen −y.
        float fx = f.X, fy = -f.Y;
        float len = MathF.Sqrt(fx * fx + fy * fy);
        if (len < 1e-6f) return false;
        fx /= len; fy /= len;
        float px = -fy, py = fx; // perpendicular

        // Distance from centre to the front edge along facing: a rectangle's half-height (facing runs along
        // its local +Z), a circle's radius.
        float frontIn = shape is RectangleBase r ? r.HeightInches * 0.5f : shape.BoundingRadiusInches;
        float baseX = center.X + fx * (frontIn + HeadingFrontGapInches) * scale;
        float baseY = center.Y + fy * (frontIn + HeadingFrontGapInches) * scale;
        float triLen = HeadingLengthInches * scale, triHalf = HeadingHalfWidthInches * scale;

        apex = new Vector2(baseX + fx * triLen, baseY + fy * triLen);
        b1 = new Vector2(baseX + px * triHalf, baseY + py * triHalf);
        b2 = new Vector2(baseX - px * triHalf, baseY - py * triHalf);
        return true;
    }

    // Treats a zero (unset) facing as forward (+Z), so callers without a meaningful facing get the axis-aligned box.
    private static Float2 Forward(Float2 facing) =>
        facing.X == 0f && facing.Y == 0f ? new Float2(0f, 1f) : facing;

    // The rectangle's four pixel corners, oriented by facing. The base's local +Z (height) axis runs along the
    // facing direction in screen space, +X (width) perpendicular; both half-extents optionally inflated.
    private static Vector2[] RectCorners(RectangleBase r, Vector2 center, float scale, Float2 facing, float inflateInches = 0f)
    {
        float hw = (r.WidthInches * 0.5f + inflateInches) * scale;
        float hh = (r.HeightInches * 0.5f + inflateInches) * scale;
        float sin = -facing.X, cos = -facing.Y; // screen rotation taking local axes to facing (table Z → screen −y)
        Vector2 Corner(float dx, float dy) => new(
            center.X + dx * cos - dy * sin,
            center.Y + dx * sin + dy * cos);
        return new[] { Corner(-hw, -hh), Corner(hw, -hh), Corner(hw, hh), Corner(-hw, hh) };
    }
}
