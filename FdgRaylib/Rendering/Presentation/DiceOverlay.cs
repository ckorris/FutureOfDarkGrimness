using System;
using System.Collections.Generic;
using FDG;
using FDG.Presentation.Beats;
using Raylib_cs;

namespace FdgRaylib.Rendering.Presentation;

/// <summary>
/// Draws the active <see cref="DiceRolledBeat"/> in a screen-space strip across the bottom-center,
/// in the forefront over the table. The panel is a built-in "spoken at the table" caption: a
/// <b>header</b> with the roll's purpose (<see cref="DiceRolledBeat.Label"/>, e.g. "Roll to Save"),
/// the dice themselves, and a <b>result line</b> that reads "needs X+" while the dice tumble and then
/// the settled outcome (<see cref="DiceRolledBeat.ResultSummary"/>, e.g. "2 saved, 3 wounds") once
/// the faces lock.
///
/// <para>Two vocabularies keyed off the roller mode:</para>
/// <list type="bullet">
/// <item><b>Realistic</b> — the actual dice as pip faces; successes green, failures gray; a brief
/// "settle" flicker at the start reads as rolling.</item>
/// <item><b>Probabilistic</b> — no discrete dice exist (fractional), so a labeled success bar.</item>
/// </list>
/// </summary>
public static class DiceOverlay
{
    private const float FlickerEnd = 0.3f; // fraction of the beat spent "rolling" before faces lock; rest lingers settled

    private const int HeaderSize = 22;
    private const int ResultSize = 20;
    private const int PanelPad   = 16;
    private const int RowGap     = 8;

    private static readonly Color Panel   = new(20, 20, 24, 210);
    private static readonly Color Success = new(60, 170, 70, 255);
    private static readonly Color Fail    = new(110, 110, 110, 255);
    private static readonly Color Rolling = new(225, 225, 225, 255);
    private static readonly Color Header  = new(235, 235, 235, 255);
    private static readonly Color Result  = new(255, 225, 150, 255); // gold — the settled "what it means"
    private static readonly Color Hint    = new(170, 170, 175, 255); // dim — the "needs X+" while rolling

    public static void Draw(DiceRolledBeat beat, float progress, int areaWidth, int screenH)
    {
        if (beat.Mode == ERandomnessType.Probabilistic)
            DrawProbabilistic(beat, areaWidth, screenH);
        else
            DrawRealistic(beat, progress, areaWidth, screenH);
    }

    private static void DrawRealistic(DiceRolledBeat beat, float progress, int areaWidth, int screenH)
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
        string result = ResultText(beat, settled);

        // Size the dice row (shrink the die if there are many).
        int gap = 8;
        int dieSize = 44;
        if (faces.Count > 0)
        {
            float maxRow = areaWidth - 80;
            if (faces.Count * (dieSize + gap) > maxRow)
                dieSize = Math.Max(16, (int)(maxRow / faces.Count) - gap);
        }
        int rowW  = faces.Count > 0 ? faces.Count * dieSize + (faces.Count - 1) * gap : 0;
        int diceH = faces.Count > 0 ? dieSize : 0;

        int innerW = Max3(Raylib.MeasureText(header, HeaderSize), rowW, Raylib.MeasureText(result, ResultSize));
        int panelW = innerW + PanelPad * 2;
        int panelH = PanelPad * 2 + HeaderSize + RowGap + (diceH > 0 ? diceH + RowGap : 0) + ResultSize;
        int panelX = (areaWidth - panelW) / 2;
        int panelY = (int)((screenH - panelH) * 0.45f); // near center, slightly above the middle

        Raylib.DrawRectangleRounded(new Rectangle(panelX, panelY, panelW, panelH), 0.18f, 6, Panel);

        int y = panelY + PanelPad;
        DrawCentered(header, areaWidth, y, HeaderSize, Header);
        y += HeaderSize + RowGap;

        if (diceH > 0)
        {
            int rowX = (areaWidth - rowW) / 2;
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
                    // Cycle faces while rolling. Cosmetic, so per-frame randomness is fine in app code.
                    shownFace = Raylib.GetRandomValue(beat.SideMin, beat.SideMax);
                    fill = Rolling;
                    pip = new Color(30, 30, 30, 255);
                }
                DrawDie(x, y, dieSize, shownFace, fill, pip);
            }
            y += dieSize + RowGap;
        }

        DrawCentered(result, areaWidth, y, ResultSize, settled ? Result : Hint);
    }

    private static void DrawProbabilistic(DiceRolledBeat beat, int areaWidth, int screenH)
    {
        // No discrete dice exist under the probabilistic roller, so there's no "rolling" phase —
        // show the result immediately.
        string header = beat.Label;
        string result = ResultText(beat, settled: true);

        int barW = Math.Min(360, areaWidth - 120);
        int barH = 22;
        int innerW = Max3(Raylib.MeasureText(header, HeaderSize), barW, Raylib.MeasureText(result, ResultSize));
        int panelW = innerW + PanelPad * 2;
        int panelH = PanelPad * 2 + HeaderSize + RowGap + barH + RowGap + ResultSize;
        int panelX = (areaWidth - panelW) / 2;
        int panelY = (int)((screenH - panelH) * 0.45f); // near center, slightly above the middle

        Raylib.DrawRectangleRounded(new Rectangle(panelX, panelY, panelW, panelH), 0.18f, 6, Panel);

        int y = panelY + PanelPad;
        DrawCentered(header, areaWidth, y, HeaderSize, Header);
        y += HeaderSize + RowGap;

        int barX = (areaWidth - barW) / 2;
        Raylib.DrawRectangle(barX, y, barW, barH, Fail);
        float frac = beat.Total > 0f ? beat.Successes / beat.Total : 0f;
        Raylib.DrawRectangle(barX, y, (int)(barW * Math.Clamp(frac, 0f, 1f)), barH, Success);
        Raylib.DrawRectangleLines(barX, y, barW, barH, Color.Black);
        y += barH + RowGap;

        DrawCentered(result, areaWidth, y, ResultSize, Result);
    }

    // The result line: while the dice tumble, what's needed; once settled, the stage-supplied summary
    // (or a generic successes/total fallback).
    private static string ResultText(DiceRolledBeat beat, bool settled)
    {
        if (!settled) return $"needs {beat.SuccessThreshold}+";
        return beat.ResultSummary ?? $"{beat.Successes:0.##} / {beat.Total:0.##}";
    }

    private static void DrawCentered(string text, int areaWidth, int y, int fontSize, Color color)
    {
        int w = Raylib.MeasureText(text, fontSize);
        Raylib.DrawText(text, (areaWidth - w) / 2, y, fontSize, color);
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

    private static void DrawDie(int x, int y, int size, int face, Color fill, Color pip)
    {
        Raylib.DrawRectangleRounded(new Rectangle(x, y, size, size), 0.22f, 6, fill);
        Raylib.DrawRectangleRoundedLines(new Rectangle(x, y, size, size), 0.22f, 6, Color.Black);

        if (!PipCells.TryGetValue(face, out var cells)) return;

        float pad = size * 0.24f;
        float step = (size - pad * 2f) / 2f;
        float r = size * 0.085f;
        foreach (var (col, row) in cells)
        {
            float px = x + pad + col * step;
            float py = y + pad + row * step;
            Raylib.DrawCircle((int)px, (int)py, r, pip);
        }
    }
}
