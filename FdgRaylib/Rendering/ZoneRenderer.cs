using System.Numerics;
using FDG;
using ImGuiNET;
using Raylib_cs;

namespace FdgRaylib.Rendering;

/// <summary>
/// Shared draw routines for IZone shapes. Two flavors: one that uses Raylib primitives
/// (for table-canvas elements drawn before rlImGui.Begin) and one that uses an ImGui draw
/// list (for resolver overlays that draw after rlImGui.Begin).
///
/// World→screen convention: world X maps to screen X; world Y (Float2.Y / Position.z) is
/// flipped against tableH so higher world Y is higher on screen.
/// </summary>
public static class ZoneRenderer
{
    private const int CircleSegments = 48;

    public static void DrawFilled(IZone shape, float scale, int originX, int originY, float tableH,
        Color fill, Color outline, float outlineThickness = 1f)
    {
        switch (shape)
        {
            case RectangularZone r:
                int x = originX + (int)(r.Left * scale);
                int y = originY + (int)((tableH - r.Top) * scale);
                int w = (int)((r.Right - r.Left) * scale);
                int h = (int)((r.Top - r.Bottom) * scale);
                Raylib.DrawRectangle(x, y, w, h, fill);
                Raylib.DrawRectangleLines(x, y, w, h, outline);
                break;

            case CircularZone c:
                float cx = originX + c.Center.X * scale;
                float cy = originY + (tableH - c.Center.Y) * scale;
                float pr = c.Radius * scale;
                Raylib.DrawCircle((int)cx, (int)cy, pr, fill);
                Raylib.DrawCircleLines((int)cx, (int)cy, pr, outline);
                break;

            case CompositeZone comp:
                foreach (var part in comp.Parts)
                    DrawFilled(part, scale, originX, originY, tableH, fill, outline, outlineThickness);
                break;

            default:
                //Unknown zone shape — skip. Add a case here when a new IZone is introduced.
                break;
        }
    }

    public static void DrawFilled(IZone shape, ImDrawListPtr drawList,
        float scale, int originX, int originY, float tableH,
        uint fillColor, uint outlineColor, float outlineThickness = 2f)
    {
        switch (shape)
        {
            case RectangularZone r:
                Vector2 tl = new(originX + r.Left * scale,  originY + (tableH - r.Top) * scale);
                Vector2 br = new(originX + r.Right * scale, originY + (tableH - r.Bottom) * scale);
                drawList.AddRectFilled(tl, br, fillColor);
                drawList.AddRect(tl, br, outlineColor, 0f, ImDrawFlags.None, outlineThickness);
                break;

            case CircularZone c:
                Vector2 center = new(originX + c.Center.X * scale, originY + (tableH - c.Center.Y) * scale);
                float pr = c.Radius * scale;
                drawList.AddCircleFilled(center, pr, fillColor, CircleSegments);
                drawList.AddCircle(center, pr, outlineColor, CircleSegments, outlineThickness);
                break;

            case CompositeZone comp:
                foreach (var part in comp.Parts)
                    DrawFilled(part, drawList, scale, originX, originY, tableH, fillColor, outlineColor, outlineThickness);
                break;
        }
    }
}
