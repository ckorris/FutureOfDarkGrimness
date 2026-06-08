using System;
using System.Collections.Generic;
using FDG;
using FDG.Presentation.Beats;
using Raylib_cs;

namespace FdgRaylib.Rendering.Presentation;

/// <summary>
/// Draws the active <see cref="DiceRolledBeat"/> in a screen-space strip across the bottom-center,
/// in the forefront over the table. Two vocabularies keyed off the roller mode:
/// <list type="bullet">
/// <item><b>Realistic</b> — the actual dice as pip faces; successes green, failures gray; a brief
/// "settle" flicker at the start reads as rolling.</item>
/// <item><b>Probabilistic</b> — no discrete dice exist (fractional), so a labeled success bar.</item>
/// </list>
/// </summary>
public static class DiceOverlay
{
    private const float FlickerEnd = 0.4f; // fraction of the beat spent "rolling" before faces lock

    private static readonly Color Panel   = new(20, 20, 24, 210);
    private static readonly Color Success = new(60, 170, 70, 255);
    private static readonly Color Fail    = new(110, 110, 110, 255);
    private static readonly Color Rolling = new(225, 225, 225, 255);
    private static readonly Color Label   = new(235, 235, 235, 255);

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

        string text = beat.Text ?? "";
        const int fontSize = 22;

        if (faces.Count == 0)
        {
            DrawCenteredPanelWithText(text, areaWidth, screenH, fontSize);
            return;
        }

        // Fit the row within the area; shrink the die size if there are many dice.
        int gap = 8;
        float maxRow = areaWidth - 80;
        int dieSize = 44;
        if (faces.Count * (dieSize + gap) > maxRow)
            dieSize = Math.Max(16, (int)(maxRow / faces.Count) - gap);

        int rowW = faces.Count * dieSize + (faces.Count - 1) * gap;
        int panelPad = 16;
        int panelW = Math.Max(rowW, Raylib.MeasureText(text, fontSize)) + panelPad * 2;
        int panelH = dieSize + fontSize + panelPad * 2 + 8;
        int panelX = (areaWidth - panelW) / 2;
        int panelY = screenH - panelH - 24;

        Raylib.DrawRectangleRounded(new Rectangle(panelX, panelY, panelW, panelH), 0.18f, 6, Panel);

        int textW = Raylib.MeasureText(text, fontSize);
        Raylib.DrawText(text, (areaWidth - textW) / 2, panelY + panelPad, fontSize, Label);

        bool settled = progress >= FlickerEnd;
        int rowX = (areaWidth - rowW) / 2;
        int rowY = panelY + panelPad + fontSize + 8;

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
            DrawDie(x, rowY, dieSize, shownFace, fill, pip);
        }
    }

    private static void DrawProbabilistic(DiceRolledBeat beat, int areaWidth, int screenH)
    {
        const int fontSize = 22;
        int barW = Math.Min(360, areaWidth - 120);
        int barH = 22;
        int panelPad = 16;
        int panelW = barW + panelPad * 2;
        int panelH = fontSize + barH + panelPad * 2 + 8;
        int panelX = (areaWidth - panelW) / 2;
        int panelY = screenH - panelH - 24;

        Raylib.DrawRectangleRounded(new Rectangle(panelX, panelY, panelW, panelH), 0.18f, 6, Panel);

        string text = beat.Text ?? "";
        int textW = Raylib.MeasureText(text, fontSize);
        Raylib.DrawText(text, (areaWidth - textW) / 2, panelY + panelPad, fontSize, Label);

        int barX = (areaWidth - barW) / 2;
        int barY = panelY + panelPad + fontSize + 8;
        Raylib.DrawRectangle(barX, barY, barW, barH, Fail);
        float frac = beat.Total > 0f ? beat.Successes / beat.Total : 0f;
        Raylib.DrawRectangle(barX, barY, (int)(barW * Math.Clamp(frac, 0f, 1f)), barH, Success);
        Raylib.DrawRectangleLines(barX, barY, barW, barH, Color.Black);
    }

    private static void DrawCenteredPanelWithText(string text, int areaWidth, int screenH, int fontSize)
    {
        int panelPad = 16;
        int panelW = Raylib.MeasureText(text, fontSize) + panelPad * 2;
        int panelH = fontSize + panelPad * 2;
        int panelX = (areaWidth - panelW) / 2;
        int panelY = screenH - panelH - 24;
        Raylib.DrawRectangleRounded(new Rectangle(panelX, panelY, panelW, panelH), 0.18f, 6, Panel);
        int textW = Raylib.MeasureText(text, fontSize);
        Raylib.DrawText(text, (areaWidth - textW) / 2, panelY + panelPad, fontSize, Label);
    }

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
