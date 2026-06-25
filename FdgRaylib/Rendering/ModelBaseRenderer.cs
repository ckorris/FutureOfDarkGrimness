using System.Numerics;
using FDG;
using ImGuiNET;
using Raylib_cs;

namespace FdgRaylib.Rendering;

/// <summary>
/// Shared draw routines for a model's <see cref="IBaseShape"/> (#149), mirroring <see cref="ZoneRenderer"/>.
/// Two flavors: Raylib primitives for the table canvas (drawn before rlImGui.Begin) and an ImGui draw list
/// for resolver overlays (drawn after). Bases are axis-aligned and centred on the model, so a rectangle is
/// drawn as a centred box (no rotation yet). Any shape not given an explicit case falls back to its
/// bounding circle, so a future shape still renders something sensible.
/// </summary>
public static class ModelBaseRenderer
{
    private const int CircleSegments = 32;

    /// <summary> Filled base + outline on the Raylib canvas, centred at pixel (<paramref name="cx"/>, <paramref name="cy"/>). </summary>
    public static void DrawFilledRaylib(IBaseShape shape, float cx, float cy, float scale, Color fill, Color outline)
    {
        if (shape is RectangleBase r)
        {
            int w = (int)(r.WidthInches * scale);
            int h = (int)(r.HeightInches * scale);
            int x = (int)(cx - w * 0.5f);
            int y = (int)(cy - h * 0.5f);
            Raylib.DrawRectangle(x, y, w, h, fill);
            Raylib.DrawRectangleLines(x, y, w, h, outline);
            return;
        }

        float pr = shape.BoundingRadiusInches * scale; // CircleBase → its radius; fallback → bounding circle.
        Raylib.DrawCircle((int)cx, (int)cy, pr, fill);
        Raylib.DrawCircleLines((int)cx, (int)cy, pr, outline);
    }

    /// <summary> Filled base + outline on an ImGui draw list, centred at pixel <paramref name="center"/>. </summary>
    public static void DrawFilledImGui(ImDrawListPtr dl, IBaseShape shape, Vector2 center, float scale,
        uint fill, uint outline, float thickness = 1.5f)
    {
        if (shape is RectangleBase r)
        {
            Vector2 half = new(r.WidthInches * 0.5f * scale, r.HeightInches * 0.5f * scale);
            dl.AddRectFilled(center - half, center + half, fill);
            dl.AddRect(center - half, center + half, outline, 0f, ImDrawFlags.None, thickness);
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
        uint outline, float thickness = 1.5f, float inflateInches = 0f)
    {
        if (shape is RectangleBase r)
        {
            Vector2 half = new((r.WidthInches * 0.5f + inflateInches) * scale,
                               (r.HeightInches * 0.5f + inflateInches) * scale);
            dl.AddRect(center - half, center + half, outline, 0f, ImDrawFlags.None, thickness);
            return;
        }

        float pr = (shape.BoundingRadiusInches + inflateInches) * scale;
        dl.AddCircle(center, pr, outline, CircleSegments, thickness);
    }
}
