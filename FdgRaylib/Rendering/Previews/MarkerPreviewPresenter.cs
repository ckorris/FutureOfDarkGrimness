using System.Numerics;
using FDG;
using FdgRaylib.Rendering.Resolvers;
using ImGuiNET;
using Raylib_cs;

namespace FdgRaylib.Rendering.Previews;

/// <summary>
/// Draws the marker family's remote previews (#280): the objective marker or terrain footprint
/// another player is currently hovering/confirming. The shape keeps its local color language
/// (neutral grey objective disc, terrain-type tint) at reduced alpha, wrapped in the placing
/// player's palette color for attribution - except when the placer is hovering an illegal spot,
/// where the outline goes red exactly like their own screen.
/// </summary>
public class MarkerPreviewPresenter : IRemotePreviewPresenter
{
    private static readonly uint InvalidOutline = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.30f, 0.30f, 0.85f));

    public void Draw(PlayerID sourcePlayer, IReadOnlyDictionary<string, object> slots, in PreviewDrawContext ctx)
    {
        if (slots.TryGetValue(MarkerPreviewSlots.Marker, out object? payload) == false)
        {
            return;
        }

        ImDrawListPtr dl = ImGui.GetBackgroundDrawList();
        Color playerColor = ctx.ColorForPlayer(sourcePlayer);
        switch (payload)
        {
            case ObjectiveMarkerPreview o:
                DrawObjective(dl, o, playerColor, in ctx);
                break;
            case TerrainFootprintPreview t:
                DrawTerrain(dl, t, playerColor, in ctx);
                break;
        }
    }

    private static void DrawObjective(ImDrawListPtr dl, ObjectiveMarkerPreview o, Color playerColor,
        in PreviewDrawContext ctx)
    {
        (float px, float py) = ctx.InchesToPixel(o.X, o.Z);
        var center = new Vector2(px, py);
        uint outline = o.Valid ? PlayerTint(playerColor, 0.85f) : InvalidOutline;

        // Seizure ring, dimmer than the placer's own (remote previews deliberately read quieter).
        // Clipped to the felt, same as the placer's ghost - WYSIWYG holds at the edges too.
        float seizurePx = o.SeizureRadiusInches * ctx.Scale;
        uint ringFill = ImGui.ColorConvertFloat4ToU32(
            new Vector4(0.70f, 0.70f, 0.70f, o.Pending ? 0.20f : 0.12f));
        TableClip.PushClipRect(dl, ctx.Scale, ctx.OriginX, ctx.OriginY, ctx.TableH);
        dl.AddCircleFilled(center, seizurePx, ringFill);
        dl.AddCircle(center, seizurePx, outline, 48, 2f);
        dl.PopClipRect();

        // Marker disc + number - same neutral grey as the renderer's committed markers.
        float radiusPx = o.BaseRadiusInches * ctx.Scale;
        float g = 160f / 255f;
        dl.AddCircleFilled(center, radiusPx, ImGui.ColorConvertFloat4ToU32(new Vector4(g, g, g, 0.90f)));
        dl.AddCircle(center, radiusPx, ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.90f)), 32, 1f);

        string label = o.MarkerNumber.ToString();
        Vector2 textSize = ImGui.CalcTextSize(label);
        dl.AddText(new Vector2(center.X - textSize.X / 2f, center.Y - textSize.Y / 2f),
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 1f)), label);
    }

    private static void DrawTerrain(ImDrawListPtr dl, TerrainFootprintPreview t, Color playerColor,
        in PreviewDrawContext ctx)
    {
        (Vector4 fillBase, _) = GuiPlaceOneTerrainResolver.TerrainTypeColors((ETerrainType)t.TerrainType);
        uint fill = ImGui.ColorConvertFloat4ToU32(
            new Vector4(fillBase.X, fillBase.Y, fillBase.Z, t.Pending ? 0.35f : 0.22f));
        uint outline = t.Valid ? PlayerTint(playerColor, 0.85f) : InvalidOutline;

        foreach (MarkerCircle c in t.Circles)
        {
            (float px, float py) = ctx.InchesToPixel(c.X, c.Z);
            var center = new Vector2(px, py);
            float radiusPx = c.Radius * ctx.Scale;
            dl.AddCircleFilled(center, radiusPx, fill, 48);
            dl.AddCircle(center, radiusPx, outline, 48, 2f);
        }

        foreach (MarkerQuad q in t.Quads)
        {
            if (q.Corners.Count != 4)
            {
                continue; // Malformed payload - cosmetic channel, skip.
            }
            var px = new Vector2[4];
            for (int i = 0; i < 4; i++)
            {
                (float x, float y) = ctx.InchesToPixel(q.Corners[i].X, q.Corners[i].Z);
                px[i] = new Vector2(x, y);
            }
            dl.AddQuadFilled(px[0], px[1], px[2], px[3], fill);
            dl.AddQuad(px[0], px[1], px[2], px[3], outline, 2f);
        }
    }

    private static uint PlayerTint(Color color, float alpha)
        => ImGui.ColorConvertFloat4ToU32(new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, alpha));
}
