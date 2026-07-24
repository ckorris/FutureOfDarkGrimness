using System;
using System.Collections.Generic;
using FDG;
using FDG.Presentation.Beats;
using Raylib_cs;

namespace FdgRaylib.Rendering.Presentation;

/// <summary>
/// Draws <see cref="BannerBeat"/> announcements, in three sizes at three places on screen (#274). One
/// size for everything meant every announcement had the weight of a phase change, so the phase changes
/// stopped landing and nothing else could be announced at all without adding to the noise.
///
/// <list type="bullet">
/// <item><b>Headline</b> — big flashing letters in the upper-center forefront (the pre-#274 banner,
/// unchanged). Reserved for the game's structural spine. This is the only tier that stops play.</item>
/// <item><b>Notice</b> — mid-size, its own band below the headline band, so a Notice and a Headline can
/// never collide. At most one is on screen: a new Notice supersedes the last.</item>
/// <item><b>Toast</b> — small pills stacked in a ticker under the status HUD, riding along with
/// whatever else is animating.</item>
/// </list>
///
/// Each tier fades in fast, holds, fades out. Fonts are sized as a fraction of screen height (so they
/// are proportionate on a 1080p laptop and a 4K desktop alike), text word-wraps to the available width,
/// and as a last resort a single over-long word shrinks the font — so no banner ever runs off-screen.
/// </summary>
public static class BannerOverlay
{
    // Vertical bands, as a fraction of screen height. The headline sits high over the table; the notice
    // band is far enough below it to clear a two-line headline; toasts tuck under the status HUD strip.
    private const float HeadlineY = 0.26f;
    private const float NoticeY   = 0.40f;
    private const int   ToastTop  = 48;   // just below StatusHudOverlay's 12px margin + 24px text

    private const int ToastPadX = 10;
    private const int ToastPadY = 5;
    private const int ToastGap  = 6;

    private static readonly Color ToastPanel = new(20, 20, 24, 200);

    /// <summary>The Headline tier: the full-size announcement, unchanged from before the tier split.</summary>
    public static void Draw(BannerBeat beat, float progress, int areaWidth, int screenH)
    {
        // ~7% of screen height, clamped — scales with resolution without getting comically large on 4K.
        int fontSize = Math.Clamp((int)Math.Round(screenH * 0.07f), 36, 96);
        DrawCentered(beat, Envelope(progress, 0.15f, 0.75f), fontSize, (int)(screenH * HeadlineY),
            areaWidth, 0.90f);
    }

    /// <summary>
    /// Every Notice/Toast banner live this frame, from <c>PresentationPlayer.GetHeldBanners</c> (oldest
    /// first). Both tiers arrive on one track; they are drawn in their own bands.
    /// </summary>
    public static void DrawHeld(IReadOnlyList<(BannerBeat beat, float progress)> held, int areaWidth, int screenH)
    {
        int toastRow = 0;
        foreach ((BannerBeat beat, float progress) in held)
        {
            if (beat.Tier == EBannerTier.Notice)
                DrawNotice(beat, progress, areaWidth, screenH);
            else
                DrawToast(beat, progress, areaWidth, screenH, toastRow++);
        }
    }

    private static void DrawNotice(BannerBeat beat, float progress, int areaWidth, int screenH)
    {
        // ~4.2% of screen height: unmistakably an announcement, unmistakably not a headline.
        int fontSize = Math.Clamp((int)Math.Round(screenH * 0.042f), 24, 56);
        DrawCentered(beat, Envelope(progress, 0.15f, 0.75f), fontSize, (int)(screenH * NoticeY),
            areaWidth, 0.80f);
    }

    // One toast pill. Rows stack downward in arrival order; when the oldest expires the rest step up.
    private static void DrawToast(BannerBeat beat, float progress, int areaWidth, int screenH, int row)
    {
        // A toast is read peripherally and lingers, so it fades in sooner and out later than the loud
        // tiers — it should never flash for attention it hasn't earned.
        float alpha = Envelope(progress, 0.08f, 0.80f);
        if (alpha <= 0f) return;

        string text = beat.BannerText ?? "";
        if (text.Length == 0) return;

        int fontSize = Math.Clamp((int)Math.Round(screenH * 0.022f), 15, 28);
        int maxWidth = Math.Max(40, (int)(areaWidth * 0.42f));

        List<string> lines = WrapText(text, fontSize, maxWidth);
        int widest = WidestLine(lines, fontSize);
        if (widest > maxWidth)
        {
            fontSize = Math.Max(11, fontSize * maxWidth / widest);
            lines = WrapText(text, fontSize, maxWidth);
            widest = WidestLine(lines, fontSize);
        }

        int lineH  = (int)(fontSize * 1.2f);
        int pillW  = widest + ToastPadX * 2;
        int pillH  = lines.Count * lineH + ToastPadY * 2;
        // Rows are laid out at a uniform height so a two-line toast doesn't reflow the stack under it.
        int rowH   = lineH + ToastPadY * 2 + ToastGap;
        int pillX  = (areaWidth - pillW) / 2;
        int pillY  = ToastTop + row * rowH;

        byte a = (byte)Math.Clamp(alpha * 255f, 0f, 255f);
        Raylib.DrawRectangleRounded(new Rectangle(pillX, pillY, pillW, pillH), 0.35f, 5,
            Faded(ToastPanel, alpha));

        TextColor c = beat.Color;
        for (int i = 0; i < lines.Count; i++)
        {
            int w = Raylib.MeasureText(lines[i], fontSize);
            Raylib.DrawText(lines[i], pillX + (pillW - w) / 2, pillY + ToastPadY + i * lineH, fontSize,
                new Color(c.R, c.G, c.B, a));
        }
    }

    // Shared body of the two centered, shadowed tiers (Headline and Notice): wrap to widthFraction of
    // the table area, shrink the font if a lone word still overflows, then draw centered from y0 down.
    private static void DrawCentered(BannerBeat beat, float alpha, int fontSize, int y0, int areaWidth,
        float widthFraction)
    {
        if (alpha <= 0f) return;

        string text = beat.BannerText ?? "";
        if (text.Length == 0) return;

        int maxWidth = Math.Max(40, (int)(areaWidth * widthFraction));
        List<string> lines = WrapText(text, fontSize, maxWidth);

        // A single word wider than the available space can't be wrapped — shrink the font to fit it.
        int widest = WidestLine(lines, fontSize);
        if (widest > maxWidth)
        {
            fontSize = Math.Max(20, fontSize * maxWidth / widest);
            lines = WrapText(text, fontSize, maxWidth);
        }

        int lineH  = (int)(fontSize * 1.15f);
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

    private static Color Faded(Color c, float alpha) =>
        new(c.R, c.G, c.B, (byte)Math.Clamp(c.A * alpha, 0f, 255f));

    // Fade in over the first fadeIn, hold, fade out over everything past holdEnd.
    private static float Envelope(float t, float fadeIn, float holdEnd)
    {
        if (t < fadeIn) return t / fadeIn;
        if (t > holdEnd) return Math.Max(0f, 1f - (t - holdEnd) / (1f - holdEnd));
        return 1f;
    }
}
