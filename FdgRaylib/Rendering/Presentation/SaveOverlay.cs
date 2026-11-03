using System;
using FDG;
using FDG.Presentation.Beats;
using Raylib_cs;

namespace FdgRaylib.Rendering.Presentation;

/// <summary>
/// Draws the active <see cref="SaveBeat"/> as deflection "pings" — expanding, fading rings on the
/// defender's models, one per saved shot (cycling across models, stacked pings nudged apart).
/// Saves are unit-level (the engine doesn't track which model was saved), so this is feedback that
/// "N blows were turned aside," not per-model.
/// </summary>
public static class SaveOverlay
{
    private static readonly Color Shield = new(150, 200, 255, 255); // deflection blue

    public static void Draw(SaveBeat beat, float progress, float scale, int originX, int originY, float tableH)
    {
        int positions = beat.DefenderPositions.Count;
        if (positions == 0 || beat.SavedCount <= 0) return;

        byte a = (byte)Math.Clamp((1f - progress) * 255f, 0f, 255f); // fade out
        var c = new Color(Shield.R, Shield.G, Shield.B, a);
        float r = Math.Max(5f, scale * 0.35f) * (0.6f + progress);   // expand outward

        for (int i = 0; i < beat.SavedCount; i++)
        {
            Position p = beat.DefenderPositions[i % positions];
            int stack = i / positions; // nudge pings that land on the same model
            int cx = originX + (int)(p.x * scale) + stack * 6;
            int cy = originY + (int)((tableH - p.z) * scale) - stack * 6;
            Raylib.DrawCircleLines(cx, cy, r, c);
        }
    }
}
