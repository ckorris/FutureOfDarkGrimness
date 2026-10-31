using System.Numerics;
using FDG;
using ImGuiNET;
using Raylib_cs;

namespace FdgRaylib.Rendering;

/// <summary>
/// The felt rect in screen pixels, plus the two clip primitives that keep world-space decorations
/// inside it.
///
/// Objective rings (the 3" seizure zone, the 9" placement exclusion) are drawn at a fixed inch
/// radius around a marker, so a marker near an edge paints its ring out over the background unless
/// it is clipped. The table is a rectangle, so a rect clip is the whole fix: the ring is cut at the
/// felt edge, which is also what it means physically - there is no table out there to seize.
///
/// Bounds are computed exactly the way <c>RaylibRenderer.DrawTable</c> computes the felt rect (same
/// int truncation of the scaled inches), so the clip lands on the felt edge, not a pixel off it.
/// Both entry points are strictly paired: call the matching End/Pop after drawing.
/// </summary>
public static class TableClip
{
    public static Rectangle Rect(float scale, int originX, int originY, float tableH)
    {
        int tw = (int)(GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES * scale);
        int th = (int)(tableH * scale);
        return new Rectangle(originX, originY, tw, th);
    }

    /// <summary>Raylib canvas pass. Pair with <c>Raylib.EndScissorMode()</c>.</summary>
    public static void BeginScissor(float scale, int originX, int originY, float tableH)
    {
        Rectangle r = Rect(scale, originX, originY, tableH);
        Raylib.BeginScissorMode((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
    }

    /// <summary>ImGui draw-list pass. Pair with <c>dl.PopClipRect()</c>.</summary>
    public static void PushClipRect(ImDrawListPtr dl, float scale, int originX, int originY, float tableH)
    {
        Rectangle r = Rect(scale, originX, originY, tableH);
        dl.PushClipRect(new Vector2(r.X, r.Y), new Vector2(r.X + r.Width, r.Y + r.Height), true);
    }
}
