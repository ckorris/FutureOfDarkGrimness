using System;
using FDG;
using FDG.Presentation.Beats;
using Raylib_cs;

namespace FdgRaylib.Rendering.Presentation;

/// <summary>
/// Draws the active <see cref="BannerBeat"/> as big flashing letters in the upper-center forefront:
/// fade in fast, hold, fade out. Color comes from the beat.
/// </summary>
public static class BannerOverlay
{
    private const int FontSize = 84;

    public static void Draw(BannerBeat beat, float progress, int areaWidth, int screenH)
    {
        float alpha = Envelope(progress);
        if (alpha <= 0f) return;

        string text = beat.BannerText ?? "";
        int textW = Raylib.MeasureText(text, FontSize);
        int x = (areaWidth - textW) / 2;
        int y = (int)(screenH * 0.30f);

        byte a = (byte)Math.Clamp(alpha * 255f, 0f, 255f);
        // Drop shadow so the text reads over the table.
        Raylib.DrawText(text, x + 3, y + 3, FontSize, new Color((byte)0, (byte)0, (byte)0, (byte)(a * 0.6f)));
        TextColor c = beat.Color;
        Raylib.DrawText(text, x, y, FontSize, new Color(c.R, c.G, c.B, a));
    }

    // Fade in over the first 15%, hold, fade out over the last 25%.
    private static float Envelope(float t)
    {
        const float fadeIn = 0.15f, holdEnd = 0.75f;
        if (t < fadeIn) return t / fadeIn;
        if (t > holdEnd) return Math.Max(0f, 1f - (t - holdEnd) / (1f - holdEnd));
        return 1f;
    }
}
