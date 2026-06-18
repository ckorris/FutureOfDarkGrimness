using System;
using System.Collections.Generic;
using FDG;
using FDG.Presentation.Beats;
using Raylib_cs;

namespace FdgRaylib.Rendering.Presentation;

/// <summary>
/// Draws the active <see cref="BannerBeat"/> as big flashing letters in the upper-center forefront:
/// fade in fast, hold, fade out. Color comes from the beat.
///
/// The font is sized as a fraction of the screen height (so it's proportionate on a 1080p laptop and a
/// 4K desktop alike), the text word-wraps to the available width, and as a last resort a single
/// over-long word shrinks the font — so a long banner never runs off-screen.
/// </summary>
public static class BannerOverlay
{
    public static void Draw(BannerBeat beat, float progress, int areaWidth, int screenH)
    {
        float alpha = Envelope(progress);
        if (alpha <= 0f) return;

        string text = beat.BannerText ?? "";
        if (text.Length == 0) return;

        int maxWidth = Math.Max(40, (int)(areaWidth * 0.90f));
        // ~7% of screen height, clamped — scales with resolution without getting comically large on 4K.
        int fontSize = Math.Clamp((int)Math.Round(screenH * 0.07f), 36, 96);

        List<string> lines = WrapText(text, fontSize, maxWidth);

        // A single word wider than the available space can't be wrapped — shrink the font to fit it.
        int widest = WidestLine(lines, fontSize);
        if (widest > maxWidth)
        {
            fontSize = Math.Max(20, fontSize * maxWidth / widest);
            lines = WrapText(text, fontSize, maxWidth);
        }

        int lineH  = (int)(fontSize * 1.15f);
        int y0     = (int)(screenH * 0.26f);
        byte a     = (byte)Math.Clamp(alpha * 255f, 0f, 255f);
        byte shadA = (byte)(a * 0.6f);
        TextColor c = beat.Color;

        for (int i = 0; i < lines.Count; i++)
        {
            int w = Raylib.MeasureText(lines[i], fontSize);
            int x = (areaWidth - w) / 2;
            int y = y0 + i * lineH;
            // Drop shadow so the text reads over the table.
            Raylib.DrawText(lines[i], x + 3, y + 3, fontSize, new Color((byte)0, (byte)0, (byte)0, shadA));
            Raylib.DrawText(lines[i], x, y, fontSize, new Color(c.R, c.G, c.B, a));
        }
    }

    // Greedy word wrap: pack words onto a line until the next would exceed maxWidth. A lone word that
    // overflows on its own gets its own line (the caller then shrinks the font to fit it).
    private static List<string> WrapText(string text, int fontSize, int maxWidth)
    {
        var lines = new List<string>();
        string current = "";
        foreach (string word in text.Split(' '))
        {
            string trial = current.Length == 0 ? word : current + " " + word;
            if (current.Length == 0 || Raylib.MeasureText(trial, fontSize) <= maxWidth)
            {
                current = trial;
            }
            else
            {
                lines.Add(current);
                current = word;
            }
        }
        if (current.Length > 0) lines.Add(current);
        return lines;
    }

    private static int WidestLine(List<string> lines, int fontSize)
    {
        int widest = 0;
        foreach (string line in lines)
            widest = Math.Max(widest, Raylib.MeasureText(line, fontSize));
        return widest;
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
