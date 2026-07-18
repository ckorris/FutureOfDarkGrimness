using System;
using System.Collections.Generic;
using FDG;
using FDG.Presentation.Beats;
using Raylib_cs;

namespace FdgRaylib.Rendering.Presentation;

/// <summary>
/// Draws the active <see cref="DiceRolledBeat"/> as a lower-third caption strip docked to the
/// bottom-center of the table viewport (#244) — the subtitle convention: the action plays out on
/// the table while the numbers narrate from the caption zone, never covering the units or the
/// concurrent attack animation (#238). The panel is: a standalone <b>target badge</b> (the success
/// threshold, e.g. "4+", big enough to read before the dice settle), a <b>header</b> with the
/// roll's purpose (<see cref="DiceRolledBeat.Label"/>), the dice themselves, and a <b>result
/// line</b> with the settled outcome (<see cref="DiceRolledBeat.ResultSummary"/>).
///
/// <para>If the attack animation's screen bounds still reach the strip (units fighting at the
/// bottom table edge), the panel fades to a ghost instead of moving — consistent anchor, graceful
/// degradation.</para>
///
/// <para>Two vocabularies keyed off the roller mode:</para>
/// <list type="bullet">
/// <item><b>Realistic</b> — the actual dice as pip faces; successes green, failures gray; a brief
/// "settle" tumble at the start reads as rolling.</item>
/// <item><b>Probabilistic</b> — no discrete dice exist (fractional), so a labeled success bar.</item>
/// </list>
///
/// <para><see cref="DrawRollOff"/> deliberately stays centered: a roll-off is rare, decisive, and
/// plays with nothing else animating, so it keeps the center-stage treatment routine rolls
/// don't get.</para>
/// </summary>
public static class DiceOverlay
{
    private const float FlickerEnd = 0.3f; // fraction of the beat spent "rolling" before faces lock; rest lingers settled
    private const float TumbleHz   = 9f;   // face-change rate while rolling (per-frame swaps strobe)

    private const int HeaderSize   = 22;
    private const int ResultSize   = 20;
    private const int BadgeSize    = 40;   // the standalone target number
    private const int BadgePad     = 10;
    private const int PanelPad     = 16;
    private const int RowGap       = 8;
    private const int ColGap       = 16;
    private const int BottomMargin = 18;

    private const float OverlapDim = 0.35f; // ghost alpha while the attack animation overlaps the strip

    private static readonly Color Panel    = new(20, 20, 24, 210);
    private static readonly Color BadgeBg  = new(42, 38, 26, 230);
    private static readonly Color Success  = new(60, 170, 70, 255);
    private static readonly Color Fail     = new(110, 110, 110, 255);
    private static readonly Color Rolling  = new(225, 225, 225, 255);
    private static readonly Color Header   = new(235, 235, 235, 255);
    private static readonly Color Result   = new(255, 225, 150, 255); // gold — the settled "what it means"
    private static readonly Color Hint     = new(170, 170, 175, 255); // dim — the "..." while rolling
    private static readonly Color Tie      = new(228, 200, 60, 255);  // yellow — tied for the win (re-rolls)

    // Smoothed overlap dim so the ghosting eases instead of stepping. Render-thread only.
    private static float  _dim = 1f;
    private static double _lastDrawTime;

    public static void Draw(DiceRolledBeat beat, float progress, float alpha, int areaWidth, int screenH,
        Rectangle? avoid)
    {
        if (beat.Mode == ERandomnessType.Probabilistic)
            DrawProbabilistic(beat, alpha, areaWidth, screenH, avoid);
        else
            DrawRealistic(beat, progress, alpha, areaWidth, screenH, avoid);
    }

    private static void DrawRealistic(DiceRolledBeat beat, float progress, float alpha, int areaWidth,
        int screenH, Rectangle? avoid)
    {
        // Expand the histogram into individual dice (rounded — realistic counts are whole numbers).
        var faces = new List<int>();
        for (int i = 0; i < beat.FaceCounts.Count; i++)
        {
            int count = (int)MathF.Round(beat.FaceCounts[i]);
            for (int n = 0; n < count; n++) faces.Add(beat.SideMin + i);
        }

        bool settled = progress >= FlickerEnd;
        string header = beat.Label;
        // The settled text is known from the start (the tumble is purely cosmetic), so the panel is
        // sized for it up front and never reflows at the settle instant.
        string result = ResultText(beat);
        string badge  = $"{beat.SuccessThreshold}+";

        // Size the dice row (shrink the die if there are many).
        int gap = 8;
        int dieSize = 44;
        if (faces.Count > 0)
        {
            float maxRow = areaWidth - 240; // leave room for the badge column + margins
            if (faces.Count * (dieSize + gap) > maxRow)
                dieSize = Math.Max(16, (int)(maxRow / faces.Count) - gap);
        }
        int rowW  = faces.Count > 0 ? faces.Count * dieSize + (faces.Count - 1) * gap : 0;
        int diceH = faces.Count > 0 ? dieSize : 0;

        int badgeW = Raylib.MeasureText(badge, BadgeSize) + BadgePad * 2;
        int badgeH = BadgeSize + BadgePad * 2;

        int contentW = Max3(Raylib.MeasureText(header, HeaderSize), rowW, Raylib.MeasureText(result, ResultSize));
        int contentH = HeaderSize + RowGap + (diceH > 0 ? diceH + RowGap : 0) + ResultSize;

        int panelW = PanelPad + badgeW + ColGap + contentW + PanelPad;
        int panelH = PanelPad * 2 + Math.Max(contentH, badgeH);
        int panelX = (areaWidth - panelW) / 2;
        int panelY = screenH - panelH - BottomMargin;

        var panelRect = new Rectangle(panelX, panelY, panelW, panelH);
        float a = alpha * UpdateDim(panelRect, avoid);
        if (a <= 0.02f) return;

        Raylib.DrawRectangleRounded(panelRect, 0.18f, 6, Faded(Panel, a));

        DrawBadge(panelX + PanelPad, panelY + (panelH - badgeH) / 2, badgeW, badgeH, badge, a);

        // Content column, centered within its own span (the badge offsets it from the panel center).
        int contentX = panelX + PanelPad + badgeW + ColGap;
        int y = panelY + (panelH - contentH) / 2;
        DrawCenteredIn(header, contentX, contentW, y, HeaderSize, Faded(Header, a));
        y += HeaderSize + RowGap;

        if (diceH > 0)
        {
            int rowX = contentX + (contentW - rowW) / 2;
            for (int i = 0; i < faces.Count; i++)
            {
                int x = rowX + i * (dieSize + gap);
                int shownFace;
                Color fill, pip;
                if (settled)
                {
                    shownFace = faces[i];
                    bool success = shownFace >= beat.SuccessThreshold;
                    fill = success ? Success : Fail;
                    pip = Color.White;
                }
                else
                {
                    shownFace = TumbleFace(i, beat.SideMin, beat.SideMax);
                    fill = Rolling;
                    pip = new Color(30, 30, 30, 255);
                }
                DrawDie(x, y, dieSize, shownFace, fill, pip, a);
            }
            y += dieSize + RowGap;
        }

        DrawCenteredIn(settled ? result : "...", contentX, contentW, y, ResultSize,
            Faded(settled ? Result : Hint, a));
    }

    private static void DrawProbabilistic(DiceRolledBeat beat, float alpha, int areaWidth, int screenH,
        Rectangle? avoid)
    {
        // No discrete dice exist under the probabilistic roller, so there's no "rolling" phase —
        // show the result immediately.
        string header = beat.Label;
        string result = ResultText(beat);
        string badge  = $"{beat.SuccessThreshold}+";

        int barW = Math.Min(360, areaWidth - 240);
        int barH = 22;

        int badgeW = Raylib.MeasureText(badge, BadgeSize) + BadgePad * 2;
        int badgeH = BadgeSize + BadgePad * 2;

        int contentW = Max3(Raylib.MeasureText(header, HeaderSize), barW, Raylib.MeasureText(result, ResultSize));
        int contentH = HeaderSize + RowGap + barH + RowGap + ResultSize;

        int panelW = PanelPad + badgeW + ColGap + contentW + PanelPad;
        int panelH = PanelPad * 2 + Math.Max(contentH, badgeH);
        int panelX = (areaWidth - panelW) / 2;
        int panelY = screenH - panelH - BottomMargin;

        var panelRect = new Rectangle(panelX, panelY, panelW, panelH);
        float a = alpha * UpdateDim(panelRect, avoid);
        if (a <= 0.02f) return;

        Raylib.DrawRectangleRounded(panelRect, 0.18f, 6, Faded(Panel, a));

        DrawBadge(panelX + PanelPad, panelY + (panelH - badgeH) / 2, badgeW, badgeH, badge, a);

        int contentX = panelX + PanelPad + badgeW + ColGap;
        int y = panelY + (panelH - contentH) / 2;
        DrawCenteredIn(header, contentX, contentW, y, HeaderSize, Faded(Header, a));
        y += HeaderSize + RowGap;

        int barX = contentX + (contentW - barW) / 2;
        Raylib.DrawRectangle(barX, y, barW, barH, Faded(Fail, a));
        float frac = beat.Total > 0f ? beat.Successes / beat.Total : 0f;
        Raylib.DrawRectangle(barX, y, (int)(barW * Math.Clamp(frac, 0f, 1f)), barH, Faded(Success, a));
        Raylib.DrawRectangleLines(barX, y, barW, barH, Faded(Color.Black, a));
        y += barH + RowGap;

        DrawCenteredIn(result, contentX, contentW, y, ResultSize, Faded(Result, a));
    }

    /// <summary>
    /// Draws a <see cref="RollOffBeat"/> as a labelled stack — each competitor's name on the left, its
    /// die on the right — so it's clear who's rolling against whom. The sole highest roller's die turns
    /// green (Won); a shared highest turns yellow (TiedForWin) and the engine emits a fresh beat for the
    /// run-off. Dice tumble for the first fraction of the beat, then settle to the rolled face + colour.
    /// Stays centered on purpose — roll-offs are rare, decisive, and play with nothing else animating.
    /// </summary>
    public static void DrawRollOff(RollOffBeat beat, float progress, int areaWidth, int screenH)
    {
        if (beat.Entries == null || beat.Entries.Count == 0) return;

        bool settled = progress >= FlickerEnd;
        const int nameFont = 22;
        const int dieSize  = 44;
        const int rowGap   = 10;
        const int colGap   = 18;

        int nameColW = 0;
        foreach (RollOffEntry e in beat.Entries)
            nameColW = Math.Max(nameColW, Raylib.MeasureText(e.Name, nameFont));

        int rowsH  = beat.Entries.Count * dieSize + (beat.Entries.Count - 1) * rowGap;
        int innerW = Math.Max(Raylib.MeasureText(beat.Label, HeaderSize), nameColW + colGap + dieSize);
        int panelW = innerW + PanelPad * 2;
        int panelH = PanelPad * 2 + HeaderSize + RowGap + rowsH;
        int panelX = (areaWidth - panelW) / 2;
        int panelY = (int)((screenH - panelH) * 0.45f);

        Raylib.DrawRectangleRounded(new Rectangle(panelX, panelY, panelW, panelH), 0.12f, 6, Panel);
        DrawCenteredIn(beat.Label, panelX, panelW, panelY + PanelPad, HeaderSize, Header);

        int rowTop = panelY + PanelPad + HeaderSize + RowGap;
        int nameX  = panelX + PanelPad;
        int dieX   = panelX + PanelPad + nameColW + colGap;
        for (int i = 0; i < beat.Entries.Count; i++)
        {
            RollOffEntry e = beat.Entries[i];
            int rowY = rowTop + i * (dieSize + rowGap);
            Raylib.DrawText(e.Name, nameX, rowY + (dieSize - nameFont) / 2, nameFont, Header);

            int face;
            Color fill, pip;
            if (settled)
            {
                face = e.Roll;
                fill = e.Result switch
                {
                    ERollOffResult.Won        => Success,
                    ERollOffResult.TiedForWin => Tie,
                    _                         => Fail,
                };
                pip = Color.White;
            }
            else
            {
                face = TumbleFace(i, 1, 6);
                fill = Rolling;
                pip  = new Color((byte)30, (byte)30, (byte)30, (byte)255);
            }
            DrawDie(dieX, rowY, dieSize, face, fill, pip, 1f);
        }
    }

    // The settled result line: the stage-supplied summary, or a generic successes/total fallback.
    private static string ResultText(DiceRolledBeat beat) =>
        beat.ResultSummary ?? $"{beat.Successes:0.##} / {beat.Total:0.##}";

    // The big standalone target number ("4+") — readable at a glance before the dice settle, and
    // still there afterwards so the settled faces can be interpreted without re-reading anything.
    private static void DrawBadge(int x, int y, int w, int h, string text, float a)
    {
        var rect = new Rectangle(x, y, w, h);
        Raylib.DrawRectangleRounded(rect, 0.25f, 6, Faded(BadgeBg, a));
        Raylib.DrawRectangleRoundedLines(rect, 0.25f, 6, Faded(Result, a));
        int tw = Raylib.MeasureText(text, BadgeSize);
        Raylib.DrawText(text, x + (w - tw) / 2, y + (h - BadgeSize) / 2, BadgeSize, Faded(Result, a));
    }

    // While tumbling, faces swap at TumbleHz instead of every frame — reads as rolling without
    // strobing. Deterministic per (die, time slice), cosmetic only.
    private static int TumbleFace(int i, int min, int max)
    {
        int phase = (int)(Raylib.GetTime() * TumbleHz);
        uint h = (uint)(i * 73856093) ^ (uint)(phase * 19349663);
        return min + (int)(h % (uint)Math.Max(1, max - min + 1));
    }

    // Ghost the panel while the attack animation's bounds reach it, easing between states.
    private static float UpdateDim(Rectangle panel, Rectangle? avoid)
    {
        double now = Raylib.GetTime();
        float dt = (float)(now - _lastDrawTime);
        _lastDrawTime = now;
        if (dt > 0.25f) _dim = 1f; // the panel just (re)appeared — start fresh, not from stale state

        float target = avoid.HasValue && Raylib.CheckCollisionRecs(panel, avoid.Value) ? OverlapDim : 1f;
        _dim += (target - _dim) * Math.Clamp(dt * 10f, 0f, 1f);
        return _dim;
    }

    private static Color Faded(Color c, float a) =>
        new(c.R, c.G, c.B, (byte)Math.Clamp(c.A * Math.Clamp(a, 0f, 1f), 0f, 255f));

    private static void DrawCenteredIn(string text, int x, int width, int y, int fontSize, Color color)
    {
        int w = Raylib.MeasureText(text, fontSize);
        Raylib.DrawText(text, x + (width - w) / 2, y, fontSize, color);
    }

    private static int Max3(int a, int b, int c) => Math.Max(a, Math.Max(b, c));

    // Standard d6 pip layout on a 3x3 grid (col, row), 0..2.
    private static readonly Dictionary<int, (int, int)[]> PipCells = new()
    {
        [1] = new[] { (1, 1) },
        [2] = new[] { (0, 0), (2, 2) },
        [3] = new[] { (0, 0), (1, 1), (2, 2) },
        [4] = new[] { (0, 0), (2, 0), (0, 2), (2, 2) },
        [5] = new[] { (0, 0), (2, 0), (1, 1), (0, 2), (2, 2) },
        [6] = new[] { (0, 0), (2, 0), (0, 1), (2, 1), (0, 2), (2, 2) },
    };

    private static void DrawDie(int x, int y, int size, int face, Color fill, Color pip, float a)
    {
        Raylib.DrawRectangleRounded(new Rectangle(x, y, size, size), 0.22f, 6, Faded(fill, a));
        Raylib.DrawRectangleRoundedLines(new Rectangle(x, y, size, size), 0.22f, 6, Faded(Color.Black, a));

        if (!PipCells.TryGetValue(face, out var cells)) return;

        float pad = size * 0.24f;
        float step = (size - pad * 2f) / 2f;
        float r = size * 0.085f;
        Color pipFaded = Faded(pip, a);
        foreach (var (col, row) in cells)
        {
            float px = x + pad + col * step;
            float py = y + pad + row * step;
            Raylib.DrawCircle((int)px, (int)py, r, pipFaded);
        }
    }
}
