using System;
using System.Collections.Generic;
using Raylib_cs;

namespace FdgRaylib.Rendering;

/// <summary>
/// A persistent top-center status strip over the table: the current round ("ROUND 2 / 4"), a live
/// objective scoreboard (one player-colored pip + controlled-objective count per player), and -- when
/// another player holds up the game (#322) -- a smaller "Waiting on Bob: Place Unit Models" line
/// beneath, one per outstanding non-local task. Answers the three questions the log otherwise
/// buries -- what round is it, who's ahead, and why is nothing happening.
///
/// Drawn with Raylib primitives in the same pass as the other table overlays (banners, dice), so it sits
/// over the board but under the ImGui panels and never captures the mouse. Purely presentational: the
/// renderer gathers the round from <c>ITableState.Progress</c> and the counts from the objectives.
/// </summary>
public static class StatusHudOverlay
{
    private const int   FontSize   = 24;
    private const int   Gap        = 16;
    private const int   PipGap     = 7;
    private const int   PipRadius  = 9;
    private const int   TopMargin  = 12;

    // #322 "Waiting on" lines: smaller than the main strip, capped so a many-player pileup can't
    // curtain the board.
    private const int   WaitFontSize = 20;
    private const int   WaitLineGap  = 5;
    private const int   MaxWaitLines = 3;

    private static readonly Color Label   = new(235, 235, 235, 255);
    private static readonly Color RoundClr = new(105, 170, 245, 255); // blue, matches the UI accent
    private static readonly Color SepClr   = new(90, 96, 106, 200);
    private static readonly Color WaitDim  = new(190, 196, 205, 235);

    /// <summary>
    /// Renders the strip centered across <paramref name="areaWidth"/> (the full table width). Pass
    /// <paramref name="round"/> null before the main phase (only the scoreboard shows).
    /// <paramref name="scores"/> is one entry per player, in a stable order.
    /// <paramref name="waiting"/> is one entry per outstanding non-local task, oldest first (may be
    /// empty; during deployment it is often the only thing on screen).
    /// </summary>
    public static void Draw(int areaWidth, int? round, int totalRounds,
        IReadOnlyList<(Color color, int count)> scores,
        IReadOnlyList<(Color color, string playerName, string taskName)> waiting)
    {
        bool hasRound  = round.HasValue;
        bool hasScores = scores.Count > 0;
        if (!hasRound && !hasScores && waiting.Count == 0) return;

        string roundText = hasRound ? $"ROUND {round} / {totalRounds}" : "";

        int roundW  = hasRound ? Raylib.MeasureText(roundText, FontSize) : 0;
        int sepW    = (hasRound && hasScores) ? Gap * 2 + 1 : 0;

        int scoresW = 0;
        for (int i = 0; i < scores.Count; i++)
        {
            scoresW += PipRadius * 2 + PipGap + Raylib.MeasureText(scores[i].count.ToString(), FontSize);
            if (i < scores.Count - 1) scoresW += Gap;
        }

        // No background panel -- it would block the table behind it. Everything is drawn straight onto the
        // board with drop shadows for legibility. (This overlay is Raylib-drawn, not an ImGui window, so it
        // has never captured the mouse -- clicks always pass through to the table underneath.)
        int innerW  = roundW + sepW + scoresW;
        int x       = (areaWidth - innerW) / 2;
        int textY   = TopMargin;
        int centerY = TopMargin + FontSize / 2;

        if (hasRound)
        {
            DrawTextShadow(roundText, x, textY, RoundClr);
            x += roundW;
        }

        if (hasRound && hasScores)
        {
            x += Gap;
            Raylib.DrawLine(x, textY + 3, x, textY + FontSize - 3, SepClr);
            x += Gap;
        }

        for (int i = 0; i < scores.Count; i++)
        {
            var (color, count) = scores[i];
            int pipCx = x + PipRadius;
            Raylib.DrawCircle(pipCx, centerY + 1, PipRadius, Shadow);   // pip shadow
            Raylib.DrawCircle(pipCx, centerY, PipRadius, color);
            Raylib.DrawCircleLines(pipCx, centerY, PipRadius, Color.Black);
            x += PipRadius * 2 + PipGap;

            string cnt = count.ToString();
            DrawTextShadow(cnt, x, textY, Label);
            x += Raylib.MeasureText(cnt, FontSize) + Gap;
        }

        DrawWaitingLines(areaWidth, waiting, startY: (hasRound || hasScores)
            ? TopMargin + FontSize + 8
            : TopMargin);
    }

    // One centered line per non-local outstanding task: dim "Waiting on ", the player's name in their
    // table color, dim ": <task>". Lines past the cap collapse into "+N more".
    private static void DrawWaitingLines(int areaWidth,
        IReadOnlyList<(Color color, string playerName, string taskName)> waiting, int startY)
    {
        int y = startY;
        int shown = Math.Min(waiting.Count, MaxWaitLines);
        for (int i = 0; i < shown; i++)
        {
            var (color, playerName, taskName) = waiting[i];
            string prefix = "Waiting on ";
            string rest   = $": {taskName}";
            int prefixW = Raylib.MeasureText(prefix, WaitFontSize);
            int nameW   = Raylib.MeasureText(playerName, WaitFontSize);
            int restW   = Raylib.MeasureText(rest, WaitFontSize);

            int x = (areaWidth - (prefixW + nameW + restW)) / 2;
            DrawTextShadow(prefix, x, y, WaitDim, WaitFontSize);
            DrawTextShadow(playerName, x + prefixW, y, color, WaitFontSize);
            DrawTextShadow(rest, x + prefixW + nameW, y, WaitDim, WaitFontSize);
            y += WaitFontSize + WaitLineGap;
        }

        if (waiting.Count > shown)
        {
            string more = $"+{waiting.Count - shown} more";
            DrawTextShadow(more, (areaWidth - Raylib.MeasureText(more, WaitFontSize)) / 2, y,
                WaitDim, WaitFontSize);
        }
    }

    private static readonly Color Shadow = new(0, 0, 0, 190);

    private static void DrawTextShadow(string text, int x, int y, Color color, int size = FontSize)
    {
        Raylib.DrawText(text, x + 1, y + 1, size, Shadow);
        Raylib.DrawText(text, x, y, size, color);
    }
}
