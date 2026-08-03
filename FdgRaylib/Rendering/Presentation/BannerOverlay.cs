using System;
using System.Collections.Generic;
using FDG;
using FDG.Presentation.Beats;
using Raylib_cs;

namespace FdgRaylib.Rendering.Presentation;

/// <summary>
/// Draws <see cref="BannerBeat"/> announcements, in three sizes at three places on screen (#275). One
/// size for everything meant every announcement had the weight of a phase change, so the phase changes
/// stopped landing and nothing else could be announced at all without adding to the noise.
///
/// <list type="bullet">
/// <item><b>Headline</b> — big flashing letters in the upper-center forefront. Reserved for the game's
/// structural spine. This is the only tier that stops play.</item>
/// <item><b>Notice</b> — mid-size, its own band below the headline band.</item>
/// <item><b>Toast</b> — small pills in a ticker under the status HUD, riding along with whatever else
/// is animating.</item>
/// </list>
///
/// <para><b>Every tier stacks (#327).</b> A banner used to be replaced by the next one in its band — a
/// new Notice wiped out the Notice before it, a new Headline replaced the last — which is the same
/// "evicted mid-read" problem the dice stack was built to fix. Now each band grows: the oldest keeps the
/// band's anchor and newer ones pile up ABOVE it, dimmed by depth so the newest reads loudest, matching
/// the roll stack's convention exactly.</para>
///
/// <para>The three bands are laid out in one pass, top down, each yielding to the one above it: toasts
/// take their ticker, headlines stack up to the bottom of the toasts, notices stack up to the bottom of
/// the headlines. Mid-screen is crowded, so when a band runs out of room its OLDEST entries are dropped
/// from the layout — never the newest, which is the one that just happened.</para>
///
/// Each banner fades in fast and out over its tail (the player owns the alpha). Fonts are sized as a
/// fraction of screen height (so they are proportionate on a 1080p laptop and a 4K desktop alike), text
/// word-wraps to the available width, and as a last resort a single over-long word shrinks the font — so
/// no banner ever runs off-screen.
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

    // Gap between stacked entries in the two centered bands, and between one band's stack and the next
    // band's ceiling.
    private const int BandGap = 10;

    // Recency gradient, newest first — the same one the roll stack uses, for the same reason: the
    // current statement reads at full strength and the history behind it steps back.
    private static readonly float[] DepthAlpha = { 1f, 0.6f, 0.4f };

    private static readonly Color ToastPanel = new(20, 20, 24, 200);

    /// <summary>
    /// Draws every live banner (#327), taking the list OLDEST FIRST exactly as
    /// <c>PresentationPlayer.GetBanners</c> returns it. All three tiers arrive together and are split
    /// into their bands here.
    /// </summary>
    public static void Draw(IReadOnlyList<(BannerBeat beat, float alpha)> banners, int areaWidth, int screenH)
    {
        if (banners.Count == 0) return;

        var toasts    = new List<(BannerBeat, float)>();
        var headlines = new List<(BannerBeat, float)>();
        var notices   = new List<(BannerBeat, float)>();
        foreach ((BannerBeat beat, float alpha) in banners)
        {
            switch (beat.Tier)
            {
                case EBannerTier.Toast:  toasts.Add((beat, alpha)); break;
                case EBannerTier.Notice: notices.Add((beat, alpha)); break;
                default:                 headlines.Add((beat, alpha)); break;
            }
        }

        // Top down, each band yielding to the one above it.
        float toastBottom = DrawToasts(toasts, areaWidth, screenH);

        int headlineFont = Math.Clamp((int)Math.Round(screenH * 0.07f), 36, 96);
        float headlineTop = DrawCenteredStack(headlines, areaWidth, (int)(screenH * HeadlineY),
            headlineFont, 0.90f, ceiling: toastBottom + BandGap);

        int noticeFont = Math.Clamp((int)Math.Round(screenH * 0.042f), 24, 56);
        DrawCenteredStack(notices, areaWidth, (int)(screenH * NoticeY), noticeFont, 0.80f,
            ceiling: headlineTop - BandGap);
    }

    /// <summary>
    /// One centered band: the oldest entry sits at <paramref name="anchorY"/> and newer ones stack
    /// upward from it, each dimmed a step further by age. Entries that would rise past
    /// <paramref name="ceiling"/> are dropped from the OLDEST end, so the newest is never the one pushed
    /// off. Returns the y of the topmost drawn entry (or the anchor when nothing drew), which is the next
    /// band's ceiling.
    /// </summary>
    private static float DrawCenteredStack(List<(BannerBeat beat, float alpha)> band, int areaWidth,
        int anchorY, int fontSize, float widthFraction, float ceiling)
    {
        if (band.Count == 0) return anchorY;

        // Measure first: the stack grows UP from the anchor, so an entry's y depends on the heights of
        // everything below it.
        var layouts = new TextLayout[band.Count];
        for (int i = 0; i < band.Count; i++)
            layouts[i] = LayOutText(band[i].beat.BannerText ?? "", fontSize, areaWidth, widthFraction);

        // Drop the OLDEST entries while the stack overflows its ceiling. The newest sits highest, so an
        // overflow always threatens the entry that just happened; dropping from the anchored end shifts
        // the whole stack back down. The newest is never dropped, however tall — better one banner
        // overhanging its band than the latest news not being shown at all.
        var heights = new float[band.Count];
        for (int i = 0; i < band.Count; i++) heights[i] = layouts[i].Height;

        int first = FirstVisible(heights, anchorY, ceiling);

        float y = anchorY;
        for (int i = first; i < band.Count; i++)
        {
            if (i > first) y -= BandGap + layouts[i].Height;

            int depth = band.Count - 1 - i;
            float depthAlpha = DepthAlpha[Math.Min(depth, DepthAlpha.Length - 1)];
            DrawCentered(band[i].beat, layouts[i], band[i].alpha * depthAlpha, (int)y, areaWidth);
        }

        return StackTop(heights, first, anchorY);
    }

    /// <summary>
    /// The oldest entry a band can still show without its stack rising past <paramref name="ceiling"/>.
    /// Entries are dropped from the OLDEST end, which shifts the whole stack back down toward the anchor;
    /// the newest is never dropped, however tall, because it is the thing that just happened. Internal
    /// for tests.
    /// </summary>
    internal static int FirstVisible(IReadOnlyList<float> heights, float anchorY, float ceiling)
    {
        int first = 0;
        while (first < heights.Count - 1 && StackTop(heights, first, anchorY) < ceiling) first++;
        return first;
    }

    /// <summary>
    /// Top edge of a band's stack that starts at <paramref name="first"/>: the oldest entry sits AT the
    /// anchor and every newer one is stacked above it. Internal for tests.
    /// </summary>
    internal static float StackTop(IReadOnlyList<float> heights, int first, float anchorY)
    {
        float y = anchorY;
        for (int i = first + 1; i < heights.Count; i++) y -= BandGap + heights[i];
        return y;
    }

    /// <summary>The toast ticker: pills stacked downward from under the status HUD, oldest at the top
    /// (they read as a running log). Returns the y just below the last one, the headline band's ceiling.</summary>
    private static float DrawToasts(List<(BannerBeat beat, float alpha)> toasts, int areaWidth, int screenH)
    {
        if (toasts.Count == 0) return ToastTop;

        int fontSize = Math.Clamp((int)Math.Round(screenH * 0.022f), 15, 28);
        int lineH    = (int)(fontSize * 1.2f);
        // Rows are laid out at a uniform height so a two-line toast doesn't reflow the stack under it.
        int rowH     = lineH + ToastPadY * 2 + ToastGap;
        int maxWidth = Math.Max(40, (int)(areaWidth * 0.42f));

        for (int row = 0; row < toasts.Count; row++)
        {
            (BannerBeat beat, float alpha) = toasts[row];
            if (alpha <= 0f) continue;

            string text = beat.BannerText ?? "";
            if (text.Length == 0) continue;

            TextLayout layout = LayOutTextTo(text, fontSize, maxWidth);
            int pillW = layout.Widest + ToastPadX * 2;
            int pillH = layout.Lines.Count * lineH + ToastPadY * 2;
            int pillX = (areaWidth - pillW) / 2;
            int pillY = ToastTop + row * rowH;

            byte a = (byte)Math.Clamp(alpha * 255f, 0f, 255f);
            Raylib.DrawRectangleRounded(new Rectangle(pillX, pillY, pillW, pillH), 0.35f, 5,
                Faded(ToastPanel, alpha));

            TextColor c = beat.Color;
            for (int i = 0; i < layout.Lines.Count; i++)
            {
                int w = Raylib.MeasureText(layout.Lines[i], layout.FontSize);
                Raylib.DrawText(layout.Lines[i], pillX + (pillW - w) / 2,
                    pillY + ToastPadY + i * lineH, layout.FontSize, new Color(c.R, c.G, c.B, a));
            }
        }

        return ToastTop + toasts.Count * rowH;
    }

    /// <summary>A wrapped block of banner text and the size it will occupy — measured before the band is
    /// laid out, because a bottom-anchored stack has to size every entry before it can place any.</summary>
    private readonly struct TextLayout
    {
        public readonly List<string> Lines;
        public readonly int FontSize;
        public readonly int Widest;
        public readonly int Height;

        public TextLayout(List<string> lines, int fontSize, int widest, int height)
        {
            Lines = lines;
            FontSize = fontSize;
            Widest = widest;
            Height = height;
        }
    }

    private static TextLayout LayOutText(string text, int fontSize, int areaWidth, float widthFraction) =>
        LayOutTextTo(text, fontSize, Math.Max(40, (int)(areaWidth * widthFraction)));

    private static TextLayout LayOutTextTo(string text, int fontSize, int maxWidth)
    {
        List<string> lines = WrapText(text, fontSize, maxWidth);

        // A single word wider than the available space can't be wrapped — shrink the font to fit it.
        int widest = WidestLine(lines, fontSize);
        if (widest > maxWidth)
        {
            fontSize = Math.Max(11, fontSize * maxWidth / widest);
            lines = WrapText(text, fontSize, maxWidth);
            widest = WidestLine(lines, fontSize);
        }

        return new TextLayout(lines, fontSize, widest, lines.Count * (int)(fontSize * 1.15f));
    }

    // Shared body of the two centered, shadowed tiers (Headline and Notice), drawn from a supplied top y.
    private static void DrawCentered(BannerBeat beat, TextLayout layout, float alpha, int y0, int areaWidth)
    {
        if (alpha <= 0f || layout.Lines.Count == 0) return;

        int lineH  = (int)(layout.FontSize * 1.15f);
        byte a     = (byte)Math.Clamp(alpha * 255f, 0f, 255f);
        byte shadA = (byte)(a * 0.6f);
        TextColor c = beat.Color;

        for (int i = 0; i < layout.Lines.Count; i++)
        {
            int w = Raylib.MeasureText(layout.Lines[i], layout.FontSize);
            int x = (areaWidth - w) / 2;
            int y = y0 + i * lineH;
            // Drop shadow so the text reads over the table.
            Raylib.DrawText(layout.Lines[i], x + 3, y + 3, layout.FontSize,
                new Color((byte)0, (byte)0, (byte)0, shadA));
            Raylib.DrawText(layout.Lines[i], x, y, layout.FontSize, new Color(c.R, c.G, c.B, a));
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
}
