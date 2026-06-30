using System;
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

    /// <summary> Filled base + outline on the Raylib canvas, centred at pixel (<paramref name="cx"/>, <paramref name="cy"/>). </summary>
    public static void DrawFilledRaylib(IBaseShape shape, float cx, float cy, float scale, Color fill, Color outline,
        Float2 facing = default)
    {
        if (shape is RectangleBase r)
        {
            Float2 f = Forward(facing);
            float w = r.WidthInches * scale, h = r.HeightInches * scale;
            float deg = MathF.Atan2(-f.X, -f.Y) * (180f / MathF.PI);
            // Filled via the built-in rotated-rect (handles winding internally); outline via the matching corners.
            Raylib.DrawRectanglePro(new Rectangle(cx, cy, w, h), new Vector2(w * 0.5f, h * 0.5f), deg, fill);
            Vector2[] c = RectCorners(r, new Vector2(cx, cy), scale, f);
            for (int i = 0; i < 4; i++) Raylib.DrawLineEx(c[i], c[(i + 1) % 4], 1f, outline);
            return;
        }

        float pr = shape.BoundingRadiusInches * scale; // CircleBase → its radius; fallback → bounding circle.
        Raylib.DrawCircle((int)cx, (int)cy, pr, fill);
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
