using System;
using System.Collections.Generic;
using Raylib_cs;

namespace FdgRaylib.Rendering;

/// <summary>
/// A persistent top-center status strip over the table: the current round ("ROUND 2 / 4") and a live
/// objective scoreboard (one player-colored pip + controlled-objective count per player). Answers the
/// two questions the log otherwise buries -- what round is it, and who's ahead.
///
/// Drawn with Raylib primitives in the same pass as the other table overlays (banners, dice), so it sits
/// over the board but under the ImGui panels and never captures the mouse. Purely presentational: the
/// renderer gathers the round from <c>ITableState.Progress</c> and the counts from the objectives.
/// </summary>
public static class StatusHudOverlay
{
    private const int   Pad        = 14;
    private const int   FontSize   = 24;
    private const int   Gap        = 16;
    private const int   PipGap     = 7;
    private const int   PipRadius  = 9;
    private const int   TopMargin  = 12;

    private static readonly Color Panel   = new(20, 20, 24, 210);
    private static readonly Color Border  = new(64, 71, 82, 180);
    private static readonly Color Label   = new(235, 235, 235, 255);
    private static readonly Color RoundClr = new(105, 170, 245, 255); // blue, matches the UI accent
    private static readonly Color SepClr   = new(90, 96, 106, 200);

    /// <summary>
    /// Renders the strip centered in <paramref name="areaWidth"/> (the table area, excluding the log
    /// panel). Pass <paramref name="round"/> null before the main phase (only the scoreboard shows).
    /// <paramref name="scores"/> is one entry per player, in a stable order.
    /// </summary>
    public static void Draw(int areaWidth, int? round, int totalRounds,
        IReadOnlyList<(Color color, int count)> scores)
    {
        bool hasRound  = round.HasValue;
        bool hasScores = scores.Count > 0;
        if (!hasRound && !hasScores) return;

        string roundText = hasRound ? $"ROUND {round} / {totalRounds}" : "";

        int roundW  = hasRound ? Raylib.MeasureText(roundText, FontSize) : 0;
        int sepW    = (hasRound && hasScores) ? Gap * 2 + 1 : 0;

        int scoresW = 0;
        for (int i = 0; i < scores.Count; i++)
        {
            scoresW += PipRadius * 2 + PipGap + Raylib.MeasureText(scores[i].count.ToString(), FontSize);
            if (i < scores.Count - 1) scoresW += Gap;
        }

        int innerW = roundW + sepW + scoresW;
        int panelW = innerW + Pad * 2;
        int panelH = Pad * 2 + FontSize;
        int panelX = (areaWidth - panelW) / 2;
        int panelY = TopMargin;

        Raylib.DrawRectangleRounded(new Rectangle(panelX, panelY, panelW, panelH), 0.28f, 6, Panel);
        Raylib.DrawRectangleRoundedLines(new Rectangle(panelX, panelY, panelW, panelH), 0.28f, 6, Border);

        int x       = panelX + Pad;
        int textY   = panelY + Pad;
        int centerY = panelY + panelH / 2;

        if (hasRound)
        {
            Raylib.DrawText(roundText, x, textY, FontSize, RoundClr);
            x += roundW;
        }

        if (hasRound && hasScores)
        {
            x += Gap;
            Raylib.DrawLine(x, panelY + Pad / 2, x, panelY + panelH - Pad / 2, SepClr);
            x += Gap;
        }

        for (int i = 0; i < scores.Count; i++)
        {
            var (color, count) = scores[i];
            int pipCx = x + PipRadius;
            Raylib.DrawCircle(pipCx, centerY, PipRadius, color);
            Raylib.DrawCircleLines(pipCx, centerY, PipRadius, Color.Black);
            x += PipRadius * 2 + PipGap;

            string cnt = count.ToString();
            Raylib.DrawText(cnt, x, textY, FontSize, Label);
            x += Raylib.MeasureText(cnt, FontSize) + Gap;
        }
    }
}
